// ModelDisplayManager.cs — v4
//
// CHANGES FROM v3:
//   ✅ PositionSpawnPoint() now uses Camera.ScreenToWorldPoint() instead of
//      camera.forward * spawnDistance.
//
//      WHY: The old method placed the spawn point in world space along the
//      camera's forward axis. This worked correctly ONLY when the phone is
//      held perfectly flat. In practice, the model appeared at the top of
//      frame (in the ceiling area), because "forward * 3m" projected above
//      the scanner box when the phone tilted even slightly.
//
//      ScreenToWorldPoint(Screen.width/2, Screen.height * 0.45, spawnDistance)
//      gives the EXACT world position that corresponds to 45% up the screen
//      at spawnDistance metres — so the model is always centred over the
//      scanner guide box no matter how the phone is angled.
//
//   ✅ spawnDistance field kept public so ScanSceneController.modelDistance
//      and this value can be kept in sync via Inspector.
//   ✅ ValidateSetup() still includes all original checks — nothing removed.

using UnityEngine;

public class ModelDisplayManager : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("All 46 CharacterData assets — index 0 to 45")]
    [Tooltip("Slot 0=क(ka) ... 32=ह(ha) ... 35=ज्ञ(gya) ... 45=९\n" +
             "Order must match class_mapping.json exactly.\n" +
             "Run 'Validate All Slots' from gear menu to check for nulls.")]
    public CharacterData[] characters = new CharacterData[46];

    [Header("Where to spawn the 3D model")]
    [Tooltip("Assign an empty GameObject here, OR leave blank — one will be created.\n" +
             "ScanSceneController updates this every frame via ScreenToWorldPoint.\n" +
             "You do not need to position it manually.")]
    public Transform spawnPoint;

    [Header("Camera Reference")]
    [Tooltip("Drag your Main Camera here, or leave blank — Camera.main is used.")]
    public Camera mainCamera;

    [Header("Spawn Distance")]
    [Tooltip("How far in front of the camera (metres) the model appears.\n" +
             "MUST match modelDistance in ScanSceneController for consistency.\n" +
             "Default: 3. Adjust both fields together.")]
    public float spawnDistance = 3f;

    // ─── Private ──────────────────────────────────────────────────────────────

    private GameObject _current;
    private int        _lastIndex = -1;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (spawnPoint == null)
        {
            GameObject sp = new GameObject("AutoSpawnPoint");
            spawnPoint = sp.transform;
            Debug.LogWarning("[ModelDisplay] spawnPoint was NULL — created AutoSpawnPoint. " +
                             "ScanSceneController will position it correctly at runtime.");
        }

        PositionSpawnPoint();
        ValidateSetup();
    }

    // ─── ShowCharacter ────────────────────────────────────────────────────────

    public void ShowCharacter(int modelOutputIndex)
    {
        Debug.Log("[ModelDisplay] ShowCharacter called with index: " + modelOutputIndex);

        if (modelOutputIndex < 0 || modelOutputIndex >= 46)
        {
            Debug.LogError("[ModelDisplay] Index out of range: " + modelOutputIndex);
            return;
        }

        if (modelOutputIndex == _lastIndex)
        {
            Debug.Log("[ModelDisplay] Same character — keeping existing model.");
            return;
        }

        _lastIndex = modelOutputIndex;

        // Refresh spawn position before instantiation so the model appears
        // at the current screen-centre position (handles phone tilt changes).
        PositionSpawnPoint();

        if (spawnPoint == null)
        {
            Debug.LogError("[ModelDisplay] spawnPoint is NULL — cannot spawn model.");
            return;
        }

        CharacterData data = characters[modelOutputIndex];

        if (data == null)
        {
            Debug.LogError($"[ModelDisplay] CharacterData slot [{modelOutputIndex}] is NULL. " +
                           $"Assign a CharData asset to slot {modelOutputIndex} in the Inspector.");
            return;
        }

        if (data.packagePrefab == null)
        {
            Debug.LogError($"[ModelDisplay] packagePrefab is NULL in '{data.name}' " +
                           $"(slot {modelOutputIndex}, '{data.devanagariChar}'). " +
                           $"Open that CharData asset → assign the CharPackage prefab.");
            return;
        }

        // Destroy previous model
        if (_current != null)
        {
            Destroy(_current);
            _current = null;
        }

        // Spawn new model
        _current = Instantiate(data.packagePrefab,
                               spawnPoint.position,
                               spawnPoint.rotation);

        Debug.Log($"[ModelDisplay] ✅ Spawned '{data.devanagariChar}' ({data.phoneticName}) " +
                  $"at {spawnPoint.position}   prefab: {data.packagePrefab.name}");

        // Screen-position sanity log
        if (mainCamera != null)
        {
            Vector3 sp = mainCamera.WorldToScreenPoint(spawnPoint.position);
            bool inView = sp.z > 0
                       && sp.x > 0 && sp.x < Screen.width
                       && sp.y > 0 && sp.y < Screen.height;
            Debug.Log($"[ModelDisplay] Screen pos: {sp}   In view: {inView}");
            if (!inView)
                Debug.LogWarning("[ModelDisplay] ⚠️ Model spawned OUTSIDE camera view! " +
                                 "Check modelDistance in ScanSceneController.");
        }

        // Wire audio — PackageController
        PackageController pc = _current.GetComponent<PackageController>();
        if (pc != null)
        {
            pc.charAudio  = data.charAudio;
            pc.word1Audio = data.word1Audio;
            pc.word2Audio = data.word2Audio;
        }

        // Wire audio — DigitController
        DigitController dc = _current.GetComponent<DigitController>();
        if (dc != null)
            dc.digitAudio = data.charAudio;
    }

    // ─── DismissCurrentPackage ────────────────────────────────────────────────

    public void DismissCurrentPackage()
    {
        if (_current != null)
        {
            Destroy(_current);
            _current = null;
        }
        _lastIndex = -1;
        Debug.Log("[ModelDisplay] Package dismissed.");
    }

    // ─── Spawn Point Positioning ──────────────────────────────────────────────
    //
    // ScreenToWorldPoint approach:
    //   Screen.width/2, Screen.height * 0.45f  →  screen centre (slightly above mid)
    //   spawnDistance                           →  depth from camera
    //
    // This guarantees the model always appears at the screen-centre position
    // at the correct depth, regardless of camera tilt or phone angle.
    //
    [ContextMenu("Set Spawn In Front Of Camera")]
    public void PositionSpawnPoint()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null || spawnPoint == null) return;

        // Project screen centre to world space at spawnDistance
        Vector3 screenCentre = new Vector3(
            Screen.width  * 0.5f,
            Screen.height * 0.45f,  // 45% up = slightly above screen centre
            spawnDistance
        );

        spawnPoint.position = mainCamera.ScreenToWorldPoint(screenCentre);
        spawnPoint.rotation = Quaternion.LookRotation(mainCamera.transform.forward);

        Debug.Log($"[ModelDisplay] SpawnPoint → screen centre at {spawnPoint.position} " +
                  $"({spawnDistance}m from camera).");
    }

    // ─── Validation ───────────────────────────────────────────────────────────

    [ContextMenu("Validate All Slots")]
    public void ValidateSetup()
    {
        int nullSlots = 0, nullPrefabs = 0, okSlots = 0;

        Debug.Log("══════════════════════════════════════════════════");
        Debug.Log("[ModelDisplay] SLOT AUDIT");
        Debug.Log("══════════════════════════════════════════════════");

        if (mainCamera == null)
            Debug.LogWarning("[ModelDisplay] ⚠️  mainCamera not assigned.");
        else
            Debug.Log($"[ModelDisplay] ✅ mainCamera: {mainCamera.name}");

        if (spawnPoint == null)
            Debug.LogError("[ModelDisplay] ❌ spawnPoint is NULL!");
        else
            Debug.Log($"[ModelDisplay] ✅ spawnPoint: {spawnPoint.name} at {spawnPoint.position}");

        ScanManager sm = FindFirstObjectByType<ScanManager>();
        if (sm != null)
        {
            if (sm.modelDisplayManager == null)
                Debug.LogError("[ModelDisplay] ❌ ScanManager.modelDisplayManager is NULL! " +
                               "Drag THIS ModelDisplayManager into ScanManager's Inspector slot.");
            else
                Debug.Log("[ModelDisplay] ✅ ScanManager.modelDisplayManager wired correctly.");
        }

        int count = characters == null ? 0 : characters.Length;
        if (count != 46)
            Debug.LogError($"[ModelDisplay] ❌ characters array has {count} slots — expected 46!");

        for (int i = 0; i < count; i++)
        {
            CharacterData d = characters[i];
            if (d == null)
            {
                Debug.LogError($"[ModelDisplay] ❌ Slot [{i:D2}] — NULL");
                nullSlots++;
            }
            else if (d.packagePrefab == null)
            {
                Debug.LogWarning($"[ModelDisplay] ⚠️  Slot [{i:D2}] '{d.devanagariChar}' " +
                                 $"— packagePrefab missing in '{d.name}'");
                nullPrefabs++;
            }
            else
            {
                okSlots++;
            }
        }

        Debug.Log($"[ModelDisplay] RESULT: ✅ {okSlots} OK | " +
                  $"⚠️  {nullPrefabs} missing prefab | ❌ {nullSlots} null slots");
        Debug.Log("══════════════════════════════════════════════════");
    }

    // ─── Context Menu Test Helpers ────────────────────────────────────────────

    [ContextMenu("Test Spawn Index 0 (Ka)")]
    void TestSpawn0()  { _lastIndex = -1; ShowCharacter(0);  }

    [ContextMenu("Test Spawn Index 22 (Ba)")]
    void TestSpawn22() { _lastIndex = -1; ShowCharacter(22); }

    [ContextMenu("Test Spawn Index 32 (Ha)")]
    void TestSpawn32() { _lastIndex = -1; ShowCharacter(32); }

    [ContextMenu("Dismiss Current")]
    void TestDismiss() => DismissCurrentPackage();
}