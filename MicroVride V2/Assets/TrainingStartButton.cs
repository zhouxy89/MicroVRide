using UnityEngine;
using UnityEngine.SceneManagement;

public class TrainingStartButton : MonoBehaviour
{
    public void StartTheGame()
    {
        // Read the vehicle picked in Start scene
        var type = SessionState.SelectedVehicle;

        string simulator = SimulatorSceneForVehicle(type);
        if (string.IsNullOrEmpty(simulator))
        {
            Debug.LogError("[TrainingStartButton] No simulator scene mapped for " + type);
            return;
        }

        SceneManager.LoadScene(simulator, LoadSceneMode.Single);
    }

    private static string SimulatorSceneForVehicle(VehicleType type)
    {
        switch (type)
        {
            case VehicleType.Segway: return "SegwaySimulator";
            case VehicleType.EScooter: return "EScooterSimulator";
            case VehicleType.Unicycle: return "UnicycleSimulator";
            case VehicleType.Skateboard: return "SkateboardSimulator";
        }
        return null;
    }
}
