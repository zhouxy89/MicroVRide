using UnityEngine;
using VK.BikeLab.Segway;

public class UnicycleController : MonoBehaviour
{
    [Header("References")]
    public Segway segway;
    public VehicleSelectionManager vehicleSelectionManager;
    public VehicleDataReceiver dataReceiver; // IMU pitch/roll

    [Header("Control Settings")]
    public float maxSpeed = 20f;
    public float maxTurnAngle = 45f;
    public float smoothing = 6f;

    private bool controllerEnabled = false;
    private float targetVelocity;
    private float targetTurn;

    public void EnableControl(bool enable)
    {
        controllerEnabled = enable;
        
    }

    private void Update()
    {
        if (!controllerEnabled || segway == null || dataReceiver == null) return;

        float pitch = Mathf.Clamp(dataReceiver.imuPitch / 45f, -1f, 1f);
        targetVelocity = pitch * maxSpeed;

        float roll = Mathf.Clamp(dataReceiver.imuRoll / 45f, -1f, 1f);
        targetTurn = roll * maxTurnAngle;

        float velocity = Mathf.Lerp(segway.getVelosity(), targetVelocity, Time.deltaTime * smoothing);
        float incline = Mathf.Lerp(segway.targetIncline, targetTurn, Time.deltaTime * smoothing);

        segway.setVelocity(velocity);
        segway.setSideIncline(incline);
    }
}
