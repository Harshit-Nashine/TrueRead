// ScanSceneController.cs
// ============================================================================
// REPLACES: ARDisplaySetup.cs  — delete or disable ARDisplaySetup in your scene
//
// WHAT THIS FIXES
// ───────────────
//  BEFORE: Models rendered behind the camera-feed canvas (canvas always on top)
//  BEFORE: ARDisplaySetup caused a black strip from a mis-configured BG camera
//  BEFORE: Models spawned at wrong world position (top of frame, not centred)
//
//  AFTER:  Camera feed fills the ENTIRE screen (background)
//  AFTER:  3D CharPackage models float ON TOP of the feed (Pokémon-Go style)
//  AFTER:  Models are always centred on screen (ScreenToWorldPoint projection)
//  AFTER:  UI buttons / result panel stay above everything (Overlay)
//
// HOW TO SET UP
// ─────────────
//  1. Select the GameObject that has ARDisplaySetup → Inspector → disable/remove it
//  2. Add ScanSceneController to any GameObject in ScanScene (e.g. the GameManager)
//  3. Drag references into Inspector (3 slots: RawImage, UI Canvas, ModelDisplayManager)
//  4. Hit Play — done
//
// RENDERING ORDER (bottom → top)
// ──────────────────────────────
//  [depth -2]  ScanScene_BG_Camera   → clears to black, draws feed RawImage
//  [depth  0]  Main Camera           → Depth-only clear → keeps feed colour → draws 3D on top
//  [overlay]   UI Canvas             → ScreenSpaceOverlay → buttons / result always visible
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]   // run Awake() before CameraManager / ScanManager
public class ScanSceneController : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Required — drag from Hierarchy")]
    [Tooltip("The RawImage that shows the live WebCam feed.\n" +
             "It will be re-parented to a dedicated background canvas.")]
    public RawImage cameraFeedRawImage;

    [Tooltip("Your main UI Canvas (scan guide text, result panel, buttons).\n" +
             "Will be set to Screen Space – Overlay so it renders above 3D.")]
    public Canvas mainUICanvas;

    [Tooltip("The ModelDisplayManager that spawns CharPackage prefabs.")]
    public ModelDisplayManager modelDisplayManager;

    [Header("3D Model Screen Position")]
    [Tooltip("Distance from camera (metres) at which the 3D model is placed.\n" +
             "Smaller = model looks bigger.  Start at 3, adjust until it looks right.")]
    [Range(1f, 8f)]
    public float modelDistance = 3f;

    [Tooltip("Vertical screen fraction for spawn point.\n" +
             "0.5 = screen centre.  0.45 = slightly above centre (natural AR feel).")]
    [Range(0.2f, 0.8f)]
    public float screenCentreY = 0.45f;

    // ─── Private ──────────────────────────────────────────────────────────────

    private Camera _bgCam;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (!ValidateSetup()) return;

        Step1_BuildBackgroundCamera();
        Step2_ConfigureMainCamera();
        Step3_ConfigureUICanvas();
        Step4_ConfigureSpawnPoint();

        Debug.Log("[ScanSceneCtrl] ✅ AR display ready — " +
                  "Feed=background | 3D=foreground | UI=overlay");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 1 — Background Camera
    //
    // Renders the camera-feed RawImage as a full-screen background BEFORE
    // the main camera draws anything.  cullingMask=0 means it touches zero
    // 3D objects — it only draws the ScreenSpaceCamera canvas attached to it.
    // ─────────────────────────────────────────────────────────────────────────
  // ─────────────────────────────────────────────────────────────────────────
    // STEP 1 — Background Canvas (Single Camera Fix for URP)
    //
    // Instead of creating a second camera (which breaks in URP), we attach 
    // the canvas directly to the Main Camera and push it 100 meters away.
    // ─────────────────────────────────────────────────────────────────────────
    void Step1_BuildBackgroundCamera()
    {
        var canvasGO            = new GameObject("ScanScene_BG_Canvas");
        canvasGO.layer          = LayerMask.NameToLayer("UI");
        
        var canvas              = canvasGO.AddComponent<Canvas>();
        canvas.renderMode       = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera      = Camera.main; // USE MAIN CAMERA
        canvas.planeDistance    = 100f;        // Push far behind the 3D models
        canvas.sortingOrder     = -10;

        var scaler                      = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode              = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution      = new Vector2(1080f, 1920f);
        scaler.screenMatchMode          = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight       = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Move camera feed RawImage into this canvas ────────────────────────
        cameraFeedRawImage.transform.SetParent(canvasGO.transform, false);

        var rt          = cameraFeedRawImage.rectTransform;
        rt.anchorMin    = Vector2.zero;
        rt.anchorMax    = Vector2.one;
        rt.offsetMin    = Vector2.zero;
        rt.offsetMax    = Vector2.zero;

        Debug.Log("[ScanSceneCtrl] Step 1 ✅ Background canvas built at 100m depth.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 2 — Main Camera Configuration
    // ─────────────────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────
    // STEP 2 — Main Camera Configuration
    // ─────────────────────────────────────────────────────────────────────────
    void Step2_ConfigureMainCamera()
    {
        var cam = Camera.main;
        
        // FIX 1: Force the camera to look at the UI layer again. 
        // (The old AR script likely unchecked this in your scene settings).
        cam.cullingMask |= (1 << LayerMask.NameToLayer("UI"));

        // FIX 2: Ensure the camera can see far enough to view the background 
        // canvas we placed at 100m depth.
        if (cam.farClipPlane < 150f)
        {
            cam.farClipPlane = 500f;
        }

        Debug.Log("[ScanSceneCtrl] Step 2 ✅ Single Camera URP fix applied: UI layer visible, far clip extended.");
    }
    // ─────────────────────────────────────────────────────────────────────────
    // STEP 3 — UI Canvas
    //
    // ScreenSpaceOverlay renders AFTER all cameras, always on top.
    // Buttons, result panel, and scan guide text are always visible.
    // ─────────────────────────────────────────────────────────────────────────
    void Step3_ConfigureUICanvas()
    {
        // Auto-find if not assigned in Inspector
        if (mainUICanvas == null)
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c.gameObject.name != "ScanScene_BG_Canvas")
                {
                    mainUICanvas = c;
                    break;
                }
            }
        }

        if (mainUICanvas != null)
        {
            mainUICanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            mainUICanvas.sortingOrder = 100;
            Debug.Log($"[ScanSceneCtrl] Step 3 ✅  '{mainUICanvas.name}' → ScreenSpaceOverlay.");
        }
        else
        {
            Debug.LogWarning("[ScanSceneCtrl] Step 3 ⚠️  UI Canvas not found — " +
                             "drag it into the Inspector slot.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 4 — Spawn Point at Screen Centre
    //
    // ScreenToWorldPoint projects the screen centre to a world-space position
    // at modelDistance in front of the camera.  This means the model always
    // appears centred over the scanner guide box, regardless of which direction
    // the phone is pointed.
    //
    // WHY NOT use camera.forward * distance?
    //   Because that gives a point in front of the camera in world space, but
    //   the screen-space position of that point drifts with camera tilt.
    //   ScreenToWorldPoint always maps to the EXACT screen centre.
    // ─────────────────────────────────────────────────────────────────────────
    void Step4_ConfigureSpawnPoint()
    {
        if (modelDisplayManager == null)
            modelDisplayManager = FindFirstObjectByType<ModelDisplayManager>();

        if (modelDisplayManager == null)
        {
            Debug.LogWarning("[ScanSceneCtrl] Step 4 ⚠️  ModelDisplayManager not found.");
            return;
        }

        modelDisplayManager.mainCamera = Camera.main;

        if (modelDisplayManager.spawnPoint == null)
        {
            var sp = new GameObject("ModelSpawnPoint");
            modelDisplayManager.spawnPoint = sp.transform;
        }

        ApplySpawnCentre();
        Debug.Log($"[ScanSceneCtrl] Step 4 ✅  Spawn point set to screen centre " +
                  $"at {modelDistance}m depth.");
    }

    // ─── Keep spawn point updated every frame ────────────────────────────────
    // This ensures the position stays correct if Screen dimensions change
    // (e.g. phone orientation change, safe area adjustments on notched phones).
    void LateUpdate() => ApplySpawnCentre();

    void ApplySpawnCentre()
    {
        if (modelDisplayManager?.spawnPoint == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        // Project screen centre → world space at modelDistance from camera
        var screenPos = new Vector3(
            Screen.width  * 0.5f,
            Screen.height * screenCentreY,
            modelDistance
        );

        modelDisplayManager.spawnPoint.position = cam.ScreenToWorldPoint(screenPos);
        modelDisplayManager.spawnPoint.rotation = Quaternion.LookRotation(cam.transform.forward);
    }

    // ─── Validation ───────────────────────────────────────────────────────────
    bool ValidateSetup()
    {
        bool ok = true;

        if (Camera.main == null)
        {
            Debug.LogError("[ScanSceneCtrl] ❌ No Main Camera! Tag your camera as 'MainCamera'.");
            ok = false;
        }

        if (cameraFeedRawImage == null)
        {
            Debug.LogError("[ScanSceneCtrl] ❌ cameraFeedRawImage is NULL! " +
                           "Drag the WebCam RawImage into the Inspector.");
            ok = false;
        }

        return ok;
    }

    // ─── Context menu test helpers ────────────────────────────────────────────
    [ContextMenu("Log Current Spawn Position")]
    void LogSpawnPos()
    {
        if (modelDisplayManager?.spawnPoint != null)
            Debug.Log($"[ScanSceneCtrl] Spawn at {modelDisplayManager.spawnPoint.position}");
    }
}