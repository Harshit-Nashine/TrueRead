// QuizManager.cs
// Attach to: QuizManager GameObject in QuizScene

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
public class QuizManager : MonoBehaviour
{
    [Header("Data — assign all 46 CharacterData assets")]
    public CharacterData[] allCharacters;

    [Header("UI — Character Display")]
    public TextMeshProUGUI questionCharTMP;  // big Hindi character
    public TextMeshProUGUI promptTMP;        // "What is this character?"

    [Header("UI — Option Buttons (4)")]
    public TextMeshProUGUI[] optionTMPs;     // 4 button labels
    public Button[] optionButtons;           // 4 buttons

    [Header("UI — Feedback")]
    public GameObject resultFeedback;        // ResultFeedback panel
    public TextMeshProUGUI feedbackTMP;      // "Correct!" or "Wrong!"
    public TextMeshProUGUI answerTMP;        // shows correct answer
    public Button nextQButton;              // Next Question button

    [Header("UI — Stats")]
    public TextMeshProUGUI streakTMP;
    public TextMeshProUGUI scoreTMP;

    [Header("UI — Back Button")]
    public Button backButton;

    [Header("Settings")]
    public int sessionLength = 10;

    // ── Mastery & Weighting ──────────────────────────────────────
    private int[] _mastery;
    private float[] _weights;

    // ── Question State ───────────────────────────────────────────
    private int _currentIndex;
    private int[] _optionIndices = new int[4];

    // ── Session State ────────────────────────────────────────────
    private int _streak;
    private int _sessionScore;
    private int _questionCount;
    private bool _answered; // prevents double-tap

    // ── Colours ──────────────────────────────────────────────────
    private static readonly Color COL_CORRECT  = new Color(0.18f, 0.8f,  0.44f); // green
    private static readonly Color COL_WRONG    = new Color(0.91f, 0.3f,  0.24f); // red
    private static readonly Color COL_NORMAL   = new Color(0.18f, 0.42f, 0.87f); // blue
    private static readonly Color COL_DISABLED = new Color(0.4f,  0.4f,  0.4f);  // grey

    // ────────────────────────────────────────────────────────────
    void Start()
    {
        Debug.Log("[Quiz] Start");

        if (allCharacters == null || allCharacters.Length == 0)
        {
            Debug.LogError("[Quiz] allCharacters is empty! " +
                           "Assign all 46 CharacterData in Inspector.");
            return;
        }

        // Hide feedback panel at start
        if (resultFeedback != null)
            resultFeedback.SetActive(false);

        // Wire option buttons
        for (int i = 0; i < optionButtons.Length; i++)
        {
            int index = i; // capture for lambda
            optionButtons[i].onClick.AddListener(
                () => SubmitAnswer(index));
        }

        // Wire next button
        if (nextQButton != null)
            nextQButton.onClick.AddListener(OnNextPressed);

        // Wire back button
        if (backButton != null)
            backButton.onClick.AddListener(OnBackPressed);

        // Load saved mastery
        _mastery = new int[allCharacters.Length];
        _weights = new float[allCharacters.Length];
        LoadMastery();
        RebuildWeights();

        // Set prompt text
        if (promptTMP != null)
            promptTMP.text = "What is this character?";

        // Start first question
        NextQuestion();

        Debug.Log("[Quiz] Ready.");
    }

    // ── Answer Submission ────────────────────────────────────────
    public void SubmitAnswer(int buttonIndex)
    {
        if (_answered) return; // prevent double tap
        _answered = true;

        bool correct = _optionIndices[buttonIndex] == _currentIndex;

        // Update mastery
        if (correct)
        {
            _mastery[_currentIndex] =
                Mathf.Min(5, _mastery[_currentIndex] + 1);
            _streak++;
            _sessionScore += 10 + (_streak > 2 ? 5 : 0);
        }
        else
        {
            _mastery[_currentIndex] =
                Mathf.Max(0, _mastery[_currentIndex] - 2);
            _streak = 0;
        }

        // Visual feedback on buttons
        ShowButtonFeedback(buttonIndex, correct);

        // Show result feedback panel
        ShowFeedback(correct, buttonIndex);

        // Update streak UI
        UpdateStatsUI();

        // Save mastery
        SaveMastery();
        RebuildWeights();

        _questionCount++;

        Debug.Log($"[Quiz] Answer: {(correct ? "CORRECT" : "WRONG")} " +
                  $"Streak: {_streak} Score: {_sessionScore}");
    }

    void ShowButtonFeedback(int tappedIndex, bool correct)
    {
        for (int i = 0; i < optionButtons.Length; i++)
        {
            ColorBlock cb = optionButtons[i].colors;

            if (_optionIndices[i] == _currentIndex)
            {
                // Always highlight correct answer green
                cb.normalColor   = COL_CORRECT;
                cb.selectedColor = COL_CORRECT;
            }
            else if (i == tappedIndex && !correct)
            {
                // Highlight wrong tap red
                cb.normalColor   = COL_WRONG;
                cb.selectedColor = COL_WRONG;
            }
            else
            {
                // Dim other buttons
                cb.normalColor   = COL_DISABLED;
                cb.selectedColor = COL_DISABLED;
            }

            optionButtons[i].colors = cb;
            optionButtons[i].interactable = false; // disable after answer
        }
    }

