using UnityEngine;

public class FaceHeadset : MonoBehaviour
{
    Transform cam;

    void Start()
    {
        if (Camera.main) cam = Camera.main.transform;
    }

    void LateUpdate()
    {
        if (!cam) return;

        // Face the camera on Y only (upright like a sign)
        Vector3 dir = transform.position - cam.position;
        dir.y = 0f; // lock vertical
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);

        // If you want full facing (not just Y), remove dir.y=0f and lock vertical line above.
    }
}
