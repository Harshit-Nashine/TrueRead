// SentisInferenceManager.cs — v6  (PERFORMANCE + FULL CHARACTER FIX)
//
// ══════════════════════════════════════════════════════════════════════════════
// WHAT WAS WRONG IN v5 AND WHY
//
// Problem 1 — Half character in LargestBlob (e.g. "द" losing top loop):
//   MorphOpen uses 3×3 erosion — a pixel stays white ONLY if all 8 neighbours
//   are white. "द" has a thin connecting stroke (~2-3px wide at 660px resolution)
//   linking the top loop to the bottom stem. Erosion severed this connection.
//   LargestComponent then saw TWO separate blobs and discarded the smaller one.
//   FIX: Remove MorphOpen entirely. LargestComponent already eliminates noise
//   blobs — MorphOpen is redundant and harmful for thin Devanagari strokes.
//
// Problem 2 — Phone overheating / laggy camera (Tecno Camon 20 4G / Helio G85):
//   BFS ran on the full camera crop (~660×660 = 435,000 pixels).
//   Each BFS iteration allocates and grows a List<int> per component.
//   PNG encoding + file I/O on every scan (debug saves) added more CPU/IO load.
//   FIX A: Downsample to 256×256 immediately after grayscale.
//          All subsequent operations (median, Otsu, invert, BFS, bbox) run on
//          65,536 pixels instead of 435,000 — 6.6× less work.
//   FIX B: BFS uses pre-allocated arrays instead of List<int> per component.
//   FIX C: Debug saves now controlled per-step. Disable when done testing.
//
// NEW PIPELINE (v6):
//   1. Grayscale            (full resolution — fast single pass)
//   2. Downsample → 256×256 (bilinear — reduces ALL subsequent work 6.6×)
//   3. Median blur 3×3      (on 256×256 — removes paper texture noise)
//   4. Otsu binarization    (on 256×256)
//   5. Auto-inversion       (on 256×256)
//   6. LargestComponent     (on 256×256 — removes edge noise, keeps full char)
//   7. BBox crop + 15% pad  (on 256×256)
//   8. Aspect-ratio pad     (on 256×256)
//   9. Resize → 64×64       (bilinear — final model input)
//  10. Final Otsu pass      (on 64×64 — removes resize blur)
// ══════════════════════════════════════════════════════════════════════════════

using UnityEngine;
using Unity.Sentis;
using System.IO;
using System.Collections.Generic;

