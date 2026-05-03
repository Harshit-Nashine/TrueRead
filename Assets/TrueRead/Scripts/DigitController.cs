using System.Collections;
using UnityEngine;

public class DigitController : MonoBehaviour
{
    public AudioClip digitAudio;       // e.g. 'Shunya' for ०
    public float     entryDuration = 0.45f;

    private AudioSource _audio;
    private float       _entryTimer = 0f;
    private bool        _entryDone  = false;

    void Awake()
    {
        _audio = GetComponent<AudioSource>();
        if (_audio == null)
            _audio = gameObject.AddComponent<AudioSource>();
    }

    void OnEnable()
    {
        _entryTimer = 0f;
        _entryDone  = false;
        transform.localScale = Vector3.zero;
        StartCoroutine(PlayAfterDelay());
    }

    void Update()
    {
        if (_entryDone) return;
        _entryTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_entryTimer / entryDuration);
        float bounce = 1f + 0.12f * Mathf.Sin(t * Mathf.PI);
        transform.localScale = Vector3.one * t * bounce;
        if (_entryTimer >= entryDuration)
        {
            transform.localScale = Vector3.one;
            _entryDone = true;
        }
    }

    IEnumerator PlayAfterDelay()
    {
        yield return new WaitForSeconds(0.3f);
        if (digitAudio != null && _audio != null)
        {
            _audio.clip = digitAudio;
            _audio.Play();
        }
    }
}