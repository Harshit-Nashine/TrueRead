// TutorialManager.cs
// Attach to: TutorialOverlay GameObject in MainMenu scene

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject tutorialOverlay;
    public TextMeshProUGUI cardTitleTMP;
    public TextMeshProUGUI cardDescTMP;
    public TextMeshProUGUI pageIndicatorTMP;
    public Button nextButton;
    public Button prevButton;
    public Button skipButton;
    public Image cardImage;

    [Header("Card Images (optional)")]
    public Sprite[] cardSprites; // 4 images, can be null

    // ── Tutorial Card Content ────────────────────────────────────
    private static readonly string[] TITLES = new string[]
    {
        "Namaste! Welcome",
        "Scan Characters",
        "Take Quizzes",
        "Track Progress"
    };

    private static readonly string[] DESCRIPTIONS = new string[]
    {
        "TrueRead helps you learn Hindi\nDevanagari characters in a fun,\ninteractive way!",
        "Point your camera at any\nHindi character and the app\nwill recognize it instantly!",
        "Test your knowledge with\nfun quizzes. Earn points,\nbuild streaks, unlock badges!",
        "Watch your mastery grow\nover time. Track your\nlearning journey here!"
    };

    private int _currentCard = 0;
    private const int TOTAL_CARDS = 4;

    // ────────────────────────────────────────────────────────────
    void Start()
    {
        // Check if tutorial has been shown before
        bool tutorialShown =
            PlayerPrefs.GetInt("tutorial_shown", 0) == 1;

        if (tutorialShown)
        {
            // Hide tutorial — already seen
            if (tutorialOverlay != null)
                tutorialOverlay.SetActive(false);
            Debug.Log("[Tutorial] Already shown. Skipping.");
            return;
        }

        // First launch — show tutorial
        Debug.Log("[Tutorial] First launch detected. Showing tutorial.");

        if (tutorialOverlay != null)
            tutorialOverlay.SetActive(true);

        // Wire buttons
        if (nextButton != null)
            nextButton.onClick.AddListener(OnNextPressed);

        if (prevButton != null)
            prevButton.onClick.AddListener(OnPrevPressed);

        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipPressed);

        // Show first card
        ShowCard(0);
    }

    // ── Card Display ─────────────────────────────────────────────
    void ShowCard(int index)
    {
        _currentCard = Mathf.Clamp(index, 0, TOTAL_CARDS - 1);

        // Update title
        if (cardTitleTMP != null)
            cardTitleTMP.text = TITLES[_currentCard];

        // Update description
        if (cardDescTMP != null)
            cardDescTMP.text = DESCRIPTIONS[_currentCard];

        // Update page indicator
        if (pageIndicatorTMP != null)
            pageIndicatorTMP.text =
                $"{_currentCard + 1} / {TOTAL_CARDS}";

        // Update card image if sprites provided
        if (cardImage != null && cardSprites != null
            && _currentCard < cardSprites.Length
            && cardSprites[_currentCard] != null)
        {
            cardImage.sprite = cardSprites[_currentCard];
            cardImage.gameObject.SetActive(true);
        }
        else if (cardImage != null)
        {
            cardImage.gameObject.SetActive(false);
        }

        // Update buttons
        UpdateButtons();

        Debug.Log($"[Tutorial] Showing card {_currentCard + 1}" +
                  $"/{TOTAL_CARDS}: {TITLES[_currentCard]}");
    }

    void UpdateButtons()
    {
        // Hide prev button on first card
        if (prevButton != null)
            prevButton.gameObject.SetActive(_currentCard > 0);

        // Change next button text on last card
        if (nextButton != null)
        {
            var tmp = nextButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = _currentCard == TOTAL_CARDS - 1 ?
                    "Let's Go!" : "Next →";
        }
    }

    // ── Button Handlers ──────────────────────────────────────────
    void OnNextPressed()
    {
        if (_currentCard < TOTAL_CARDS - 1)
        {
            ShowCard(_currentCard + 1);
        }
        else
        {
            // Last card — finish tutorial
            CompleteTutorial();
        }
    }

    void OnPrevPressed()
    {
        if (_currentCard > 0)
            ShowCard(_currentCard - 1);
    }

    void OnSkipPressed()
    {
        Debug.Log("[Tutorial] Skipped.");
        CompleteTutorial();
    }

    void CompleteTutorial()
    {
        Debug.Log("[Tutorial] Completed. Saving to PlayerPrefs.");

        // Save so it never shows again
        PlayerPrefs.SetInt("tutorial_shown", 1);
        PlayerPrefs.Save();

        // Hide overlay
        if (tutorialOverlay != null)
            tutorialOverlay.SetActive(false);

        // Unlock first badge
        if (StatsManager.Instance != null)
            StatsManager.Instance.UnlockBadge("firststep");
    }

    // ── Dev Helper — reset tutorial for testing ──────────────────
    // Call this from Console or a debug button
    public void ResetTutorial()
    {
        PlayerPrefs.DeleteKey("tutorial_shown");
        PlayerPrefs.Save();
        Debug.Log("[Tutorial] Reset. Will show on next launch.");
    }
}
