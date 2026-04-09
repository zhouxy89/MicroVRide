using UnityEngine;

public enum VehicleType { Segway, EScooter, Unicycle, Skateboard }
public enum SkateboardStance { Regular, Goofy }

public static class SessionState
{
    public static bool CalibrationComplete { get; set; } = false;
    public static VehicleType SelectedVehicle { get; set; } = VehicleType.EScooter;
    public static SkateboardStance SelectedSkateboardStance { get; set; } = SkateboardStance.Regular;

    // NEW: one-time spawn pose computed at selection time
    public static bool HasSpawnPose { get; set; } = false;
    public static Vector3 SpawnPosition { get; set; }
    public static float SpawnYawDeg { get; set; }
}
