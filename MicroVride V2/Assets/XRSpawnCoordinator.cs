using UnityEngine;

public class XRSpawnCoordinator : MonoBehaviour
{
    public static XRSpawnCoordinator Instance { get; private set; }

    // Stored from Start scene at the instant user selects a vehicle
    public bool hasData { get; private set; }
    public Vector3 headLocalToOrigin { get; private set; } // headset pos in XR Origin local space
    public float headYawRelDeg { get; private set; }       // headset yaw relative to XR Origin yaw
    public Vector3 deckOffsetLocal { get; private set; }   // Segway local offset to user stand point

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static XRSpawnCoordinator Ensure()
    {
        if (!Instance)
        {
            var go = new GameObject("XRSpawnCoordinator");
            Instance = go.AddComponent<XRSpawnCoordinator>();
        }
        return Instance;
    }

    /// Capture headset pose RELATIVE to the XR Origin root at selection time.
    public void Capture(Transform xrOrigin, Transform headset, Vector3 deckOffset)
    {
        if (!xrOrigin || !headset)
        {
            hasData = false;
            Debug.LogWarning("[XRSpawnCoordinator] Missing xrOrigin/headset, spawn capture aborted.");
            return;
        }

        headLocalToOrigin = xrOrigin.InverseTransformPoint(headset.position);
        float originYaw = xrOrigin.eulerAngles.y;
        float headYaw = headset.eulerAngles.y;
        headYawRelDeg = Mathf.DeltaAngle(originYaw, headYaw); // head - origin
        deckOffsetLocal = deckOffset;
        hasData = true;

        // Optional debug:
        // Debug.Log($"[XRSpawnCoordinator] Captured local head {headLocalToOrigin}, yawRel {headYawRelDeg:F1}°");
    }

    public void Clear() => hasData = false;
}
