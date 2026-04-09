using UnityEngine;

public class XRCameraFollower : MonoBehaviour
{
    public Transform vehicle;  // your physics vehicle (with Rigidbody)
    public Transform xrRig;    // XR Rig root (OVRCameraRig or XR Origin)
    public bool matchRotation = true;

    void LateUpdate()
    {
        if (vehicle == null || xrRig == null) return;

        // Smooth follow (no physics jitter)
        xrRig.position = vehicle.position;

        if (matchRotation)
            xrRig.rotation = vehicle.rotation;
    }
}
