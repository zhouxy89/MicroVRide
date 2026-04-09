using UnityEngine;
using TMPro;

public class TestMovementDebugUI : MonoBehaviour
{
    public TMP_Text debugText; // Assign in Inspector

    void Update()
    {
        if (VehicleDataReceiver.Instance != null)
        {
            float t = VehicleDataReceiver.Instance.throttle;

            // Show on UI instead of console
            if (debugText != null)
            {
                debugText.text = $"Throttle: {t:F2}";
            }

            // Test movement: move forward if throttle > 0.1
            if (t > 0.1f)
            {
                transform.Translate(Vector3.forward * t * 5f * Time.deltaTime);
            }
        }
        else
        {
            if (debugText != null)
                debugText.text = "No VehicleDataReceiver instance found.";
        }
    }
}
