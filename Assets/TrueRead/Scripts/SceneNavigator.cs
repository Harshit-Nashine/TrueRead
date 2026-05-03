// SceneNavigator.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class SceneNavigator : MonoBehaviour
{
    void Awake()
    {
        EnsureEventSystem();
    }

    void EnsureEventSystem()
{
    // Count all EventSystems including inactive
    EventSystem[] allSystems = FindObjectsByType<EventSystem>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None);

    if (allSystems.Length == 0)
    {
        Debug.LogWarning("[SceneNavigator] No EventSystem found! Creating one.");
        GameObject esObj = new GameObject("EventSystem");
        esObj.AddComponent<EventSystem>();
        esObj.AddComponent<StandaloneInputModule>();
        Debug.Log("[SceneNavigator] EventSystem created successfully.");
    }
    else if (allSystems.Length > 1)
    {
        // Destroy extras, keep first one
        for (int i = 1; i < allSystems.Length; i++)
            Destroy(allSystems[i].gameObject);
        Debug.Log("[SceneNavigator] Removed duplicate EventSystems.");
    }
    else
    {
        Debug.Log("[SceneNavigator] EventSystem OK: " +
                  allSystems[0].gameObject.name);
    }
}

    public void GoToScan()
    {
        Debug.Log("[Nav] Loading ScanScene");
        SceneManager.LoadScene("ScanScene");
    }

    public void GoToQuiz()
    {
        Debug.Log("[Nav] Loading QuizScene");
        SceneManager.LoadScene("QuizScene");
    }

    public void GoToStats()
    {
        Debug.Log("[Nav] Loading StatsScene");
        SceneManager.LoadScene("StatsScene");
    }

    public void GoToMainMenu()
    {
        Debug.Log("[Nav] Loading MainMenu");
        SceneManager.LoadScene("MainMenu");
    }
}
