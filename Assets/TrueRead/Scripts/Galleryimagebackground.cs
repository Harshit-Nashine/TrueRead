// GalleryImageBackground.cs
// Attach to: A new empty GameObject named "GalleryBackground" in ScanScene
//
// PURPOSE:
//   When the user picks an image from their gallery, this script replaces
//   the frozen camera feed with the actual selected photo as background.
//   The 3D model then renders on top via the main camera (same AR layering
//   already configured by ScanSceneController).
//
// SETUP:
//   1. In ScanScene Hierarchy → right-click → Create Empty → name "GalleryBackground"
//   2. Add this component to it
//   3. Leave all Inspector fields empty (canvas + image created automatically)
//   4. Drag "GalleryBackground" into ScanManager's "Gallery Background" slot

using UnityEngine;
using UnityEngine.UI;

public class GalleryImageBackground : MonoBehaviour
{
    // Optional — auto-created if left empty
    [Header("Optional — created automatically at runtime if left empty")]
    public RawImage displayImage;

    // ─── Private ─────────────────────────────────────────────────────────────
    private Canvas _canvas;
    private bool   _visible = false;

    // ─── Awake ────────────────────────────────────────────────────────────────
    void Awake()
    {
        BuildCanvas();
        gameObject.SetActive(false); // hidden at start
    }

    void BuildCanvas()
    {
        // sortingOrder = 5: above the background camera feed canvas (sortingOrder -10)
        //                   but below the UI overlay (sortingOrder 100)
        // This means: gallery image shows instead of camera feed,
        //             3D model renders on top (main camera, Depth Only),
        //             buttons/result panel stay above everything.
        _canvas = gameObject.GetComponent<Canvas>()
               ?? gameObject.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5;

        var scaler = gameObject.GetComponent<CanvasScaler>()
                  ?? gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight  = 0.5f;

        if (!gameObject.GetComponent<GraphicRaycaster>())
            gameObject.AddComponent<GraphicRaycaster>();

        // Black background so empty areas don't show garbage
        var bgGO    = new GameObject("Black_BG");
        bgGO.transform.SetParent(transform, false);
        var bgImg   = bgGO.AddComponent<Image>();
        bgImg.color = Color.black;
        StretchToFill(bgImg.rectTransform);

        // RawImage for the gallery photo
        if (displayImage == null)
        {
            var imgGO = new GameObject("Gallery_RawImage");
            imgGO.transform.SetParent(transform, false);
            displayImage = imgGO.AddComponent<RawImage>();
            StretchToFill(displayImage.rectTransform);

            // Keep photo proportions — fit inside screen without stretching
            var fitter          = imgGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode   = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio  = 1f; // updated when image loads
        }

        Debug.Log("[GalleryBG] Canvas built (sortingOrder=5).");
    }

    static void StretchToFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ScanManager after gallery image is loaded.
    /// Shows the image as full-screen background.
    /// NOTE: ScanManager still owns the texture lifetime — do NOT destroy it here.
    /// </summary>
    public void ShowImage(Texture2D texture)
    {
        if (texture == null)
        {
            Debug.LogWarning("[GalleryBG] ShowImage called with null texture.");
            return;
        }

        displayImage.texture = texture;

        // Match actual image proportions
        var fitter = displayImage.GetComponent<AspectRatioFitter>();
        if (fitter != null && texture.height > 0)
            fitter.aspectRatio = (float)texture.width / texture.height;

        gameObject.SetActive(true);
        _visible = true;

        Debug.Log($"[GalleryBG] Showing gallery image {texture.width}×{texture.height}px.");
    }

    /// <summary>
    /// Called by ScanManager when returning to live camera mode.
    /// Hides the gallery background so the camera feed shows again.
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
        _visible = false;
        if (displayImage) displayImage.texture = null;
        Debug.Log("[GalleryBG] Hidden — camera feed background restored.");
    }

    public bool IsVisible => _visible;
}