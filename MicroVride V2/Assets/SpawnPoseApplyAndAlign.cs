using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// One-shot alignment:
/// 1) Align XR Origin yaw and XZ to the captured head pose (ignores captured Y),
/// 2) Set XR Origin Y so the camera sits exactly at (ground + desiredEyeHeight),
/// 3) Place Segway under the head,
/// 4) Re-enable CC/locomotion and stop.
[DefaultExecutionOrder(1000)]
public class SpawnPoseApplyAndAlign: MonoBehaviour
{
    [Header("Simulator XR Rig (this scene)")]
    public Transform xrOrigin;     // XR rig root (parent of the camera/camera offset)
    public Transform simHeadset;   // XR camera (Main Camera)

    [Header("Segway root to place (optional if this is on the segway)")]
    public Transform segwayRoot;   // if null, uses this.transform

    [Header("Ground / Eye Height")]
    public LayerMask groundMask = ~0;
    public float desiredEyeHeight = 1.70f;   // meters
    public bool snapSegwayToGround = true;
    public float segwayGroundSnap = 0.02f;

    [Header("Ignore Start-scene Y")]
    public bool ignoreCapturedY = true;      // align only XZ + yaw from captured pose

    [Header("Freeze while applying")]
    public bool freezeCharacterController = true;
    public Behaviour[] disableWhileApplying; // e.g. locomotion providers, gravity drivers

    [Header("Timing")]
    public float maxWaitSeconds = 3f;
    public int applyFrames = 10;             // apply for N LateUpdates to beat other scripts

    // --- internals ---
    CharacterController xrCC;
    bool ccWasEnabled;
    readonly List<(Behaviour b, bool wasEnabled)> frozen = new();
    Transform targetSeg;
    int framesLeft;
    bool primed;

    void Awake()
    {
        targetSeg = segwayRoot ? segwayRoot : transform;

        if (!simHeadset && Camera.main) simHeadset = Camera.main.transform;
        if (!xrOrigin && simHeadset) xrOrigin = simHeadset.root;

        if (xrOrigin) xrCC = xrOrigin.GetComponent<CharacterController>();
    }

    void OnEnable() { StartCoroutine(Prepare()); }

    IEnumerator Prepare()
    {
        if (!SpawnPoseStore.HasData) { enabled = false; yield break; }

        float t = 0f;
        while (t < maxWaitSeconds)
        {
            if (!simHeadset && Camera.main) simHeadset = Camera.main.transform;
            if (!xrOrigin && simHeadset) xrOrigin = simHeadset.root;

            if (xrOrigin && simHeadset && IsPoseValid(simHeadset.position))
                break;

            t += Time.deltaTime;
            yield return null;
        }

        if (!xrOrigin || !simHeadset) { enabled = false; yield break; }

        // Freeze controllers that fight height/pose
        if (freezeCharacterController && xrCC)
        {
            ccWasEnabled = xrCC.enabled;
            xrCC.enabled = false;
        }
        frozen.Clear();
        if (disableWhileApplying != null)
        {
            foreach (var b in disableWhileApplying)
            {
                if (!b) continue;
                frozen.Add((b, b.enabled));
                b.enabled = false;
            }
        }

        framesLeft = Mathf.Max(1, applyFrames);
        primed = true;
    }

    void LateUpdate()
    {
        if (!primed || framesLeft <= 0) return;

        ApplyOnce();  // do the alignment and exact-height set

        framesLeft--;
        if (framesLeft == 0)
        {
            // restore frozen components
            if (freezeCharacterController && xrCC)
                xrCC.enabled = ccWasEnabled;

            foreach (var (b, was) in frozen)
                if (b) b.enabled = was;

            enabled = false;
        }
    }

    void ApplyOnce()
    {
        // 1) Align yaw and XZ to captured head pose (ignore captured Y)
        Vector3 camWorld = simHeadset.position;
        float camYaw = simHeadset.eulerAngles.y;

        Vector3 tgtHead = SpawnPoseStore.TargetHeadWorldPos;
        float tgtYaw = SpawnPoseStore.TargetHeadYawDeg;

        if (ignoreCapturedY) tgtHead.y = camWorld.y;

        // rotate origin so camera yaw matches target
        float yawDelta = Mathf.DeltaAngle(camYaw, tgtYaw);
        xrOrigin.RotateAround(camWorld, Vector3.up, yawDelta);

        // translate origin so camera XZ matches target
        camWorld = simHeadset.position; // refresh after rotation
        Vector3 delta = tgtHead - camWorld;
        if (ignoreCapturedY) delta.y = 0f;
        MoveOrigin(delta);

        // 2) Compute ground under camera and set Origin.y so camera = ground + desiredEyeHeight
        // (no Camera Offset edits; this is absolute and robust)
        if (TryGroundUnder(simHeadset.position, out float groundY))
        {
            // camera local Y relative to XR Origin:
            float cameraLocalY = GetCameraLocalY();

            // desired Origin.y so that (Origin.y + cameraLocalY) == groundY + desiredEyeHeight
            float desiredOriginY = desiredEyeHeight;

            Vector3 op = xrOrigin.position;
            op.y = desiredOriginY;
            xrOrigin.position = op;
        }

        // 3) Place Segway under head
        Vector3 headWorld = simHeadset.position;
        float headYaw = simHeadset.eulerAngles.y;
        Quaternion yawRot = Quaternion.Euler(0f, headYaw, 0f);

        Vector3 segPos = headWorld - (yawRot * SpawnPoseStore.DeckOffsetLocal);

        if (snapSegwayToGround && TryGroundUnder(segPos, out float groundY2))
            segPos.y = groundY2 + segwayGroundSnap;

        targetSeg.SetPositionAndRotation(segPos, yawRot);
    }

    float GetCameraLocalY()
    {
        // camera local pos relative to XR Origin
        // (works even if there's an intermediate "Camera Offset" object)
        return xrOrigin.InverseTransformPoint(simHeadset.position).y;
    }

    bool TryGroundUnder(Vector3 worldPos, out float groundY)
    {
        // generous raycast down to find ground
        Vector3 start = worldPos + Vector3.up * 3f;
        if (Physics.Raycast(start, Vector3.down, out var hit, 100f, groundMask, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }
        groundY = worldPos.y;
        return false;
    }

    void MoveOrigin(Vector3 delta)
    {
        if (xrCC && xrCC.enabled) xrCC.Move(delta);
        else xrOrigin.position += delta;
    }

    static bool IsPoseValid(Vector3 p)
    {
        if (!float.IsFinite(p.x + p.y + p.z)) return false;
        return p.y > 0.2f;
    }
}
