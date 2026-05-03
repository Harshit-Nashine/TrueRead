// BootstrapLoader.cs
using UnityEngine;
using UnityEngine.SceneManagement;

public class BootstrapLoader : MonoBehaviour
{
    void Awake()
    {

        // Disable Bootstrap's camera immediately
        // so it doesn't conflict with MainMenu camera
        Camera bootstrapCam = GetComponentInChildren<Camera>();
        if (bootstrapCam != null)
            bootstrapCam.gameObject.SetActive(false);

        // Disable Bootstrap's audio listener too
        AudioListener bootstrapAudio = GetComponentInChildren<AudioListener>();
        if (bootstrapAudio != null)
            bootstrapAudio.enabled = false;
        
    }

    void Start()
    {
        // Load MainMenu, replacing Bootstrap entirely
        SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
    }
    
}
