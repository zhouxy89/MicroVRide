// VehicleSceneLoader.cs
using UnityEngine;
using UnityEngine.SceneManagement;

public class VehicleSceneLoader : MonoBehaviour
{
    public void LoadSimulationScene()
    {
        // Optionally validate that calibration and selection are complete
        if (UserSimulationSettings.CalibrationComplete)
        {
            SceneManager.LoadScene("VehicleSimulationScene");  // Use exact scene name
        }
        else
        {
            Debug.LogWarning("Calibration not complete. Cannot proceed.");
        }
    }
}
