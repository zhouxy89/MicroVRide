using UnityEngine;


public class Coin : MonoBehaviour
{
    [Header("Spin Only")]
    [Tooltip("Spin speed (degrees/second) around world Y.")]
    public float rotationSpeed = 90f;

    [Header("Pickup FX")]
    public AudioClip pickupSound;
    public ParticleSystem pickupParticles;

    [Header("Trigger Filter")]
    public string playerTag = "Player";
    public LayerMask playerLayers = default;

    private AudioSource audioSource;

    static bool _coinLayerIgnored = false;

    [HideInInspector] public bool collected = false; // set by collector

    // ----------- Setup -----------
    void Reset()
    {
        var col = GetComponent<Collider>() ?? gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false;
    }

    void Awake()
    {
        if (!_coinLayerIgnored)
        {
            int coinsLayer = LayerMask.NameToLayer("Coins");
            if (coinsLayer >= 0)
                Physics.IgnoreLayerCollision(coinsLayer, coinsLayer, true); // Coins vs Coins never collide/trigger
            _coinLayerIgnored = true;
        }

        // enforce trigger+RB
        var col = GetComponent<Collider>(); if (col) col.isTrigger = false;
        var rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false;

    
    }

    // ----------- Animate (spin only) -----------
    void Update()
    {
        // Simple continuous spin around world Y axis
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    // ----------- Trigger -----------
void OnTriggerEnter(Collider other)
{
    bool tagOk =
        (!string.IsNullOrEmpty(playerTag) &&
         (other.CompareTag(playerTag) ||
          (other.transform.root != null && other.transform.root.CompareTag(playerTag)) ||
          (other.GetComponentInParent<Transform>() != null &&
           other.GetComponentInParent<Transform>().CompareTag(playerTag))));

    bool layerOk = (playerLayers.value != 0) && ((playerLayers.value & (1 << other.gameObject.layer)) != 0);

    if (!(tagOk || layerOk)) return;

    // ✅ Immediately disable collider so it can’t stop vehicle physics
    var col = GetComponent<Collider>();
    if (col) col.enabled = false;

        var gm = GameManager.Instance;
        if (gm != null) gm.AddCoin(1);

        // ✅ Play audio safely (doesn’t require AudioSource on prefab)
        if (pickupSound != null)
    {
        AudioSource.PlayClipAtPoint(pickupSound, transform.position);
    }

    // Particles
    if (pickupParticles != null)
    {
        var fx = Instantiate(pickupParticles, transform.position, Quaternion.identity);
        fx.Play();
        Destroy(fx.gameObject, fx.main.duration + fx.main.startLifetime.constantMax);
    }

    // Log metadata
    var meta = GetComponent<CoinMeta>();
    if (meta != null && Study.StudyLogger.Instance != null)
    {
        Study.StudyLogger.Instance.LogCoinCollected(meta);
    }

    // Hide visuals (optional now, collider is already disabled)
    foreach (Transform child in transform) child.gameObject.SetActive(false);

    // Destroy coin root shortly after
    Destroy(gameObject, 0.05f);
}

}
