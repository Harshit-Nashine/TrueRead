using System.Collections;
using UnityEngine;
using TMPro;

public class PackageController : MonoBehaviour
{
    // ── Inspector references ─────────────────────────────
    [Header("Slot References — drag from Hierarchy")]
    public GameObject objectSlot1;       // ObjectSlot_1 GameObject
    public GameObject objectSlot2;       // ObjectSlot_2 GameObject

    [Header("Audio")]
    public AudioClip  charAudio;         // pronunciation of the character
    public AudioClip  word1Audio;        // pronunciation of word 1
    public AudioClip  word2Audio;        // pronunciation of word 2

    [Header("Animation")]
    public float entryDuration  = 0.45f; // seconds for scale-in
    public float rotateSpeed    = 22f;   // degrees/sec for model spin

    // ── Private state ────────────────────────────────────
    private AudioSource _audio;
    private int         _currentSlot = 1; // 1 or 2
    private bool        _isAnimating  = false;
    private float       _entryTimer   = 0f;
    private bool        _entryDone    = false;

    // Swipe detection
    private Vector2     _touchStart;
    private const float SWIPE_THRESHOLD = 60f; // pixels

    // ── Unity Lifecycle ──────────────────────────────────
    void Awake()
    {
        _audio = GetComponent<AudioSource>();
        if (_audio == null)
            _audio = gameObject.AddComponent<AudioSource>();
    }

    void OnEnable()
    {
        // Reset to slot 1 every time this package is shown
        _currentSlot  = 1;
        _entryTimer   = 0f;
        _entryDone    = false;
        _isAnimating  = false;
        transform.localScale = Vector3.zero;

        // Ensure correct slot visibility
        if (objectSlot1 != null) objectSlot1.SetActive(true);
        if (objectSlot2 != null) objectSlot2.SetActive(false);

        // Start audio chain: char audio → word1 audio
        StartCoroutine(PlayAudioChain(charAudio, word1Audio));
    }

    void Update()
    {
        HandleEntryAnimation();
        if (_entryDone) HandleSwipeInput();
        SpinCurrentModel();
    }

    // ── Entry animation ──────────────────────────────────
    void HandleEntryAnimation()
    {
        if (_entryDone) return;
        _entryTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_entryTimer / entryDuration);
        // Bouncy overshoot easing
        float bounce = 1f + 0.12f * Mathf.Sin(t * Mathf.PI);
        transform.localScale = Vector3.one * t * bounce;
        if (_entryTimer >= entryDuration)
        {
            transform.localScale = Vector3.one;
            _entryDone = true;
        }
    }

    // ── Swipe and tap input ──────────────────────────────
    void HandleSwipeInput()
    {
        // ── Touch (phone) ──
        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
                _touchStart = t.position;
            if (t.phase == TouchPhase.Ended)
            {
                float deltaX = t.position.x - _touchStart.x;
                if (Mathf.Abs(deltaX) > SWIPE_THRESHOLD)
                    SwitchSlot();  // any horizontal swipe switches
            }
        }
        // ── Mouse click (editor testing) ──
        if (Input.GetMouseButtonDown(0))
            SwitchSlot();
    }

    // ── Switch between Object 1 and Object 2 ────────────
    public void SwitchSlot()
    {
        if (_isAnimating) return;
        _currentSlot = (_currentSlot == 1) ? 2 : 1;
        StartCoroutine(AnimateSlotSwitch());
    }

    IEnumerator AnimateSlotSwitch()
    {
        _isAnimating = true;

        // Determine which slots to hide and show
        GameObject hiding  = _currentSlot == 2 ? objectSlot1 : objectSlot2;
        GameObject showing = _currentSlot == 2 ? objectSlot2 : objectSlot1;
        AudioClip  newAudio= _currentSlot == 2 ? word2Audio  : word1Audio;

        // Scale OUT the current slot
        float t = 0f;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            if (hiding != null)
                hiding.transform.localScale = Vector3.one * (1f - t / 0.2f);
            yield return null;
        }
        if (hiding  != null) { hiding.SetActive(false);  hiding.transform.localScale  = Vector3.one; }
        if (showing != null) { showing.SetActive(true);  showing.transform.localScale = Vector3.zero; }

        // Scale IN the new slot
        t = 0f;
        while (t < 0.25f)
        {
            t += Time.deltaTime;
            if (showing != null)
            {
                float s = t / 0.25f;
                float bounce = 1f + 0.1f * Mathf.Sin(s * Mathf.PI);
                showing.transform.localScale = Vector3.one * s * bounce;
            }
            yield return null;
        }
        if (showing != null) showing.transform.localScale = Vector3.one;

        // Play the new word audio
        PlayOneClip(newAudio);

        _isAnimating = false;
    }

    // ── Spin the active model ────────────────────────────
    void SpinCurrentModel()
    {
        GameObject activeSlot = _currentSlot == 1 ? objectSlot1 : objectSlot2;
        if (activeSlot == null || !activeSlot.activeSelf) return;
        // Spin Model_ child (index 1 inside the slot — index 0 is WordDisplay)
        if (activeSlot.transform.childCount > 1)
            activeSlot.transform.GetChild(1).Rotate(0, rotateSpeed * Time.deltaTime, 0);
    }

    // ── Audio helpers ────────────────────────────────────
    IEnumerator PlayAudioChain(AudioClip first, AudioClip second)
    {
        yield return new WaitForSeconds(0.3f); // brief pause before first plays
        PlayOneClip(first);
        if (first != null)
            yield return new WaitForSeconds(first.length + 0.4f);
        PlayOneClip(second);
    }

    void PlayOneClip(AudioClip clip)
    {
        if (clip == null || _audio == null) return;
        _audio.Stop();
        _audio.clip = clip;
        _audio.Play();
    }
}