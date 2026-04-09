using UnityEngine;

public class SimulatorSceneBootstrap : MonoBehaviour
{
    private void Start()
    {
        switch (SessionState.SelectedVehicle)
        {
            case VehicleType.EScooter:
                var escooter = FindObjectOfType<EscooterController>();
                if (escooter != null) escooter.EnableControl(true);
                break;

            case VehicleType.Segway:
                var segway = FindObjectOfType<SegwayController>();
                if (segway != null) segway.EnableControl(true);
                break;

            case VehicleType.Unicycle:
                var unicycle = FindObjectOfType<UnicycleController>();
                if (unicycle != null) unicycle.EnableControl(true);
                break;

            case VehicleType.Skateboard:
                var board = FindObjectOfType<OneWheelController>();
                if (board != null) board.EnableControl(true);
                break;
        }
    }
}
