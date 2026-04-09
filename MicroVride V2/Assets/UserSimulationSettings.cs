// UserSimulationSettings.cs
// Shared settings used between VehicleSelectionScene and VehicleSimulationScene

public static class UserSimulationSettings
{
    public enum VehicleType { Segway, EScooter, Unicycle, OneWheel }
    public enum Stance { Regular, Goofy }

    public static VehicleType SelectedVehicle = VehicleType.Segway;
    public static Stance SelectedStance = Stance.Regular;
    public static bool CalibrationComplete = false;
}
