using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SegwayGrounding : MonoBehaviour
{
    [Header("Grounding")]
    public float castHeight = 2.0f;        // how high above the segway to start the ground ray
    public float upOffset = 0.03f;        // small lift above the ground after snap
    public LayerMask groundMask = ~0;      // layers considered ground

    [Header("Gravity restore")]
    public float contactCheckRadius = 0.15f;
    public float contactCheckDistance = 0.2f;
    public float maxSettleTime = 1.5f;     // seconds to wait before restoring gravity anyway

    Rigidbody rb;
    bool originalUseGravity;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        originalUseGravity = rb.useGravity;
    }

    void OnEnable()
    {
        StartCoroutine(SnapAndSettle());
    }

    IEnumerator SnapAndSettle()
    {
        // 1) Temporarily turn gravity off so we don't drop while scene wakes up
        rb.useGravity = false;

        // 2) Snap the segway to the ground once
        SnapToGround();

        // 3) Let other components finish Start() for a couple frames
        yield return null;
        yield return null;

        // 4) Wait until there's ground under us, then restore original gravity
        float t = 0f;
        while (t < maxSettleTime)
        {
            if (IsGrounded())
            {
                rb.useGravity = originalUseGravity;   // restore your rig’s intended gravity
                yield break;
            }
            t += Time.deltaTime;
            yield return null;
        }

        // Fallback: restore gravity even if ground wasn't detected (e.g., custom ground layers)
        rb.useGravity = originalUseGravity;
    }

    void SnapToGround()
    {
        Vector3 origin = transform.position + Vector3.up * castHeight;

        if (Physics.Raycast(origin, Vector3.down, out var hit, castHeight * 5f, groundMask))
        {
            transform.position = hit.point + Vector3.up * upOffset;
            return;
        }

        // try from current position as fallback
        if (Physics.Raycast(transform.position, Vector3.down, out hit, castHeight * 5f, groundMask))
        {
            transform.position = hit.point + Vector3.up * upOffset;
        }
    }

    bool IsGrounded()
    {
        Vector3 origin = rb.worldCenterOfMass + Vector3.up * 0.05f;
        return Physics.SphereCast(origin, contactCheckRadius, Vector3.down, out _, contactCheckDistance, groundMask);
    }
}
