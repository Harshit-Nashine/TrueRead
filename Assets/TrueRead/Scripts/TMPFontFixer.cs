// TMPFontFixer.cs
// Menu: TrueRead → Fix All TMP Fonts
// Removes underline from all TMPs and fixes English TMPs using Hindi font

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public class TMPFontFixer : EditorWindow
{
    [MenuItem("TrueRead/Fix All TMP Fonts In Open Scenes")]
    static void FixAllTMPFonts()
    {
        int fixedCount = 0;

        // Get all TMP objects in all open scenes
        TextMeshProUGUI[] allTMPs =
            GameObject.FindObjectsByType<TextMeshProUGUI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        Debug.Log($"[TMPFixer] Found {allTMPs.Length} TMP objects.");

        foreach (TextMeshProUGUI tmp in allTMPs)
        {
            bool changed = false;

            // Remove underline style
            if ((tmp.fontStyle & FontStyles.Underline) != 0)
            {
                tmp.fontStyle &= ~FontStyles.Underline;
                changed = true;
                Debug.Log($"[TMPFixer] Removed underline: {tmp.gameObject.name}");
            }

            // Fix English TMPs using Hindi font
            // Check if text contains only ASCII characters
            if (tmp.font != null &&
                tmp.font.name.Contains("Hindi") &&
                IsEnglishText(tmp.text))
            {
                // Find LiberationSans SDF
                TMP_FontAsset liberationFont =
                    AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/TextMesh Pro/Resources/Fonts & Materials/" +
                    "LiberationSans SDF.asset");

                if (liberationFont == null)
                {
                    // Try alternate path
                    string[] guids = AssetDatabase.FindAssets(
                        "LiberationSans SDF t:TMP_FontAsset");
                    if (guids.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        liberationFont =
                            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    }
                }

                if (liberationFont != null)
                {
                    tmp.font = liberationFont;
                    changed = true;
                    Debug.Log($"[TMPFixer] Fixed font on: " +
                              $"{tmp.gameObject.name} text='{tmp.text}'");
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(tmp);
                fixedCount++;
            }
        }

        // Save all open scenes
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty)
                EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[TMPFixer] Done. Fixed {fixedCount} TMP objects.");
    }

    static bool IsEnglishText(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        foreach (char c in text)
            if (c > 127) return false; // non-ASCII = likely Hindi
        return true;
    }
}
#endif