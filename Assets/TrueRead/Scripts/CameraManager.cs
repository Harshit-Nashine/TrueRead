// CameraManager.cs — v4  (HORIZONTAL FLIP FIX)
//
// ══════════════════════════════════════════════════════════════════
// ROOT CAUSE OF "ह predicted as ३":
//
//   Unity's WebCamTexture.GetPixels32() returns pixels with row-0
//   at the BOTTOM (bottom-left origin). The 90° rotation math below
//   assumed top-left origin. Result: every captured image was
//   HORIZONTALLY MIRRORED before being sent to the model.
//
//   "ह" (ha, index 32) when mirrored horizontally looks like "३"
//   (digit 3, index 39). The model was correct — it saw a mirrored
//   character and correctly identified it. The INPUT was wrong.
//
// FIX: After the rotation switch, apply one horizontal flip pass.
//      This makes the pixel buffer match what the user sees on screen
//      and what the model was trained on.
// ══════════════════════════════════════════════════════════════════
//
// OTHER THINGS IN THIS FILE:
//   - AspectRatioFitter (EnvelopeParent) — fixes camera squeeze
//   - UPI-style scanner box overlay
//   - saveRawSnapshotDebug flag — saves oriented snapshot BEFORE
//     preprocessing so you can confirm orientation is now correct

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Android;
using System.IO;

public class CameraManager : MonoBehaviour
{
    [Header("Camera Feed")]
    public RawImage rawImageFeed;

    [Header("Scanner Box Overlay")]
    [Tooltip("Drag a UI Image here to show a UPI-style scan guide box")]
    public Image scannerBoxImage;

    [Header("Camera Settings")]
    public int cameraWidth  = 1280;
    public int cameraHeight = 720;
    public int cameraFPS    = 30;

    [Header("Debug")]
    [Tooltip("Saves the oriented snapshot (after flip fix, before preprocessing) as PNG.\n" +
             "Check: does the character look correct (not mirrored) in the saved file?")]
    public bool saveRawSnapshotDebug = false;

    // ─── Private ─────────────────────────────────────────────────────────────
    private WebCamTexture     _webcamTexture;
    private AspectRatioFitter _aspectFitter;
    private bool _cameraStarted = false;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    void Start()
    {
        if (rawImageFeed != null)
        {
            _aspectFitter = rawImageFeed.GetComponent<AspectRatioFitter>()
                         ?? rawImageFeed.gameObject.AddComponent<AspectRatioFitter>();
            _aspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        }

        if (scannerBoxImage != null)
        {
            var r = scannerBoxImage.rectTransform;
            r.anchorMin  = new Vector2(0.5f, 0.5f);
            r.anchorMax  = new Vector2(0.5f, 0.5f);
            r.pivot      = new Vector2(0.5f, 0.5f);
            r.sizeDelta  = new Vector2(320f, 320f);
            scannerBoxImage.color = new Color(0.98f, 0.45f, 0.09f, 0.0f);
        }

        RequestCameraPermission();
    }

    // ─── Permission ───────────────────────────────────────────────────────────
    public void RequestCameraPermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            var cb = new PermissionCallbacks();
            cb.PermissionGranted += OnPermissionGranted;
            cb.PermissionDenied  += OnPermissionDenied;
            Permission.RequestUserPermission(Permission.Camera, cb);
        }
        else { StartCamera(); }
#else
        StartCamera();