    void ShowFeedback(bool correct, int tappedIndex)
    {        resultFeedback.transform.localScale = new Vector3(1f, 0f, 1f);
                resultFeedback.transform.DOScaleY(1f, 0.2f).SetEase(Ease.OutBack);
            
        if (resultFeedback == null) return;

        resultFeedback.SetActive(true);

        if (feedbackTMP != null)
        {
            feedbackTMP.text  = correct ? "Correct!" : "Wrong!";
            feedbackTMP.color = correct ? COL_CORRECT : COL_WRONG;
        }

        if (answerTMP != null)
        {
            string correctChar =
                allCharacters[_currentIndex].devanagariChar;
            string correctPhonetic =
                allCharacters[_currentIndex].phoneticName;

            if (correct)
                answerTMP.text = $"{correctChar} = {correctPhonetic}";
            else
                answerTMP.text = $"Answer: {correctChar} ({correctPhonetic})";
        }
    }

    // ── Next Question ────────────────────────────────────────────
    void OnNextPressed()
    {
        Debug.Log("[Quiz] Next pressed.");

        if (_questionCount >= sessionLength)
        {
            EndSession();
            return;
        }

        NextQuestion();
    }

    void NextQuestion()
    {
        _answered = false;

        // Hide feedback
        if (resultFeedback != null)
            resultFeedback.SetActive(false);

        // Reset button colours
        ResetButtonColors();

        // Pick weighted random character
        _currentIndex = WeightedRandom();

        // Show character
        if (questionCharTMP != null)
            questionCharTMP.text =
                allCharacters[_currentIndex].devanagariChar;

        // Build 4 options
        BuildOptions();

        Debug.Log($"[Quiz] Question: " +
                  $"{allCharacters[_currentIndex].devanagariChar} " +
                  $"({allCharacters[_currentIndex].phoneticName}) " +
                  $"Q#{_questionCount + 1}/{sessionLength}");
    }

    void BuildOptions()
    {
        // Pick 3 wrong answers
        var pool = new List<int>();
        int attempts = 0;

        while (pool.Count < 3 && attempts < 200)
        {
            int r = Random.Range(0, allCharacters.Length);
            if (r != _currentIndex && !pool.Contains(r))
                pool.Add(r);
            attempts++;
        }

        // Fill option indices
        _optionIndices[0] = _currentIndex;
        _optionIndices[1] = pool[0];
        _optionIndices[2] = pool[1];
        _optionIndices[3] = pool[2];

        // Fisher-Yates shuffle
        for (int i = 3; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = _optionIndices[i];
            _optionIndices[i] = _optionIndices[j];
            _optionIndices[j] = tmp;
        }

        // Set button labels to phonetic names
        for (int i = 0; i < 4; i++)
        {
            if (optionTMPs[i] != null)
                optionTMPs[i].text =
                    allCharacters[_optionIndices[i]].phoneticName;

            optionButtons[i].interactable = true;
        }
    }

    void ResetButtonColors()
    {
        foreach (Button btn in optionButtons)
        {
            ColorBlock cb = btn.colors;
            cb.normalColor   = COL_NORMAL;
            cb.selectedColor = COL_NORMAL;
            btn.colors = cb;
            btn.interactable = true;
        }
    }

    // ── Session End ──────────────────────────────────────────────
    void EndSession()
    {
        Debug.Log($"[Quiz] Session ended. " +
                  $"Score: {_sessionScore} " +
                  $"Questions: {_questionCount} " +
                  $"Best streak: {_streak}");

        // Save to StatsManager
        if (StatsManager.Instance != null)
            StatsManager.Instance.SaveSession(
                _sessionScore, _questionCount, _streak);
        else
            Debug.LogWarning("[Quiz] StatsManager.Instance is NULL. " +
                             "Stats not saved.");

        // Go to StatsScene
        UnityEngine.SceneManagement.SceneManager
            .LoadScene("StatsScene");
    }

    // ── Stats UI ─────────────────────────────────────────────────
    void UpdateStatsUI()
    {
        if (streakTMP != null)
            streakTMP.text = _streak > 1 ? $"Streak: {_streak}" : "";

        if (scoreTMP != null)
            scoreTMP.text = $"{_sessionScore} pts";
    }

    // ── Back Button ──────────────────────────────────────────────
    void OnBackPressed()
    {
        Debug.Log("[Quiz] Back pressed.");
        UnityEngine.SceneManagement.SceneManager
            .LoadScene("MainMenu");
    }

    // ── Weighted Random ──────────────────────────────────────────
    void RebuildWeights()
    {
        float total = 0;

        for (int i = 0; i < _mastery.Length; i++)
        {
            // Higher weight for less mastered characters
            _weights[i] = Mathf.Pow(6f - _mastery[i], 2f);
            total += _weights[i];
        }

        // Normalise
        for (int i = 0; i < _weights.Length; i++)
            _weights[i] /= total;
    }

    int WeightedRandom()
    {
        float r = Random.value;
        float cum = 0;

        for (int i = 0; i < _weights.Length; i++)
        {
            cum += _weights[i];
            if (r <= cum) return i;
        }

        return _weights.Length - 1;
    }

    // ── PlayerPrefs Save/Load ────────────────────────────────────
    void SaveMastery()
    {
        for (int i = 0; i < _mastery.Length; i++)
            PlayerPrefs.SetInt("mastery_" + i, _mastery[i]);
        PlayerPrefs.Save();
        Debug.Log("[Quiz] Mastery saved.");
    }

    void LoadMastery()
    {
        for (int i = 0; i < _mastery.Length; i++)
            _mastery[i] = PlayerPrefs.GetInt("mastery_" + i, 0);
        Debug.Log("[Quiz] Mastery loaded.");
    }
}