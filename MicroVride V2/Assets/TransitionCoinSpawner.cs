using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// Transition-based coin spawner for HCI study:
/// - Difficulty is defined by steering delta: delta = |lat[i] - lat[i-1]|
/// - Generator targets a requested Easy/Med/Hard mix
/// - Uniform spacing along path (coinSpacing)
/// - Height/visual offset taken from prefab via optional child "SpawnAnchor", plus airHeight
/// - Bakes in Edit Mode; can export CSV of metadata
[ExecuteAlways]
public class TransitionCoinSpawner : MonoBehaviour
{
    [Header("Path & Prefab")]
    public Transform pathRoot;            // children are ordered waypoints
    public GameObject coinPrefab;         // prefab may contain "SpawnAnchor" child

    [Header("Difficulty")]
    public TransitionDifficultyConfig config;

    [Header("Sampling")]
    [Tooltip("Uniform distance between coins along the path (m).")]
    public float coinSpacing = 2.0f;

    [Header("Randomness")]
    public int randomSeed = 0;
    [Tooltip("Small forward jitter (m) purely for visual variety; doesn't affect metrics.")]
    public float forwardJitter = 0.15f;

    [Header("Edit Mode")]
    public bool generateInEditMode = true;
    public bool autoRegenerateOnChange = false;

    [Header("Play Mode")]
    public bool spawnOnStartPlay = false;

    [Header("Vertical")]
    public float airHeight = 1.2f;   // meters above path

    [Header("Container (created if missing)")]
    public Transform container;
    const string ContainerName = "CoinsContainer";
    const string AnchorName = "SpawnAnchor";

    [Header("XR")]
    [Tooltip("XR headset camera. Drag your XR rig's Main Camera here.")]
    public Transform xrCamera;


    // Runtime cache
    List<Vector3> _samples;      // positions along path
    List<Vector3> _rights;       // right vectors at samples (for lateral)
    List<int> _segIndex;         // segment index per sample

#if UNITY_EDITOR
    bool _pendingRegen;
#endif

    void OnEnable()
    {
        EnsureContainer();
#if UNITY_EDITOR
        if (!Application.isPlaying && generateInEditMode && autoRegenerateOnChange)
            ScheduleEditorRegen();
#endif
    }

    void Start()
    {
        if (Application.isPlaying && spawnOnStartPlay)
            SpawnRuntime();
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        if (!generateInEditMode || !autoRegenerateOnChange) return;
        ScheduleEditorRegen();    // defer — do NOT regenerate synchronously here
#endif
    }

    // ---------------- Buttons ----------------
    [ContextMenu("Bake Coins (Edit Mode)")]
    public void BakeCoins()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) { Debug.LogWarning("Bake is for Edit Mode. Use SpawnRuntime during Play."); return; }
        EditorRegenerateNow();
#else
        Debug.LogWarning("Bake is Editor-only.");
#endif
    }

    [ContextMenu("Clear Baked Coins")]
    public void ClearBaked()
    {
        EnsureContainer();
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // In editor (not play), use DestroyImmediate here — but only from a deferred call
            for (int i = container.childCount - 1; i >= 0; i--)
                DestroyImmediate(container.GetChild(i).gameObject);
            return;
        }
#endif
        // Runtime: deferred destroy
        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);
    }

    [ContextMenu("Spawn Runtime (Play)")]
    public void SpawnRuntime()
    {
        if (!Validate()) return;
        ClearBaked();
        int seed = (randomSeed > 0) ? randomSeed : Environment.TickCount;
        UnityEngine.Random.InitState(seed);
        Debug.Log($"[TransitionCoinSpawner] Using random seed = {seed}");

        BuildPathSamples();

        GenerateAndPlace((prefab, parent) => Instantiate(prefab, parent),
                         registerUndo: false);
    }

    [ContextMenu("Export CSV (coins)")]
    public void ExportCSV()
    {
        var metas = container ? container.GetComponentsInChildren<CoinMeta>() : null;
        if (metas == null || metas.Length == 0)
        {
            Debug.LogWarning("[TransitionCoinSpawner] No CoinMeta found under container.");
            return;
        }

        string dir = Application.isEditor ? Application.dataPath : Application.persistentDataPath;
        string path = Path.Combine(dir, $"coin_log_{name}.csv");

        using (var sw = new StreamWriter(path))
        {
            sw.WriteLine("index,s_meters,lateral_m,delta_lateral_m,label,segmentIndex,world_x,world_y,world_z");
            foreach (var m in metas)
            {
                Vector3 p = m.transform.position;
                sw.WriteLine($"{m.index},{m.s:F3},{m.lateral:F3},{m.deltaLateral:F3},{m.label},{m.segmentIndex},{p.x:F3},{p.y:F3},{p.z:F3}");
            }
        }
        Debug.Log($"[TransitionCoinSpawner] CSV exported: {path}");
    }

    // --------------- Core generation ---------------
