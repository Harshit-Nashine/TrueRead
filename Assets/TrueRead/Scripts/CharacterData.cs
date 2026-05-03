using UnityEngine;

[CreateAssetMenu(menuName = "TrueRead/Character Data", fileName = "CharData_")]
public class CharacterData : ScriptableObject
{
    [Header("Identity")]
    public int    modelIndex;        // 0–45 — must match AI output exactly
    public string devanagariChar;    // e.g.  क
    public string phoneticName;      // e.g.  Ka
    public string characterType;     // Consonant | Digit | Conjunct

    [Header("Words (blank for character-only types)")]
    public string word1Hindi;        // e.g.  कलम
    public string word1Meaning;      // e.g.  Pen
    public string word2Hindi;        // e.g.  किताब
    public string word2Meaning;      // e.g.  Book

    [Header("Audio Clips")]
    public AudioClip charAudio;      // pronunciation of the character
    public AudioClip word1Audio;     // pronunciation of word 1 (null if no words)
    public AudioClip word2Audio;     // pronunciation of word 2 (null if 1 word only)

    [Header("3D Prefab")]
    public GameObject packagePrefab; // the CharPackage prefab for this character
}