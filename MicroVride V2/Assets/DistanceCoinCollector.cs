using System.Collections.Generic;
using UnityEngine;
using Study;

public class DistanceCoinCollector : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Meters from vehicle center to count as a pickup.")]
    public float collectRadius = 0.75f;

    [Header("Filters (optional)")]
    [Tooltip("Only consider objects on this layer. Set to -1 to ignore.")]
    public int coinsLayer = -1;                       // e.g. LayerMask.NameToLayer("Coins")
    [Tooltip("Only consider objects with this tag. Leave empty to ignore.")]
    public string coinTag = "";                       // e.g. "Coin"
    [Tooltip("Require a Coin component on the object to be collectible.")]
    public bool requireCoinComponent = false;

    [Header("Scanning")]
    [Tooltip("Re-scan for coins every X seconds. 0 = no periodic rescan.")]
    public float rescanInterval = 0.0f;
    [Tooltip("Also do a deferred re-scan a short time after Start (helps if spawner runs in Start).")]
    public float deferredScanDelay = 0.15f;

    [Header("Behaviour")]
    [Tooltip("Destroy coin object after collection.")]
    public bool destroyCoin = true;
    [Tooltip("Delay before destroying coin object (seconds). 0 = immediate.")]
    public float destroyDelay = 0.05f;

    [Header("Fallback FX if Coin component is missing")]
    public AudioClip defaultPickupSound;
    public ParticleSystem defaultPickupParticles;

    // Internal
    private readonly List<Transform> _coins = new List<Transform>();
    private readonly HashSet<int> _collectedIds = new HashSet<int>();   // prevent double collect
    private float _scanT = 0f;
    private Transform _self;

    [Header("Target")]
    [Tooltip("If assigned, use this transform position for distance checks. Defaults to this component's transform.")]
    public Transform target;

    [Header("Cylinder pickup")]
    [Tooltip("Horizontal pickup radius (X/Z only).")]
    public float horizontalRadius = 1.0f;

    [Tooltip("Max vertical difference allowed to collect (|dy|).")]
    public float verticalTolerance = 2.0f;


    void Awake()
    {
        _self = transform;
        Vector3 p = (target ? target.position : _self.position);
        float r2 = horizontalRadius * horizontalRadius;

    }

    void OnEnable()
    {
        // Immediate scan
        ScanCoins();

        // Deferred scan to catch coins spawned in Start()
        if (deferredScanDelay > 0f)
            Invoke(nameof(ScanCoins), deferredScanDelay);
    }

    void Start()
    {
        // If you want a periodic rescan, set rescanInterval > 0 in Inspector
        // Otherwise, two scans above usually suffice for Start()-spawned coins.
    }

    void Update()
    {
        if (rescanInterval > 0f)
        {
            _scanT += Time.deltaTime;
            if (_scanT >= rescanInterval)
            {
                _scanT = 0f;
                ScanCoins();
            }
        }

        if (_coins.Count == 0) return;

        Vector3 p = (target ? target.position : _self.position);
        float r2 = horizontalRadius * horizontalRadius;

        for (int i = _coins.Count - 1; i >= 0; i--)
        {
            var tr = _coins[i];
            if (tr == null) { _coins.RemoveAt(i); continue; }

            // already collected?
            int id = tr.GetInstanceID();
            if (_collectedIds.Contains(id)) { _coins.RemoveAt(i); continue; }

            // distance vector
            Vector3 dv = tr.position - p;

            // Horizontal distance (ignore Y)
            float h2 = dv.x * dv.x + dv.z * dv.z;
            float vy = Mathf.Abs(dv.y);

            if (h2 <= r2 && vy <= verticalTolerance)
            {
                Debug.Log($"[DistanceCoinCollector] Collecting {tr.name}  h={Mathf.Sqrt(h2):F2}  dy={vy:F2}");
                Collect(tr);
                _coins.RemoveAt(i);
                _collectedIds.Add(id);
            }
        }
    }


    public void ScanCoins()
    {
        _coins.Clear();

        // 1) Prefer exact Coin components (if required)
        if (requireCoinComponent)
        {
            var coins = FindObjectsOfType<Coin>(includeInactive: false);
            foreach (var c in coins)
            {
                if (c == null) continue;
                if (coinsLayer >= 0 && c.gameObject.layer != coinsLayer) continue;
                if (!string.IsNullOrEmpty(coinTag) && !c.CompareTag(coinTag)) continue;
                _coins.Add(c.transform);
            }
        }
        else
        {
            // 2) Collect by layer/tag, no Coin component required
            // Layer-only case
            if (coinsLayer >= 0 && string.IsNullOrEmpty(coinTag))
            {
                var all = FindObjectsOfType<Transform>(includeInactive: false);
                foreach (var t in all)
                {
                    if (t.gameObject.layer == coinsLayer) _coins.Add(t);
                }
            }
            // Tag-only case
            else if (coinsLayer < 0 && !string.IsNullOrEmpty(coinTag))
            {
                var tagged = GameObject.FindGameObjectsWithTag(coinTag);
                foreach (var go in tagged) _coins.Add(go.transform);
            }
            // Both filters
            else if (coinsLayer >= 0 && !string.IsNullOrEmpty(coinTag))
            {
                var tagged = GameObject.FindGameObjectsWithTag(coinTag);
                foreach (var go in tagged)
                {
                    if (go.layer == coinsLayer) _coins.Add(go.transform);
                }
            }
            // No filters: fall back to Coin components if present, else nothing
            else
            {
                var coins = FindObjectsOfType<Coin>(includeInactive: false);
                foreach (var c in coins) _coins.Add(c.transform);
            }
        }

        Debug.Log($"[DistanceCoinCollector] Found coins = {_coins.Count}");
    }

    private void Collect(Transform tr)
    {
        // Optional: if a Coin component is present, mark it (not required)
        var coin = tr.GetComponent<Coin>();
        if (coin != null) coin.collected = true;

        // 1) Study/game logic
        if (GameManager.Instance != null) GameManager.Instance.AddCoin(1);

        var meta = tr.GetComponent<CoinMeta>();
        if (meta != null && StudyLogger.Instance != null)
            StudyLogger.Instance.LogCoinCollected(meta);

        // 2) FX
        var clip = (coin && coin.pickupSound) ? coin.pickupSound : defaultPickupSound;
        if (clip) AudioSource.PlayClipAtPoint(clip, tr.position);

        var fxPrefab = (coin && coin.pickupParticles) ? coin.pickupParticles : defaultPickupParticles;
        if (fxPrefab)
        {
            var fx = Instantiate(fxPrefab, tr.position, Quaternion.identity);
            fx.Play();
            Destroy(fx.gameObject, fx.main.duration + fx.main.startLifetime.constantMax);
        }

        // 3) Hide visuals now
        foreach (Transform child in tr) child.gameObject.SetActive(false);

        // 4) Destroy coin root
        if (destroyCoin) Destroy(tr.gameObject, Mathf.Max(0f, destroyDelay));
    }
}