#endif
    }

    public void OnPermissionGranted(string p) => StartCamera();

    void OnPermissionDenied(string p)
    {
        Debug.LogError("[Camera] Permission DENIED.");
        if (rawImageFeed) rawImageFeed.color = Color.red;
    }

    // ─── Camera Control ───────────────────────────────────────────────────────
    public void StartCamera()
    {
        if (_webcamTexture != null && _webcamTexture.isPlaying)  { return; }
        if (_webcamTexture != null && !_webcamTexture.isPlaying) { _webcamTexture.Play(); _cameraStarted = true; return; }

        var devices = WebCamTexture.devices;
        if (devices.Length == 0) { Debug.LogError("[Camera] No camera found!"); return; }

        string name = "";
        foreach (var d in devices) if (!d.isFrontFacing) { name = d.name; break; }
        if (string.IsNullOrEmpty(name)) name = devices[0].name;

        _webcamTexture = new WebCamTexture(name, cameraWidth, cameraHeight, cameraFPS);
        rawImageFeed.texture = _webcamTexture;
        rawImageFeed.color   = Color.white;
        _webcamTexture.Play();
        _cameraStarted = true;
        Debug.Log($"[Camera] Started: {name}");
    }

    void Update()
    {
        if (_cameraStarted && _webcamTexture != null && _webcamTexture.didUpdateThisFrame)
            FixCameraRotation();
    }

    void FixCameraRotation()
    {
        if (!rawImageFeed || _webcamTexture == null) return;
        int  angle    = _webcamTexture.videoRotationAngle;
        bool mirrored = _webcamTexture.videoVerticallyMirrored;

        rawImageFeed.rectTransform.localEulerAngles = new Vector3(0, 0, -angle);
        rawImageFeed.rectTransform.localScale       = new Vector3(1f, mirrored ? -1f : 1f, 1f);

        if (_aspectFitter != null)
        {
            bool isRotated = angle == 90 || angle == 270;
            _aspectFitter.aspectRatio = isRotated
                ? (float)_webcamTexture.height / _webcamTexture.width
                : (float)_webcamTexture.width  / _webcamTexture.height;
        }
    }

    // ─── Snapshot (FIXED) ─────────────────────────────────────────────────────
    public Texture2D CaptureSnapshot()
    {
        if (_webcamTexture == null || !_webcamTexture.isPlaying)
        {
            Debug.LogWarning("[Camera] CaptureSnapshot: camera not running.");
            return null;
        }

        int       angle    = _webcamTexture.videoRotationAngle;
        bool      mirrored = _webcamTexture.videoVerticallyMirrored;
        int       srcW     = _webcamTexture.width;
        int       srcH     = _webcamTexture.height;
        Color32[] raw      = _webcamTexture.GetPixels32();

        // ── Step 1: Vertical flip if camera hardware reports mirroring ────────
        if (mirrored)
        {
            for (int y = 0; y < srcH / 2; y++)
                for (int x = 0; x < srcW; x++)
                {
                    int a = y * srcW + x, b = (srcH-1-y) * srcW + x;
                    Color32 t = raw[a]; raw[a] = raw[b]; raw[b] = t;
                }
        }

        // ── Step 2: Rotate to compensate for hardware orientation ─────────────
        Color32[] oriented;
        int outW, outH;

        switch (angle % 360)
        {
            case 90:
                outW = srcH; outH = srcW;
                oriented = new Color32[outW * outH];
                for (int y = 0; y < srcH; y++)
                    for (int x = 0; x < srcW; x++)
                        oriented[x * outW + (srcH - 1 - y)] = raw[y * srcW + x];
                break;

            case 180:
                outW = srcW; outH = srcH;
                oriented = new Color32[outW * outH];
                for (int y = 0; y < srcH; y++)
                    for (int x = 0; x < srcW; x++)
                        oriented[(srcH-1-y) * outW + (srcW-1-x)] = raw[y * srcW + x];
                break;

            case 270:
                outW = srcH; outH = srcW;
                oriented = new Color32[outW * outH];
                for (int y = 0; y < srcH; y++)
                    for (int x = 0; x < srcW; x++)
                        oriented[(srcW-1-x) * outW + y] = raw[y * srcW + x];
                break;

            default: // 0°
                outW = srcW; outH = srcH;
                oriented = raw;
                break;
        }

        // ══════════════════════════════════════════════════════════════════════
        // CRITICAL FIX — HORIZONTAL FLIP
        //
        // Unity WebCamTexture pixels are bottom-left origin. After the rotation
        // above, the image is horizontally mirrored vs what is displayed on screen.
        // This caused "ह" (ha) to appear as its mirror image "३" (digit 3).
        //
        // This flip makes the pixel buffer match the display AND the training data.
        // ══════════════════════════════════════════════════════════════════════
        Color32[] hFlipped = new Color32[outW * outH];
        for (int y = 0; y < outH; y++)
            for (int x = 0; x < outW; x++)
                hFlipped[y * outW + x] = oriented[y * outW + (outW - 1 - x)];
        // ══════════════════════════════════════════════════════════════════════

        // ── Step 3: Center-crop square to match scanner box ───────────────────
        // Crops the region inside the guide box so the model only sees
        // the character the user placed there, not background clutter.
        int cropSize = Mathf.RoundToInt(outW * 0.4f);
        int startX   = (outW - cropSize) / 2;
        int startY   = (outH - cropSize) / 2;

        Color32[] cropped = new Color32[cropSize * cropSize];
        for (int y = 0; y < cropSize; y++)
            for (int x = 0; x < cropSize; x++)
                cropped[y * cropSize + x] = hFlipped[(startY + y) * outW + (startX + x)];

        Texture2D result = new Texture2D(cropSize, cropSize, TextureFormat.RGB24, false);
        result.SetPixels32(cropped);
        result.Apply();

        // ── Debug: save oriented snapshot BEFORE preprocessing ────────────────
        // Look at this file — the character should look CORRECT (not mirrored).
        // If it still looks mirrored after this fix, your phone may need angle==0.
        if (saveRawSnapshotDebug)
        {
            string ts   = System.DateTime.Now.ToString("HHmmss");
            string path = Path.Combine(Application.persistentDataPath, $"{ts}_0_RawInput.png");
            File.WriteAllBytes(path, result.EncodeToPNG());
            Debug.Log($"[Camera] Saved raw snapshot (post-flip): {path}");
            Debug.Log("[Camera] ✅ CHECK: is the character correctly oriented in this PNG?");
        }

        return result;
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────────
    public void OnDisable() => StopCamera();
    void        OnDestroy() => StopCamera();

    public void StopCamera()
    {
        if (_webcamTexture != null && _webcamTexture.isPlaying) _webcamTexture.Stop();
        _cameraStarted = false;
    }

    public WebCamTexture GetWebCamTexture() => _webcamTexture;
    public bool IsCameraReady => _cameraStarted;
}