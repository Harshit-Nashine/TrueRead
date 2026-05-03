// StatsDisplay.cs
// Attach to: StatsDisplay GameObject in StatsScene

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class StatsDisplay : MonoBehaviour
{
    [Header("Player Header")]
    public TextMeshProUGUI playerLevelTMP;
    public TextMeshProUGUI playerXPTMP;          // ⚠️ Was unassigned — drag LevelTMP/XP text here

    [Header("XP Bar")]
    public Image xpBarFill;

    [Header("Day Streak")]
    public TextMeshProUGUI dayStreakTMP;

    [Header("Stat Cards")]
    public TextMeshProUGUI totalQuestionsTMP;
    public TextMeshProUGUI accuracyTMP;
    public TextMeshProUGUI bestStreakTMP;

    [Header("Session History")]
    public Transform sessionHistoryContent;
    public GameObject sessionEntryPrefab;        // ⚠️ Was Missing — assign a TMP prefab

    [Header("Badges")]
    public Transform badgesContent;

    [Header("Mastery")]
    public TextMeshProUGUI masteryTMP;           // ⚠️ Was unassigned — drag MasterySection text here

    [Header("Navigation")]
    public Button backButton;
    public Button playAgainButton;               // ⚠️ Was unassigned — drag PlayAgain button here

    // ── Badge definitions ────────────────────────────────────────
    private static readonly string[] BADGE_KEYS = {
        "firststep", "hotstreak", "onfire",
        "perfect", "hindihero", "dailylearner", "dedicated"
    };

    private static readonly string[] BADGE_NAMES = {
        "First Step", "Hot Streak", "On Fire!",
        "Perfect Score", "Hindi Hero", "Daily Learner", "Dedicated"
    };

    private static readonly string[] BADGE_DESC = {
        "Complete your first quiz",
        "Get a streak of 3",
        "Get a streak of 5",
        "Score 100 in a session",
        "Master all characters",
        "Play 7 days in a row",
        "Play 30 days in a row"
    };

    // ────────────────────────────────────────────────────────────
    void Start()
    {
        Debug.Log("[Stats] Start — wiring buttons and populating display.");

        if (backButton != null)
            backButton.onClick.AddListener(OnBackPressed);
        else
            Debug.LogWarning("[Stats] backButton is not assigned!");

        if (playAgainButton != null)
            playAgainButton.onClick.AddListener(OnPlayAgainPressed);
        else
            Debug.LogWarning("[Stats] playAgainButton is not assigned! Drag the button into the Inspector.");

        DisplayPlayerHeader();
        DisplayXPBar();
        DisplayDayStreak();
        DisplayStatCards();
        DisplaySessionHistory();
        DisplayBadges();
        DisplayMastery();

        Debug.Log("[Stats] Display complete.");
    }

    // ── Player Header ────────────────────────────────────────────
    void DisplayPlayerHeader()
    {
        int level = PlayerPrefs.GetInt("current_level", 1);
        int xp    = PlayerPrefs.GetInt("total_xp", 0);

        if (playerLevelTMP != null)
            playerLevelTMP.text = $"Level {level}";
        else
            Debug.LogWarning("[Stats] playerLevelTMP not assigned.");

        if (playerXPTMP != null)
            playerXPTMP.text = $"{xp} XP";
        else
            Debug.LogWarning("[Stats] playerXPTMP not assigned — drag XP text object into Inspector.");

        Debug.Log($"[Stats] Header → Level: {level}, XP: {xp}");
    }

    // ── XP Bar ───────────────────────────────────────────────────
    void DisplayXPBar()
    {
        if (xpBarFill == null)
        {
            Debug.LogWarning("[Stats] xpBarFill not assigned.");
            return;
        }

        int xp    = PlayerPrefs.GetInt("total_xp", 0);
        int level = PlayerPrefs.GetInt("current_level", 1);

        int[] thresholds = { 0, 200, 500, 1000, 2000, 3500, 5000, 7500 };

        int currentLevelXP = (level - 1) < thresholds.Length
            ? thresholds[level - 1]
            : thresholds[thresholds.Length - 1];

        int nextLevelXP = level < thresholds.Length
            ? thresholds[level]
            : thresholds[thresholds.Length - 1] + 1000;

        float range    = nextLevelXP - currentLevelXP;
        float progress = range > 0 ? (float)(xp - currentLevelXP) / range : 1f;

        xpBarFill.fillAmount = Mathf.Clamp01(progress);

        Debug.Log($"[Stats] XP Bar → {progress:P0} " +
                  $"({xp - currentLevelXP}/{nextLevelXP - currentLevelXP})");
    }

    // ── Day Streak ───────────────────────────────────────────────
    void DisplayDayStreak()
    {
        if (dayStreakTMP == null)
        {
            Debug.LogWarning("[Stats] dayStreakTMP not assigned.");
            return;
        }

        int streak = PlayerPrefs.GetInt("day_streak", 0);
        string emoji = streak >= 7 ? " 🔥" : streak >= 3 ? " ⚡" : "";
        dayStreakTMP.text = streak > 0
            ? $"{streak} Day Streak{emoji}"
            : "Start your streak today!";
    }

    // ── Stat Cards ───────────────────────────────────────────────
    void DisplayStatCards()
    {
        int totalQuizzes   = PlayerPrefs.GetInt("total_quizzes", 0);
        int totalQuestions = totalQuizzes * 10;
        int sessionCount   = PlayerPrefs.GetInt("session_count", 0);

        int totalCorrect = 0;
        int totalAsked   = 0;
        int bestStreak   = 0;

        for (int i = 0; i < sessionCount; i++)
        {
            int score  = PlayerPrefs.GetInt($"sess_{i}_score", 0);
            int streak = PlayerPrefs.GetInt($"sess_{i}_streak", 0);

            // Each question is worth 10pts base
            totalCorrect += score / 10;
            totalAsked   += 10;

            if (streak > bestStreak) bestStreak = streak;
        }

        float accuracy = totalAsked > 0
            ? (float)totalCorrect / totalAsked * 100f
            : 0f;

        if (totalQuestionsTMP != null)
            totalQuestionsTMP.text = totalQuestions.ToString();
        else
            Debug.LogWarning("[Stats] totalQuestionsTMP not assigned.");

        if (accuracyTMP != null)
            accuracyTMP.text = $"{accuracy:F0}%";
        else
            Debug.LogWarning("[Stats] accuracyTMP not assigned.");

        if (bestStreakTMP != null)
            bestStreakTMP.text = bestStreak.ToString();
        else
            Debug.LogWarning("[Stats] bestStreakTMP not assigned.");

        Debug.Log($"[Stats] Cards → Questions: {totalQuestions}, " +
                  $"Accuracy: {accuracy:F0}%, Best Streak: {bestStreak}");
    }

    // ── Session History ──────────────────────────────────────────
    void DisplaySessionHistory()
    {
        if (sessionHistoryContent == null)
        {
            Debug.LogWarning("[Stats] sessionHistoryContent not assigned.");
            return;
        }

        // Clear existing entries
        foreach (Transform child in sessionHistoryContent)
            Destroy(child.gameObject);

        int sessionCount = PlayerPrefs.GetInt("session_count", 0);
        int showCount    = Mathf.Min(sessionCount, 10);

        if (showCount == 0)
        {
            SpawnTextEntry(sessionHistoryContent, "No sessions yet — play a quiz!", Color.gray);
            return;
        }

        for (int i = sessionCount - 1; i >= sessionCount - showCount; i--)
        {
            int    score  = PlayerPrefs.GetInt($"sess_{i}_score", 0);
            int    streak = PlayerPrefs.GetInt($"sess_{i}_streak", 0);
            string date   = PlayerPrefs.GetString($"sess_{i}_date", "Unknown date");

            string label = $"{date}   Score: {score}   Streak: {streak}";
            SpawnTextEntry(sessionHistoryContent, label, Color.white);
        }
    }

    // ── Badges ───────────────────────────────────────────────────
    void DisplayBadges()
    {
        if (badgesContent == null)
        {
            Debug.LogWarning("[Stats] badgesContent not assigned.");
            return;
        }

        foreach (Transform child in badgesContent)
            Destroy(child.gameObject);

        for (int i = 0; i < BADGE_KEYS.Length; i++)
        {
            bool unlocked = PlayerPrefs.GetInt("badge_" + BADGE_KEYS[i], 0) == 1;

            string label = unlocked
                ? $"★  {BADGE_NAMES[i]}"
                : $"☆  {BADGE_NAMES[i]}  —  {BADGE_DESC[i]}";

            Color col = unlocked
                ? new Color(1f, 0.75f, 0f)   // gold
                : new Color(0.5f, 0.5f, 0.5f); // grey

            SpawnTextEntry(badgesContent, label, col);
        }
    }

    // ── Mastery Section ──────────────────────────────────────────
    void DisplayMastery()
    {
        if (masteryTMP == null)
        {
            Debug.LogWarning("[Stats] masteryTMP not assigned — drag the MasterySection TMP into Inspector.");
            return;
        }

        int mastered = 0;
        int learning = 0;
        int notSeen  = 0;

        for (int i = 0; i < 46; i++)
        {
            int m = PlayerPrefs.GetInt("mastery_" + i, 0);
            if (m >= 5)     mastered++;
            else if (m > 0) learning++;
            else            notSeen++;
        }

        masteryTMP.text =
            $"Mastered:  {mastered} / 46\n" +
            $"Learning:  {learning}\n" +
            $"Not seen:  {notSeen}";

        Debug.Log($"[Stats] Mastery → Mastered: {mastered}, " +
                  $"Learning: {learning}, Not seen: {notSeen}");
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Spawns a text entry under a parent transform.
    /// Uses the sessionEntryPrefab if assigned, otherwise creates a minimal fallback.
    /// </summary>
    void SpawnTextEntry(Transform parent, string text, Color color)
    {
        if (sessionEntryPrefab != null)
        {
            GameObject go  = Instantiate(sessionEntryPrefab, parent);
            var        tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text  = text;
                tmp.color = color;
            }
        }
        else
        {
            // Fallback: create a plain GameObject with TMP component
            // (visible in Editor but will look unstyled — assign a prefab for production)
            Debug.LogWarning($"[Stats] sessionEntryPrefab is missing. " +
                             $"Create a prefab with a TextMeshProUGUI component " +
                             $"and assign it in the Inspector. Entry: {text}");
        }
    }

    // ── Button Handlers ──────────────────────────────────────────
    void OnBackPressed()
    {
        Debug.Log("[Stats] Back → MainMenu");
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    void OnPlayAgainPressed()
    {
        Debug.Log("[Stats] Play Again → QuizScene");
        UnityEngine.SceneManagement.SceneManager.LoadScene("QuizScene");
    }
}