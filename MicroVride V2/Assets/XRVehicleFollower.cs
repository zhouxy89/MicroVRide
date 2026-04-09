using UnityEngine;

public class XRVehicleFollower : MonoBehaviour
{
    [Header("References")]
    public Transform segwayTransform;      // This is your Segway or vehicle root transform
    public Transform xrOriginTransform;    // This is the XR Origin (Action-based) GameObject

    [Header("Smoothing Settings")]
    public float positionLerpSpeed = 5f;
    public float rotationLerpSpeed = 2f;

    private Vector3 initialOffset;

    void Start()
    {
        if (segwayTransform && xrOriginTransform)
            initialOffset = xrOriginTransform.position - segwayTransform.position;
    }

    void Update()
    {
        if (segwayTransform && xrOriginTransform)
        {
            // Smoothly follow position
            Vector3 targetPosition = segwayTransform.position + initialOffset;
            xrOriginTransform.position = Vector3.Lerp(xrOriginTransform.position, targetPosition, Time.deltaTime * positionLerpSpeed);

            // Smoothly follow rotation
            Quaternion targetRotation = segwayTransform.rotation;
            xrOriginTransform.rotation = Quaternion.Slerp(xrOriginTransform.rotation, targetRotation, Time.deltaTime * rotationLerpSpeed);
        }
    }
}