public class SentisInferenceManager : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Model")]
    public ModelAsset modelAsset;

    [Header("Tensor Names — from Colab Cell 16")]
    public string inputTensorName  = "input";
    public string outputTensorName = "predictions";

    [Header("Confidence Gate")]
    [Range(0.5f, 0.99f)]
    public float minConfidence = 0.70f;

    [Header("Debug — disable when done testing to reduce CPU/IO load")]
    [Tooltip("Master switch — turn OFF in production to stop file I/O on every scan")]
    public bool enableDebugSave = false;

    [Tooltip("Which steps to save (only used when enableDebugSave = true).\n" +
             "Disable steps you've already verified to reduce I/O.\n" +
             "Files saved to: Android/data/<package>/files/")]
    public bool debugSaveGray       = true;
    public bool debugSaveMedian     = false;  // rarely needed once verified
    public bool debugSaveBinary     = true;
    public bool debugSaveInverted   = true;
    public bool debugSaveLargest    = true;   // most important step to verify
    public bool debugSaveCropped    = false;
    public bool debugSaveFinal      = true;

    // ─── Constants ────────────────────────────────────────────────────────────
    private const int INPUT_SIZE    = 64;    // model input — do not change
    private const int WORK_SIZE     = 256;   // internal processing resolution
    private const int NUM_CLASSES   = 46;

    // ─── Private ──────────────────────────────────────────────────────────────
    private Model  _runtimeModel;
    private Worker _worker;
    private bool   _isReady = false;

    // Pre-allocated BFS buffers — allocated once in Awake, reused every scan
    // Avoids per-scan heap allocation which was causing GC pressure on Helio G85
    private bool[]  _bfsVisited;
    private int[]   _bfsQueue;
    private int[]   _bestPixels;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    void Awake()
    {
        if (modelAsset == null)
        {
            Debug.LogError("[Sentis] modelAsset is NULL. Drag .onnx into Inspector.");
            return;
        }

        _runtimeModel = ModelLoader.Load(modelAsset);

        try
        {
            _worker = new Worker(_runtimeModel, BackendType.GPUCompute);
            Debug.Log("[Sentis] Worker: GPUCompute");
        }
        catch
        {
            Debug.LogWarning("[Sentis] GPUCompute unavailable — using CPU.");
            _worker = new Worker(_runtimeModel, BackendType.CPU);
        }

        // Pre-allocate BFS buffers for WORK_SIZE×WORK_SIZE (256×256 = 65536 pixels)
        int workPixels = WORK_SIZE * WORK_SIZE;
        _bfsVisited  = new bool[workPixels];
        _bfsQueue    = new int[workPixels];
        _bestPixels  = new int[workPixels];

        WarmUp();
        _isReady = true;
        Debug.Log("[Sentis] Ready. " +
                  (enableDebugSave ? "Debug saves ON." : "Debug saves OFF (production mode)."));
    }

    void OnDestroy() => _worker?.Dispose();

    // ─── Public API ──────────────────────────────────────────────────────────
    public (int classIndex, float confidence) RunInference(Texture2D inputTexture)
    {
        if (!_isReady || inputTexture == null)
        {
            Debug.LogError("[Sentis] Not ready or null texture.");
            return (-1, 0f);
        }

        float[] pixelData = PreprocessTexture(inputTexture);

        (int bestIndex, float bestConf) = ExecuteInference(pixelData);

        if (bestConf < minConfidence)
        {
            Debug.Log($"[Sentis] Rejected — {bestConf:P0} < threshold {minConfidence:P0}. " +
                      $"Best guess index={bestIndex}");
            return (-1, bestConf);
        }

        Debug.Log($"[Sentis] ✅ Accepted — class {bestIndex} at {bestConf:P0}");
        return (bestIndex, bestConf);
    }

    // ─── Warm-up ──────────────────────────────────────────────────────────────
    void WarmUp()
    {
        ExecuteInference(new float[INPUT_SIZE * INPUT_SIZE]);
        Debug.Log("[Sentis] Warm-up done.");
    }

    // ─── Inference (Sentis 2.x API) ───────────────────────────────────────────
    (int, float) ExecuteInference(float[] data)
    {
        var shape = new TensorShape(1, INPUT_SIZE, INPUT_SIZE, 1);
        var input = new Tensor<float>(shape, data);

        _worker.SetInput(inputTensorName, input);
        _worker.Schedule();
        input.Dispose();

        var outputGPU = _worker.PeekOutput(outputTensorName) as Tensor<float>;
        var output    = outputGPU.ReadbackAndClone();

        int   best     = 0;
        float bestConf = 0f;
        for (int i = 0; i < NUM_CLASSES; i++)
        {
            float v = output[i];
            if (v > bestConf) { bestConf = v; best = i; }
        }

        output.Dispose();
        return (best, bestConf);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PREPROCESSING PIPELINE — v6
    // ══════════════════════════════════════════════════════════════════════════
    float[] PreprocessTexture(Texture2D source)
    {
        string ts  = enableDebugSave ? System.DateTime.Now.ToString("HHmmss") : "";
        Color32[] px  = source.GetPixels32();
        int       srcW = source.width;
        int       srcH = source.height;

        // ── Step 1: Grayscale (full resolution) ───────────────────────────────
        float[] gray = new float[srcW * srcH];
        for (int i = 0; i < px.Length; i++)
            gray[i] = (0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b) / 255f;

        if (enableDebugSave && debugSaveGray)
            SaveFloat(gray, srcW, srcH, $"{ts}_1_Gray.png");

        // ── Step 2: Downsample to WORK_SIZE × WORK_SIZE (256×256) ─────────────
        // WHY: All subsequent steps (median, Otsu, BFS) are O(pixels).
        // Running on 660×660 = 435k pixels. On 256×256 = 65k pixels.
        // This is 6.6× less work — eliminates phone overheating on Helio G85.
        // The character structure is fully preserved at 256×256 resolution.
        float[] work = BilinearResizeFloat(gray, srcW, srcH, WORK_SIZE, WORK_SIZE);
        int     wW   = WORK_SIZE, wH = WORK_SIZE;

        // ── Step 3: 3×3 Median blur ───────────────────────────────────────────
        // Removes paper texture noise before Otsu without blurring stroke edges.
        float[] blurred = MedianBlur3x3(work, wW, wH);

        if (enableDebugSave && debugSaveMedian)
            SaveFloat(blurred, wW, wH, $"{ts}_2_Median.png");

        // ── Step 4: Otsu binarization ─────────────────────────────────────────
        float  otsu   = ComputeOtsuThreshold(blurred);
        byte[] binary = new byte[wW * wH];
        for (int i = 0; i < blurred.Length; i++)
            binary[i] = blurred[i] >= otsu ? (byte)255 : (byte)0;

        if (enableDebugSave && debugSaveBinary)
            SaveByte(binary, wW, wH, $"{ts}_3_Binary.png");

        // ── Step 5: Auto-inversion ────────────────────────────────────────────
        float mean = 0f;
        for (int i = 0; i < binary.Length; i++) mean += binary[i];
        mean /= binary.Length;
        if (mean > 128f)
            for (int i = 0; i < binary.Length; i++)
                binary[i] = (byte)(255 - binary[i]);

        if (enableDebugSave && debugSaveInverted)
            SaveByte(binary, wW, wH, $"{ts}_4_Inverted.png");

        // ── Step 6: Largest connected component ───────────────────────────────
        // NOTE: MorphOpen (erode+dilate) was REMOVED from v5.
        // It was severing thin Devanagari strokes (e.g. "द" top loop connection),
        // causing LargestComponent to keep only half the character.
        // LargestComponent alone handles noise without damaging thin strokes.
        byte[] charOnly = LargestConnectedComponent(binary, wW, wH);

        if (enableDebugSave && debugSaveLargest)
            SaveByte(charOnly, wW, wH, $"{ts}_5_LargestBlob.png");

        // ── Step 7: Bounding box crop + 15% safety padding ───────────────────
        int minX = wW, maxX = 0, minY = wH, maxY = 0;
        bool found = false;
        for (int y = 0; y < wH; y++)
            for (int x = 0; x < wW; x++)
                if (charOnly[y * wW + x] > 0)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    found = true;
                }

        if (!found)
        {
            Debug.LogWarning("[Sentis] No character found after preprocessing. " +
                             "Check lighting and ensure character is inside the scanner box.");
            return new float[INPUT_SIZE * INPUT_SIZE];
        }

        int bboxW = maxX - minX + 1, bboxH = maxY - minY + 1;
        int pad   = Mathf.RoundToInt(Mathf.Max(bboxW, bboxH) * 0.15f);
        int x1 = Mathf.Max(0, minX - pad),      y1 = Mathf.Max(0, minY - pad);
        int x2 = Mathf.Min(wW-1, maxX + pad),   y2 = Mathf.Min(wH-1, maxY + pad);
        int cW  = x2 - x1 + 1,                  cH = y2 - y1 + 1;

        byte[] cropped = new byte[cW * cH];
        for (int y = 0; y < cH; y++)
            for (int x = 0; x < cW; x++)
                cropped[y * cW + x] = charOnly[(y1+y) * wW + (x1+x)];

        if (enableDebugSave && debugSaveCropped)
            SaveByte(cropped, cW, cH, $"{ts}_6_Cropped.png");

        // ── Step 8: Aspect-ratio square padding ───────────────────────────────
        int sq   = Mathf.Max(cW, cH);
        int offX = (sq - cW) / 2, offY = (sq - cH) / 2;
        byte[] padded = new byte[sq * sq];
        for (int y = 0; y < cH; y++)
            for (int x = 0; x < cW; x++)
                padded[(y + offY) * sq + (x + offX)] = cropped[y * cW + x];

        // ── Step 9: Resize to 64×64 ───────────────────────────────────────────
        float[] resized = BilinearResizeByte(padded, sq, sq, INPUT_SIZE, INPUT_SIZE);

        // ── Step 10: Final Otsu pass (removes resize interpolation blur) ───────
        float  ft    = ComputeOtsuThreshold(resized);
        float[] final = new float[INPUT_SIZE * INPUT_SIZE];
        for (int i = 0; i < final.Length; i++)
            final[i] = resized[i] >= ft ? 1f : 0f;

        if (enableDebugSave && debugSaveFinal)
            SaveFloat(final, INPUT_SIZE, INPUT_SIZE, $"{ts}_7_Final64.png");

        // Health check — white pixel % should be 5–35% for a single character
        float whitePct = 0f;
        for (int i = 0; i < final.Length; i++) whitePct += final[i];
        whitePct = whitePct / final.Length * 100f;
        Debug.Log($"[Sentis] Done. White pixel %: {whitePct:F1}% " +
                  $"(healthy: 5–35%. Outside range = check LargestBlob debug image)");

        return final;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PREPROCESSING HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bilinear resize for float arrays (used for initial downsample).
    /// </summary>
    float[] BilinearResizeFloat(float[] src, int srcW, int srcH, int dstW, int dstH)
    {
        float[] dst    = new float[dstW * dstH];
        float   xScale = (float)srcW / dstW;
        float   yScale = (float)srcH / dstH;

        for (int y = 0; y < dstH; y++)
        {
            for (int x = 0; x < dstW; x++)
            {
                float gx = x * xScale, gy = y * yScale;
                int   gxi = (int)gx,   gyi = (int)gy;
                float fx = gx - gxi,   fy = gy - gyi;
                int x0 = Mathf.Min(gxi,   srcW-1), x1c = Mathf.Min(gxi+1, srcW-1);
                int y0 = Mathf.Min(gyi,   srcH-1), y1c = Mathf.Min(gyi+1, srcH-1);
                dst[y*dstW+x] = src[y0*srcW+x0]*(1-fx)*(1-fy)
                              + src[y0*srcW+x1c]*fx*(1-fy)
                              + src[y1c*srcW+x0]*(1-fx)*fy
                              + src[y1c*srcW+x1c]*fx*fy;
            }
        }
        return dst;
    }

    /// <summary>
    /// Bilinear resize for byte arrays (used for final 64×64 resize).
    /// </summary>
    float[] BilinearResizeByte(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        float[] dst    = new float[dstW * dstH];
        float   xScale = (float)srcW / dstW;
        float   yScale = (float)srcH / dstH;

        for (int y = 0; y < dstH; y++)
        {
            for (int x = 0; x < dstW; x++)
            {
                float gx = x * xScale, gy = y * yScale;
                int   gxi = (int)gx,   gyi = (int)gy;
                float fx = gx - gxi,   fy = gy - gyi;
                int x0 = Mathf.Min(gxi,   srcW-1), x1c = Mathf.Min(gxi+1, srcW-1);
                int y0 = Mathf.Min(gyi,   srcH-1), y1c = Mathf.Min(gyi+1, srcH-1);
                dst[y*dstW+x] = src[y0*srcW+x0]/255f*(1-fx)*(1-fy)
                              + src[y0*srcW+x1c]/255f*fx*(1-fy)
                              + src[y1c*srcW+x0]/255f*(1-fx)*fy
                              + src[y1c*srcW+x1c]/255f*fx*fy;
            }
        }
        return dst;
    }

    /// <summary>
    /// 3×3 median filter — removes salt-and-pepper noise without blurring strokes.
    /// </summary>
    float[] MedianBlur3x3(float[] pixels, int w, int h)
    {
        float[] result = new float[w * h];
        float[] window = new float[9];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int k = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = Mathf.Clamp(x + dx, 0, w - 1);
                        int ny = Mathf.Clamp(y + dy, 0, h - 1);
                        window[k++] = pixels[ny * w + nx];
                    }
                System.Array.Sort(window);
                result[y * w + x] = window[4];
            }
        }
        return result;
    }

    /// <summary>
    /// Otsu's threshold — maximises inter-class variance between ink and paper.
    /// No manual tuning needed — adapts to any lighting condition.
    /// </summary>
    float ComputeOtsuThreshold(float[] pixels)
    {
        int n = pixels.Length;
        int[] hist = new int[256];
        for (int i = 0; i < n; i++)
            hist[Mathf.Clamp((int)(pixels[i]*255f), 0, 255)]++;

        float total = 0f;
        for (int i = 0; i < 256; i++) total += i * hist[i];

        float sumBg = 0, wBg = 0, maxVar = 0f;
        int bestT = 128;
        for (int t = 0; t < 256; t++)
        {
            wBg += hist[t]; if (wBg == 0) continue;
            float wFg = n - wBg; if (wFg == 0) break;
            sumBg += t * hist[t];
            float mBg = sumBg / wBg, mFg = (total - sumBg) / wFg;
            float d = mBg - mFg, v = wBg * wFg * d * d;
            if (v > maxVar) { maxVar = v; bestT = t; }
        }
        return bestT / 255f;
    }

    /// <summary>
    /// BFS connected component — keeps only the largest white blob.
    ///
    /// PERFORMANCE IMPROVEMENTS over v5:
    /// - Uses pre-allocated _bfsVisited, _bfsQueue, _bestPixels arrays (no heap alloc)
    /// - Manual queue with head/tail pointers instead of Queue<int> (no GC pressure)
    /// - Runs on WORK_SIZE×WORK_SIZE (256×256) instead of full camera resolution
    ///
    /// WHY MorphOpen was removed:
    /// Erosion (part of MorphOpen) requires all 8 neighbours to be white.
    /// Thin Devanagari connecting strokes fail this test and get severed.
    /// LargestComponent alone handles noise because small noise blobs (< character size)
    /// are naturally discarded as the "non-largest" components.
    /// </summary>
    byte[] LargestConnectedComponent(byte[] binary, int w, int h)
    {
        int n = w * h;

        // Clear pre-allocated visited buffer
        System.Array.Clear(_bfsVisited, 0, n);

        int bestSize  = 0;
        int bestCount = 0;

        for (int startIdx = 0; startIdx < n; startIdx++)
        {
            if (_bfsVisited[startIdx] || binary[startIdx] == 0) continue;

            // BFS using pre-allocated array as circular queue
            int head = 0, tail = 0;
            _bfsQueue[tail++] = startIdx;
            _bfsVisited[startIdx] = true;
            int size = 0;

            while (head < tail)
            {
                int curr = _bfsQueue[head++];
                size++;

                int cy = curr / w, cx = curr % w;

                // 4-connected neighbours
                if (cy > 0)   TryEnqueueFast(cy-1, cx, w, binary, _bfsVisited, _bfsQueue, ref tail);
                if (cy < h-1) TryEnqueueFast(cy+1, cx, w, binary, _bfsVisited, _bfsQueue, ref tail);
                if (cx > 0)   TryEnqueueFast(cy, cx-1, w, binary, _bfsVisited, _bfsQueue, ref tail);
                if (cx < w-1) TryEnqueueFast(cy, cx+1, w, binary, _bfsVisited, _bfsQueue, ref tail);
            }

            if (size > bestSize)
            {
                bestSize  = size;
                bestCount = tail; // number of pixels in this component
                // Copy current component pixels to _bestPixels
                System.Array.Copy(_bfsQueue, 0, _bestPixels, 0, tail);
            }
        }

        byte[] result = new byte[n];
        for (int i = 0; i < bestCount; i++)
            result[_bestPixels[i]] = 255;

        Debug.Log($"[Sentis] LargestBlob: {bestSize} px out of {n} total. " +
                  $"({(bestSize * 100f / n):F1}% of image)");

        return result;
    }

    void TryEnqueueFast(int y, int x, int w, byte[] binary,
                        bool[] visited, int[] queue, ref int tail)
    {
        int idx = y * w + x;
        if (visited[idx] || binary[idx] == 0) return;
        visited[idx]  = true;
        queue[tail++] = idx;
    }

    // ─── Debug Savers ─────────────────────────────────────────────────────────
    void SaveFloat(float[] data, int w, int h, string filename)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var col = new Color[w * h];
        for (int i = 0; i < data.Length; i++) { float v = data[i]; col[i] = new Color(v,v,v); }
        tex.SetPixels(col); tex.Apply();
        File.WriteAllBytes(Path.Combine(Application.persistentDataPath, filename), tex.EncodeToPNG());
        Destroy(tex);
        Debug.Log($"[Sentis] Debug saved: {filename}");
    }

    void SaveByte(byte[] data, int w, int h, string filename)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var col = new Color[w * h];
        for (int i = 0; i < data.Length; i++) { float v = data[i]/255f; col[i] = new Color(v,v,v); }
        tex.SetPixels(col); tex.Apply();
        File.WriteAllBytes(Path.Combine(Application.persistentDataPath, filename), tex.EncodeToPNG());
        Destroy(tex);
        Debug.Log($"[Sentis] Debug saved: {filename}");
    }
}