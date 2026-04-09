using UnityEngine;
using TMPro;

public class SensorDebugBinder : MonoBehaviour
{
    [Tooltip("The TMP_Text in this scene to display sensor readings")]
    public TMP_Text sceneDebugText;

    void Start()
    {
        if (EscooterDataReceiver.Instance != null && sceneDebugText != null)
        {
            EscooterDataReceiver.Instance.debugText = sceneDebugText;
            Debug.Log("[SensorDebugBinder] Bound scene debug text to EscooterDataReceiver.");
        }
        else
        {
            Debug.LogWarning("[SensorDebugBinder] Could not bind debug text — missing reference.");
        }
    }
}
