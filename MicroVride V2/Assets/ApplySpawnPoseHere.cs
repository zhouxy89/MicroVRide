using System.Collections;
using UnityEngine;

/// Apply a one-time spawn pose using XRSpawnCoordinator data.
/// This runs for a few LateUpdate frames to beat any other writers, then disables itself.
[DefaultExecutionOrder(1000)]
public class ApplySpawnPoseHere : MonoBehaviour
{
    [Header("XR Refs")]
    public Transform xrOrigin;   // assign your XR Origin root (e.g., XROrigin / XR Rig). If null, will try to infer from Camera.main.parent.
    public Transform headset;    // assign the XR camera; if null, tries Camera.main

    [Header("Ground Snap")]
    public bool snapToGround = true;
    public LayerMask groundMask = ~0;
    public float groundSnap = 0.02f;

    [Header("Stability")]
    [Tooltip("Max seconds to wait for valid XR objects/poses.")]
    public float maxWaitSeconds = 2f;
    [Tooltip("Apply pose every LateUpdate for this many frames, then stop.")]
    public int applyFrames = 8;

    Transform t;
    int framesLeft;
    bool primed;

    void Awake()
    {
        t = transform;
        if (!headset && Camera.main) headset = Camera.main.transform;
        if (!xrOrigin && headset) xrOrigin = headset.root; // fallback guess
    }

    void OnEnable()
    {
        StartCoroutine(PrepareAndPrime());
    }

    IEnumerator PrepareAndPrime()
    {
        var coord = XRSpawnCoordinator.Ensure();
        float t0 = 0f;

        // Wait until we have data and XR objects with valid pose
        while (t0 < maxWaitSeconds)
        {
            if (coord.hasData && xrOrigin && (headset || Camera.main))
            {
                if (!headset && Camera.main) headset = Camera.main.transform;
                if (IsPoseValid(headset)) break;
            }
            t0 += Time.deltaTime;
            yield return null;
        }

        if (!coord.hasData || !xrOrigin || !headset)
        {
            enabled = false;
            yield break;
        }

        framesLeft = Mathf.Max(1, applyFrames);
        primed = true;
    }

    void LateUpdate()
    {
        if (!primed || framesLeft <= 0) return;
        ApplyOnce();
        framesLeft--;
        if (framesLeft == 0) enabled = false;
    }

    void ApplyOnce()
    {
        var coord = XRSpawnCoordinator.Instance;
        if (coord == null || !coord.hasData || xrOrigin == null) return;

        // World head position reconstructed from XR Origin + stored local
        Vector3 headWorld = xrOrigin.TransformPoint(coord.headLocalToOrigin);
        float originYaw = xrOrigin.eulerAngles.y;
        float headYaw = originYaw + coord.headYawRelDeg;
        Quaternion yawRot = Quaternion.Euler(0f, headYaw, 0f);

        // Position Segway so (Segway * deckOffsetLocal) == headWorld
        Vector3 segPos = headWorld - (yawRot * coord.deckOffsetLocal);

        if (snapToGround)
        {
            Vector3 origin = segPos + Vector3.up * 2f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 5f, groundMask, QueryTriggerInteraction.Ignore))
                segPos.y = hit.point.y + groundSnap;
        }

        t.SetPositionAndRotation(segPos, yawRot);

        // Optional debug:
        // Debug.Log($"[ApplySpawnPoseHere] Applied pose @ {segPos} yaw {headYaw:F1}° ({framesLeft} frames left)");

        // Do NOT clear here — leaving data available in case you re-enter another sim quickly.
        // XRSpawnCoordinator.Instance.Clear(); // call this yourself if you prefer one-shot across whole session
    }

    static bool IsPoseValid(Transform cam)
    {
        if (!cam) return false;
        var p = cam.position;
        if (!float.IsFinite(p.x + p.y + p.z)) return false;
        return p.y > 0.2f; // avoid (0,0,0) default
    }
}
