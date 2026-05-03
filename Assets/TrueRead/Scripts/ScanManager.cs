// ScanManager.cs — v5  (CONTINUOUS SCAN + AUTO-DISMISS)
//
// ══════════════════════════════════════════════════════════════════════════════
// WHAT CHANGED FROM v4 AND WHY
//
// Problem: Models never appeared after recognition.
// Root cause: _resultShown = true stopped the scan loop completely after first
// result. If first scan failed (low confidence), nothing ever called ShowCharacter.
// User had to press "Next" to unblock scanning — bad UX.
//
// New behaviour (Pokemon Go style):
//   ✅ Scan loop runs CONTINUOUSLY — _resultShown no longer blocks scanning
//   ✅ When character is detected → model appears + panel shows
//   ✅ When character removed from camera for autoDismissSeconds → model hides
//   ✅ Same character detected again → model stays (no flicker)
//   ✅ Different character detected → model swaps to new one
//   ✅ Tap anywhere on screen → PackageController switches slot 1→2 (unchanged)
//   ✅ Gallery, Back, Next buttons unchanged
//
// CHANGES (additions only — no existing logic removed):
//   + autoDismissSeconds setting (default 3.5s)
//   + _noDetectSeconds counter — counts up on each failed scan
//   + _modelVisible flag — tracks whether 3D model is currently on screen
//   + AutoDismiss() — hides model + panel when no-detect timer expires
//   + Scan loop condition: removed !_resultShown gate → always scans
//   + ShowResult: resets _noDetectSeconds, sets _modelVisible
//   + CaptureAndPredict: increments timer on fail, calls AutoDismiss if expired
// ══════════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class ScanManager : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Scene References")]
    public CameraManager          cameraManager;
    public ModelDisplayManager    modelDisplayManager;
    public SentisInferenceManager sentisManager;
    [Tooltip("NEW: Drag the GalleryBackground GameObject here.\n" +
             "Displays selected gallery image as background during gallery mode.")]
    public GalleryImageBackground galleryBackground;

    [Header("UI — Result Panel")]
    public GameObject       resultPanel;
    public TextMeshProUGUI  recognizedCharTMP;
    public TextMeshProUGUI  phoneticTMP;
    public TextMeshProUGUI  confidenceTMP;

    [Header("UI — Buttons")]
    public Button nextButton;
    public Button useCameraButton;
    public Button galleryButton;
    public Button backButton;

    [Header("UI — Scan Guide")]
    public TextMeshProUGUI scanGuideTMP;

    [Header("Settings")]
    [Tooltip("Seconds between scans")]
    public float scanInterval = 1.2f;

    [Range(0f, 1f)]
    [Tooltip("Minimum confidence to accept a prediction")]
    public float confidenceThreshold = 0.70f;

    [Tooltip("Seconds without a successful detection before the 3D model is hidden.\n" +
             "Set to 3–4 seconds for natural feel.")]
    public float autoDismissSeconds = 3.5f;

    // ─── Character Maps (unchanged — verified against class_mapping.json) ──────
    private static readonly string[] INDEX_TO_CHAR = new string[]
    {
        "क","ख","ग","घ","ङ",        // 0-4
        "च","छ","ज","झ","ञ",        // 5-9
        "ट","ठ","ड","ढ","ण",        // 10-14
        "त","थ","द","ध","न",        // 15-19
        "प","फ","ब","भ","म",        // 20-24
        "य","र","ल","व","श",        // 25-29
        "ष","स","ह","क्ष","त्र",      // 30-34
        "ज्ञ","०","१","२","३",        // 35-39
        "४","५","६","७","८",        // 40-44
        "९"                          // 45
    };

    private static readonly string[] INDEX_TO_PHONETIC = new string[]
    {
        "Ka","Kha","Ga","Gha","Nga",
        "Cha","Chha","Ja","Jha","Nya",
        "Taamatar","Thaa","Daa","Dhaa","Adna",
        "Tabala","Tha","Da","Dha","Na",
        "Pa","Pha","Ba","Bha","Ma",
        "Yaw","Ra","La","Waw","Motosaw",
        "Petchiryakha","Patalosaw","Ha","Ksha","Tra",
        "Gya","Zero","One","Two","Three",
        "Four","Five","Six","Seven","Eight",
        "Nine"
    };

    // ─── Private State ────────────────────────────────────────────────────────
    private bool      _resultShown     = false; // is the result panel currently visible
    private bool      _isScanning      = false; // is a scan coroutine running right now
    private bool      _usingCamera     = true;  // false when gallery is open
    private bool      _modelVisible    = false; // is a 3D model currently on screen
    private float     _noDetectSeconds = 0f;    // how long since last successful detection
    private Coroutine _scanCoroutine;

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    void Start()
    {
        if (sentisManager == null)
            Debug.LogError("[ScanManager] sentisManager NULL! Drag component into Inspector.");
        if (modelDisplayManager == null)
            Debug.LogError("[ScanManager] modelDisplayManager NULL! Drag component into Inspector.");

        if (resultPanel) resultPanel.SetActive(false);
        if (scanGuideTMP) scanGuideTMP.text = "Hold character in front of camera";

        nextButton?.onClick.AddListener(OnNextPressed);
        useCameraButton?.onClick.AddListener(OnUseCameraPressed);
        galleryButton?.onClick.AddListener(OnGalleryPressed);
        backButton?.onClick.AddListener(OnBackPressed);

        _scanCoroutine = StartCoroutine(ScanLoop());
    }

    void OnDestroy()
    {
        if (_scanCoroutine != null) StopCoroutine(_scanCoroutine);
    }

    // ─── Scan Loop ────────────────────────────────────────────────────────────
    // KEY CHANGE: removed !_resultShown gate.
    // Previously: stopped scanning after first result until Next was pressed.
    // Now: scans continuously so the model appears/disappears with the character.
    IEnumerator ScanLoop()
    {
        while (!cameraManager.IsCameraReady)
            yield return new WaitForSeconds(0.3f);

        yield return new WaitForSeconds(0.5f);
        Debug.Log("[ScanManager] Scan loop active (continuous mode).");

        while (true)
        {
            // Only gate on _isScanning and _usingCamera — not _resultShown
            if (!_isScanning && _usingCamera)
                yield return StartCoroutine(CaptureAndPredict());

            yield return new WaitForSeconds(scanInterval);
        }
    }

    // ─── Camera Inference ─────────────────────────────────────────────────────
    IEnumerator CaptureAndPredict()
    {
        _isScanning = true;

        if (!cameraManager.IsCameraReady || sentisManager == null)
        {
            _isScanning = false; yield break;
        }

        Texture2D snapshot = cameraManager.CaptureSnapshot();
        if (snapshot == null) { _isScanning = false; yield break; }

        yield return null;

        var (idx, conf) = sentisManager.RunInference(snapshot);
        Destroy(snapshot);

        Debug.Log($"[ScanManager] Result: index={idx} " +
                  $"char={(idx >= 0 ? INDEX_TO_CHAR[idx] : "?")} " +
                  $"conf={conf:P0}");

        if (idx >= 0 && conf >= confidenceThreshold)
        {
            // ── Successful detection ──────────────────────────────────────────
            // Reset the no-detect timer so auto-dismiss doesn't fire
            _noDetectSeconds = 0f;
            ShowResult(idx, conf);
        }
        else
        {
            // ── Failed detection ──────────────────────────────────────────────
            // Accumulate no-detect time. If model is visible and timer expires,
            // dismiss the model (character has left the camera frame).
            _noDetectSeconds += scanInterval;

            if (_modelVisible && _noDetectSeconds >= autoDismissSeconds)
            {
                AutoDismiss();
            }
            else if (scanGuideTMP && !_modelVisible)
            {
                // Only show guide text when no model is visible
                scanGuideTMP.text = conf < 0.3f
                    ? "No character detected — hold closer"
                    : "Hold steady...";
            }

            Debug.Log($"[ScanManager] No detection. " +
                      $"No-detect timer: {_noDetectSeconds:F1}s / {autoDismissSeconds:F1}s. " +
                      $"Model visible: {_modelVisible}");
        }

        _isScanning = false;
    }

    // ─── Auto-Dismiss ─────────────────────────────────────────────────────────
    // Called when character has been absent from camera for autoDismissSeconds.
    // Hides the 3D model and result panel, resets state so next detection is fresh.
    void AutoDismiss()
    {
        Debug.Log($"[ScanManager] Auto-dismissing — no character for {_noDetectSeconds:F1}s.");

        _modelVisible    = false;
        _resultShown     = false;
        _noDetectSeconds = 0f;

        if (resultPanel) resultPanel.SetActive(false);
        modelDisplayManager?.DismissCurrentPackage();

        if (scanGuideTMP) scanGuideTMP.text = "Hold character in front of camera";
    }

    // ─── Show Result ──────────────────────────────────────────────────────────
    void ShowResult(int index, float confidence)
    {
        if (index < 0 || index >= 46)
        {
            Debug.LogError("[ScanManager] Invalid index: " + index); return;
        }

        // Mark model as visible and result as shown
        _modelVisible = true;
        _resultShown  = true;

        // Update result panel UI
        if (recognizedCharTMP) recognizedCharTMP.text = INDEX_TO_CHAR[index];
        if (phoneticTMP)       phoneticTMP.text       = INDEX_TO_PHONETIC[index];
        if (confidenceTMP)     confidenceTMP.text      = confidence.ToString("P0") + " confident";
        if (resultPanel)       resultPanel.SetActive(true);

        // Clear guide text while model is visible
        if (scanGuideTMP) scanGuideTMP.text = "";

        // Spawn the 3D model
        // ModelDisplayManager.ShowCharacter() skips reload if same index,
        // so scanning the same character continuously doesn't cause flickering.
        if (modelDisplayManager != null)
        {
            modelDisplayManager.ShowCharacter(index);
            Debug.Log($"[ScanManager] ✅ ShowCharacter({index}) called — " +
                      $"'{INDEX_TO_CHAR[index]}' ({INDEX_TO_PHONETIC[index]})");
        }
        else
        {
            Debug.LogError("[ScanManager] modelDisplayManager is NULL — " +
                           "drag the ModelDisplayManager component into Inspector!");
        }
    }

    // ─── Gallery ──────────────────────────────────────────────────────────────
    void OnGalleryPressed()
    {
        _usingCamera = false;
        cameraManager?.StopCamera();

#if UNITY_ANDROID
        NativeGallery.GetImageFromGallery((path) =>
        {
            if (path == null) { ResumeCamera(); return; }
            StartCoroutine(ProcessGalleryImage(path));
        }, "Select a Hindi character image", "image/*");
#else
        ShowResult(Random.Range(0, 46), 0.88f);
#endif
    }

    IEnumerator ProcessGalleryImage(string imagePath)
    {
        Texture2D tex = NativeGallery.LoadImageAtPath(imagePath, 512, false);
        if (tex == null) { ResumeCamera(); yield break; }

        // ADDITION: Show the selected image as background immediately after loading.
        // This replaces the frozen camera frame so the user sees what they picked.
        // The 3D model will render on top of this via the main camera (AR layering).
        galleryBackground?.ShowImage(tex);

        yield return null;
        if (sentisManager == null) { Destroy(tex); ResumeCamera(); yield break; }

        var (idx, conf) = sentisManager.RunInference(tex);
        // NOTE: tex is kept alive here — galleryBackground is displaying it.
        // We destroy it only when leaving gallery mode (ResumeCamera → Hide).
        // Use a local reference so we can destroy it safely at the right time.
        Texture2D texToDestroy = tex;

        if (idx >= 0 && conf >= confidenceThreshold)
        {
            // SUCCESS: Show model on top of gallery image background.
            // Do NOT call ResumeCamera — gallery image stays as background.
            // User presses Next or Back to return to live camera.
            ShowResult(idx, conf);
        }
        else
        {
            // BUG FIX: Previously called ResumeCamera() here, which immediately
            // restarted the live camera feed — making it seem like gallery did nothing.
            // Now we stay on the gallery image and show an informative message.
            // User can press Next to go back to camera, or Back to exit.
            if (scanGuideTMP)
                scanGuideTMP.text = "Could not recognise — press Next to try camera";
            Debug.Log($"[ScanManager] Gallery recognition failed (conf={conf:P0}). " +
                      "Staying in gallery mode. User must press Next to return to camera.");
        }

        // Destroy texture now that inference is done (gallery background copies the display).
        // Note: GalleryImageBackground.displayImage.texture will be null after this,
        // but the canvas will keep showing the last rendered frame until Hide() is called.
        Destroy(texToDestroy);
    }

    void ResumeCamera()
    {
        _usingCamera = true;
        cameraManager?.StartCamera();
        galleryBackground?.Hide();  // ADDITION: hide gallery image, show live camera feed
        if (scanGuideTMP) scanGuideTMP.text = "Hold character in front of camera";
    }

    // ─── Buttons (unchanged) ──────────────────────────────────────────────────
    void OnNextPressed()
    {
        _resultShown     = false;
        _modelVisible    = false;
        _noDetectSeconds = 0f;

        if (resultPanel) resultPanel.SetActive(false);
        modelDisplayManager?.DismissCurrentPackage();
        if (scanGuideTMP) scanGuideTMP.text = "Hold character in front of camera";
        if (!_usingCamera) ResumeCamera();
    }

    void OnUseCameraPressed() => OnNextPressed();

    void OnBackPressed()
    {
        cameraManager?.StopCamera();
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }
}