#if UNITY_EDITOR
    // Defer editor regeneration to avoid immediate destroy during OnValidate/physics/render callbacks
    void ScheduleEditorRegen()
    {
        if (_pendingRegen) return;
        _pendingRegen = true;
        EditorApplication.delayCall += () =>
        {
            _pendingRegen = false;
            if (this == null) return;            // component may have been deleted
            if (!generateInEditMode) return;
            EditorRegenerateNow();
        };
    }

    void EditorRegenerateNow()
    {
        if (!Validate()) return;
        ClearBaked();                            // safe now (deferred context)
        int seed = (randomSeed > 0) ? randomSeed : Environment.TickCount;
        UnityEngine.Random.InitState(seed);
        Debug.Log($"[TransitionCoinSpawner] Using random seed = {seed}");

        BuildPathSamples();

        GenerateAndPlace(
            (prefab, parent) =>
            {
                var prefabRef = PrefabUtility.GetCorrespondingObjectFromSource(prefab) ?? prefab;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefabRef, parent);
                Undo.RegisterCreatedObjectUndo(inst, "Bake Coin");
                return inst;
            },
            registerUndo: true
        );
        EditorUtility.SetDirty(gameObject);
    }
#endif

    delegate GameObject Instantiator(GameObject prefab, Transform parent);

    void GenerateAndPlace(Instantiator inst, bool registerUndo)
    {
        if (_samples == null || _samples.Count == 0) { Debug.LogWarning("[TransitionCoinSpawner] No path samples."); return; }

        var metas = new List<CoinMeta>(_samples.Count);

        // Counters to track achieved mix
        int nEasy = 0, nMed = 0, nHard = 0;

        float cumulativeS = 0f;
        float prevLat = 0f;     // start on centerline at the first sample
        bool hasPrev = false;

        for (int i = 0; i < _samples.Count; i++)
        {
            Vector3 basePos = _samples[i];
            Vector3 right = _rights[i];

            // Decide desired label to approach target mix (greedy toward deficit)
            DifficultyLabel desired = ChooseLabelTowardsTarget(nEasy, nMed, nHard, i + 1);

            // Sample a lateral that satisfies the desired bucket
            float lat;
            bool hitBucket = TrySampleLateral(desired, prevLat, config, out lat);

            // If we failed to hit the bucket after attempts, fall back to closest feasible
            if (!hitBucket)
                lat = Mathf.Clamp(prevLat + UnityEngine.Random.Range(-config.medMax, config.medMax), -config.maxLateralAmplitude, config.maxLateralAmplitude);

            float delta = hasPrev ? Mathf.Abs(lat - prevLat) : 0f;
            var label = Classify(delta, config);

            // Update counters
            if (label == DifficultyLabel.Easy) nEasy++;
            else if (label == DifficultyLabel.Medium) nMed++;
            else nHard++;

            // Forward jitter for visuals only (doesn't affect s/lateral)
            Vector3 fwdJit = Vector3.zero;
            if (forwardJitter > 0f && i < _samples.Count - 1)
            {
                Vector3 tangent = (_samples[Mathf.Min(i + 1, _samples.Count - 1)] - _samples[i]).normalized;
                fwdJit = tangent * UnityEngine.Random.Range(-forwardJitter, forwardJitter);
            }

            // Final world pose before anchor
            Vector3 pos = basePos + right * lat + fwdJit;
            pos.y += airHeight;
            Quaternion rot = coinPrefab.transform.rotation; // keep authored rotation

            // Instantiate first to read its anchor
            GameObject coin = inst(coinPrefab, container);

            int coinsLayer = LayerMask.NameToLayer("Coins");
            if (coinsLayer >= 0) coin.layer = coinsLayer;

            // (NEW) Ensure not static so it can rotate in Play
#if UNITY_EDITOR
            GameObjectUtility.SetStaticEditorFlags(coin, 0);
#endif

            // (NEW) Pass the XR camera to the Coin so it can billboard
            var coinComp = coin.GetComponent<Coin>();
            if (coinComp != null)
            {
                if (xrCamera == null)
                {
                    // fallback: try Camera.main or any active camera
                    var cam = Camera.main ? Camera.main.transform
                                          : (Camera.allCameras.Length > 0 ? Camera.allCameras[0].transform : null);
                    //coinComp.SetHeadset(cam);
                }
                else
                {
                    //coinComp.SetHeadset(xrCamera);
                }
            }

            Vector3 localAnchor = Vector3.zero;
            var anchor = coin.transform.Find(AnchorName);
            if (anchor) localAnchor = anchor.localPosition;

            Vector3 worldPos = pos - (rot * localAnchor);
            coin.transform.SetPositionAndRotation(worldPos, rot);

            // Metadata
            var meta = coin.GetComponent<CoinMeta>() ?? coin.AddComponent<CoinMeta>();
            meta.index = i;
            meta.s = cumulativeS;
            meta.lateral = lat;
            meta.deltaLateral = delta;
            meta.label = label;
            meta.segmentIndex = _segIndex[i];
            metas.Add(meta);

            // Prepare next iteration
            hasPrev = true;
            prevLat = lat;

            // Advance s
            if (i < _samples.Count - 1)
                cumulativeS += Vector3.Distance(_samples[i], _samples[i + 1]);
        }

        Debug.Log($"[TransitionCoinSpawner] Spawned {metas.Count} coins. Mix achieved — Easy:{nEasy}, Med:{nMed}, Hard:{nHard}");
    }

    // Choose the label that best moves us toward the target mix
    DifficultyLabel ChooseLabelTowardsTarget(int nE, int nM, int nH, int placedCount)
    {
        float total = Mathf.Max(1, placedCount - 1); // previous placed
        float pE = nE / total, pM = nM / total, pH = nH / total;

        float dE = config.targetEasy - pE;
        float dM = config.targetMedium - pM;
        float dH = config.targetHard - pH;

        // Pick the largest positive deficit; if all negative, pick the least negative
        if (dE >= dM && dE >= dH) return DifficultyLabel.Easy;
        if (dM >= dE && dM >= dH) return DifficultyLabel.Medium;
        return DifficultyLabel.Hard;
    }

    // Attempt to sample a lateral satisfying the desired bucket
    bool TrySampleLateral(DifficultyLabel desired, float prevLat, TransitionDifficultyConfig cfg, out float lat)
    {
        lat = 0f;
        for (int k = 0; k < Mathf.Max(1, cfg.attemptsPerCoin); k++)
        {
            float candidate = UnityEngine.Random.Range(-cfg.maxLateralAmplitude, cfg.maxLateralAmplitude);
            float delta = Mathf.Abs(candidate - prevLat);
            var label = Classify(delta, cfg);
            if (label == desired)
            {
                lat = candidate;
                return true;
            }
        }
        return false;
    }

    DifficultyLabel Classify(float delta, TransitionDifficultyConfig cfg)
    {
        if (delta < cfg.easyMax) return DifficultyLabel.Easy;
        if (delta < cfg.medMax) return DifficultyLabel.Medium;
        return DifficultyLabel.Hard;
    }

    // --------------- Path sampling ---------------
    void BuildPathSamples()
    {
        _samples = new List<Vector3>();
        _rights = new List<Vector3>();
        _segIndex = new List<int>();

        var wps = GetWaypoints();
        if (wps.Count < 2 || coinSpacing <= 0.001f) return;

        Vector3 up = Vector3.up;

        // Precompute per-segment starts, lens, dirs, rights, cumulative distances
        int segCount = wps.Count - 1;
        var segStartS = new float[segCount];
        var segLen = new float[segCount];
        var segDir = new Vector3[segCount];
        var segRight = new Vector3[segCount];

        float totalLen = 0f;
        for (int i = 0; i < segCount; i++)
        {
            segStartS[i] = totalLen;

            Vector3 a = wps[i].position;
            Vector3 b = wps[i + 1].position;
            Vector3 d = b - a;
            float L = d.magnitude;
            segLen[i] = L;
            segDir[i] = (L > 1e-6f) ? d / L : Vector3.forward;

            // true lateral: orthogonal to tangent
            Vector3 r = Vector3.Cross(up, segDir[i]);
            if (r.sqrMagnitude < 1e-6f) // degenerate backup
            {
                r = Vector3.Cross(segDir[i], Vector3.right);
                if (r.sqrMagnitude < 1e-6f) r = Vector3.Cross(segDir[i], Vector3.forward);
            }
            segRight[i] = r.normalized;

            totalLen += L;
        }

        // ---- Global stepping along the whole path ----
        // Start at coinSpacing so you don't hit one "immediately" at s=0.
        // If you DO want the first coin right at the start, set startS = 0f.
        float startS = Mathf.Min(coinSpacing, totalLen);
        for (float s = startS; s <= totalLen + 1e-4f; s += coinSpacing)
        {
            // find segment index for this s
            int seg = FindSegmentForS(s, segStartS, segLen);
            if (seg < 0) break;

            float sLocal = s - segStartS[seg];
            sLocal = Mathf.Clamp(sLocal, 0f, segLen[seg]);

            Vector3 a = wps[seg].position;
            Vector3 p = a + segDir[seg] * sLocal;

            _samples.Add(p);
            _rights.Add(segRight[seg]);
            _segIndex.Add(seg);
        }

        // Safety: if spacing was too large (path shorter than spacing), at least place ONE coin at middle
        if (_samples.Count == 0 && totalLen > 0f)
        {
            float midS = totalLen * 0.5f;
            int seg = FindSegmentForS(midS, segStartS, segLen);
            if (seg >= 0)
            {
                float sLocal = midS - segStartS[seg];
                Vector3 a = wps[seg].position;
                Vector3 p = a + segDir[seg] * sLocal;
                _samples.Add(p);
                _rights.Add(segRight[seg]);
                _segIndex.Add(seg);
            }
        }

        Debug.Log($"[TransitionCoinSpawner] PathLen={totalLen:F2} m, coinSpacing={coinSpacing:F2} m, samples={_samples.Count}");
    }

    int FindSegmentForS(float s, float[] segStartS, float[] segLen)
    {
        for (int i = 0; i < segStartS.Length; i++)
        {
            float a = segStartS[i];
            float b = a + segLen[i];
            if (s >= a - 1e-5f && s <= b + 1e-5f) return i;
        }
        return -1;
    }



    // --------------- Helpers ---------------
    void EnsureContainer()
    {
        if (container) return;
        var found = transform.Find(ContainerName);
        if (found) { container = found; return; }
        var go = new GameObject(ContainerName);
        go.transform.SetParent(transform, false);
        container = go.transform;
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(go, "Create Coins Container");
#endif
    }

    List<Transform> GetWaypoints()
    {
        var list = new List<Transform>();
        if (!pathRoot) return list;
        for (int i = 0; i < pathRoot.childCount; i++)
            list.Add(pathRoot.GetChild(i));
        return list;
    }

    bool Validate()
    {
        if (!pathRoot) { Debug.LogError("[TransitionCoinSpawner] pathRoot missing."); return false; }
        if (!coinPrefab) { Debug.LogError("[TransitionCoinSpawner] coinPrefab missing."); return false; }
        if (!config) { Debug.LogError("[TransitionCoinSpawner] config missing (TransitionDifficultyConfig)."); return false; }
        return true;
    }

    void OnDrawGizmos()
    {
        if (!pathRoot) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        for (int i = 0; i < pathRoot.childCount; i++)
        {
            var t = pathRoot.GetChild(i);
            Gizmos.DrawSphere(t.position, 0.06f);
            Gizmos.DrawLine(t.position, t.position + t.forward * 0.5f);
        }
    }
}
