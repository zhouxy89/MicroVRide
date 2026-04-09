using UnityEngine;

public class SegwayApplySpawnPoseOnLoad : MonoBehaviour
{
    [Header("Optional ground snap")]
    public bool snapToGround = true;
    public LayerMask groundMask = ~0;
    public float groundSnap = 0.02f;

    void Awake()
    {
        if (!SessionState.HasSpawnPose) return;

        Vector3 pos = SessionState.SpawnPosition;
        float yaw = SessionState.SpawnYawDeg;

        if (snapToGround)
        {
            Vector3 origin = pos + Vector3.up * 2f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 5f, groundMask, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y + groundSnap;
        }

        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

        // consume the spawn pose so it won't re-apply
        SessionState.HasSpawnPose = false;
    }
}
