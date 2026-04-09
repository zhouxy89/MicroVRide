using UnityEngine;
using UnityEngine.SceneManagement;
using VK.BikeLab.Segway;
using TMPro;
using Study;

public class OneWheelSkateboardController : MonoBehaviour
{
    [Header("References")]
    public Segway segway;
    public VehicleSelectionManager vehicleSelectionManager;
    private VehicleDataReceiver dataReceiver;

    // -------- IMU → Speed (lean forward/back) --------
    public Axis speedAxis = Axis.Pitch;           // pitch to go forward/back
    [Tooltip("Ignore tiny lean (deg).")]
    public float speedDeadzoneDeg = 2.0f;
    [Tooltip("Zero offset for speed axis (deg). Use CalibrateSpeedZero().")]
    public float speedZeroDeg = 0f;
    [Tooltip("Invert speed if forward/back feels reversed.")]
    public bool invertSpeed = false;
    [Tooltip("How many degrees equals full speed (→1.0). Higher = less sensitive.")]
    public float speedSensitivityDeg = 18f;
    [Range(0f, 1f)] public float speedExpo = 0.35f;

    // -------- IMU → Turn (carve) --------
    public Axis steerAxis = Axis.Yaw;             // yaw by default; set Roll for carve-by-lean
    [Tooltip("Ignore tiny steering angle (deg).")]
    public float yawDeadzoneDeg = 3f;
    [Tooltip("Zero offset for steer axis (deg). Use CalibrateSteerZero().")]
    public float steerZeroDeg = 0f;
    [Tooltip("Invert steering if turns feel backwards.")]
    public bool invertSteer = false;
    [Tooltip("How many degrees equals full turn (-1..1). Higher = less sensitive.")]
    public float yawSensitivityDeg = 55f;
    [Range(0f, 1f)] public float yawExpo = 0.35f;

    // -------- Caps / shaping --------
    [Header("Caps")]
    [Tooltip("Max forward speed (m/s).")]
    public float maxSpeed = 12f;
    [Tooltip("Max turn command (deg).")]
    public float maxTurnAngle = 28f;

    [Header("Additional Smoothing")]
    public float speedFilter = 6f;
    public float turnFilter = 8f;

    [Header("Slew-Rate Limits")]
    public float speedSlewPerSec = 6f;    // m/s^2
    public float turnSlewPerSec = 120f;  // deg/s

    [Header("Speed-Scaled Steering")]
    [Range(0.2f, 1f)] public float minSteerScaleAtHighSpeed = 0.55f;
    public float steerTightenStartSpeed = 4.0f;
    public float steerTightenEndSpeed = 11.0f;

    [Header("Legacy Blend")]
    public float smoothing = 5f;

    [Header("Neutral / Anti-creep")]
    [Tooltip("Braking decel (m/s^2) to bring speed to zero when lean is near neutral.")]
    public float idleBrakePerSec = 3f;
    [Tooltip("Minimum speed (m/s) required to apply lean. Prevents lean from propelling at rest.")]
    public float minSpeedForLean = 0.7f;
    public bool allowTurnWhenStationary = false;


    // -------- Debug speed override --------
    [Header("Debug / Editor Overrides")]
    [Tooltip("If true, ignore IMU pitch for speed and use Debug Speed below.")]
    public bool useDebugSpeed = false;
    [Tooltip("Speed (m/s) when debug override is active.")]
    public float debugSpeed = 3.0f;

    // -------- Collision guards & coins --------
    [Header("Collision guards (auto-resume)")]
    [Tooltip("LAYERS to treat as environment (curbs/walls). Leave 0 to disable bumper.")]
    public LayerMask environmentLayers = 0;   // 0 disables bumper
    [Tooltip("Meters ahead to block forward speed (front bumper).")]
    public float bumperDistance = 0.5f;
    [Tooltip("Small time (s) to suppress forward drive into obstacle normal after a collision.")]
    public float recoverDuration = 0.35f;

    [Header("Coins")]
    [Tooltip("Layers that contain coins/pickups (ignored by collision stop and bumper).")]
    //public LayerMask coinLayers;              // set to your “Coins” layer


    // --- Asymmetric speed mapping ---
    [Header("Asymmetric Speed Mapping")]
    
    public float forwardGain = 1.2f;
    [Tooltip("Multiply backward speed output ( < 1 to soften reverse ).")]
    public float backwardGain = 0.6f;

    [Range(0f, 1f)] public float forwardExpo = 0.15f;   // less expo = punchier around center
    [Range(0f, 1f)] public float backwardExpo = 0.35f;  // more expo = gentler reverse near center

    [Tooltip("Deadzone (deg) when leaning forward.")]
    public float speedDeadzoneDegForward = 1.0f;

    [Tooltip("Deadzone (deg) when leaning backward.")]
    public float speedDeadzoneDegBackward = 1.6f;

    // --- Direction-aware slew (rate limits) ---
    [Header("Directional Slew Rates (m/s²)")]
    public float slewAccelForward = 14f;  // punchier forward
    public float slewAccelBackward = 8f;  // softer reverse accel
    public float slewDecel = 18f;  // brisk slow-down both ways


    // -------- Internal state --------
    private bool controllerEnabled = false;
    private float cmdSpeed;                   // m/s (signed)
    private float cmdTurn;                    // deg (signed)
    private float recoverUntil = -1f;
    private Vector3 lastHitNormal = Vector3.zero;
    private float sceneStartTime;

    public TMP_Text debugText; // optional HUD
    private int coinsLayer = -1;

    public enum Axis { Yaw, Roll, Pitch }

    void Start()
    {
        dataReceiver = VehicleDataReceiver.Instance;
        if (dataReceiver == null)
        {
            Debug.LogError("[OneWheelSkateboardController] No VehicleDataReceiver found!");
            return;
        }

        if (SceneManager.GetActiveScene().name != "Start")
            EnableControl(true);

        sceneStartTime = Time.time;
        coinsLayer = LayerMask.NameToLayer("Coins");
    }

    public void EnableControl(bool enable) => controllerEnabled = enable;

    // --- Calibration helpers ---
    public void CalibrateAllZeros()
    {
        if (dataReceiver == null) return;
        speedZeroDeg = ReadAxisDeg(speedAxis);
        steerZeroDeg = ReadAxisDeg(steerAxis);
        Debug.Log($"[OneWheel] Zeros set: speedZero={speedZeroDeg:F1}°, steerZero={steerZeroDeg:F1}°");
    }
    public void CalibrateSpeedZero()
    {
        if (dataReceiver == null) return;
        speedZeroDeg = ReadAxisDeg(speedAxis);
        Debug.Log($"[OneWheel] Speed zero = {speedZeroDeg:F1}° ({speedAxis})");
    }
    public void CalibrateSteerZero()
    {
        if (dataReceiver == null) return;
        steerZeroDeg = ReadAxisDeg(steerAxis);
        Debug.Log($"[OneWheel] Steer zero = {steerZeroDeg:F1}° ({steerAxis})");
    }

    void Update()
    {
        if (!controllerEnabled || segway == null || dataReceiver == null) return;

        // -------- IMU → speed (pitch lean), asymmetric --------
        float rawSpeedDeg = ReadAxisDeg(speedAxis) - speedZeroDeg;
        if (invertSpeed) rawSpeedDeg = -rawSpeedDeg;

        float dz = (rawSpeedDeg >= 0f) ? speedDeadzoneDegForward : speedDeadzoneDegBackward;
        float speedDeg = ApplyDeadzoneSigned(rawSpeedDeg, dz);

        // Normalize by sensitivity (same sensitivity both ways; keep this simple)
        float speedNorm = Mathf.Clamp(speedDeg / Mathf.Max(1e-3f, speedSensitivityDeg), -1f, 1f);

        // Asymmetric expo
        float expo = (speedNorm >= 0f) ? forwardExpo : backwardExpo;
        speedNorm = ApplyExpoSigned(speedNorm, expo);

        // Asymmetric gain
        float gain = (speedNorm >= 0f) ? forwardGain : backwardGain;
        speedNorm = Mathf.Clamp(speedNorm * gain, -1f, 1f);

        float targetVelocity = speedNorm * maxSpeed;


        // --- DEBUG SPEED OVERRIDE ---
        if (useDebugSpeed)
            targetVelocity = Mathf.Clamp(debugSpeed, -maxSpeed, maxSpeed);

        // -------- IMU → steer (yaw or roll) --------
        float rawSteerDeg = ReadAxisDeg(steerAxis) - steerZeroDeg;
        if (invertSteer) rawSteerDeg = -rawSteerDeg;

        float steerDeg = ApplyDeadzoneSigned(rawSteerDeg, yawDeadzoneDeg);
        float steerNorm = Mathf.Clamp(steerDeg / Mathf.Max(1e-3f, yawSensitivityDeg), -1f, 1f);
        steerNorm = ApplyExpoSigned(steerNorm, yawExpo);               // -1..1

        float speedNow = Mathf.Abs(segway.getVelosity());
        float steerScale = 1f - (1f - minSteerScaleAtHighSpeed) *
                           Mathf.Clamp01(Mathf.InverseLerp(steerTightenStartSpeed, steerTightenEndSpeed, speedNow));

        bool canApplyLean = allowTurnWhenStationary || (speedNow >= minSpeedForLean) || (Mathf.Abs(targetVelocity) > 0.01f);
        float targetTurn = canApplyLean ? (-steerNorm) * maxTurnAngle * steerScale : 0f;

        // -------- Bumper guard (optional) --------
        bool bumperBlock = Physics.Raycast(
            segway.transform.position + Vector3.up * 0.2f,
            segway.transform.forward,
            out RaycastHit hit,
            bumperDistance,
            environmentLayers,
            QueryTriggerInteraction.Ignore
        ) && hit.collider.gameObject.layer != coinsLayer;
        if (!useDebugSpeed)
        {
            if (bumperBlock)
                targetVelocity = 0f;

            // After collision: don’t push back into wall
            if (Time.time < recoverUntil && lastHitNormal != Vector3.zero)
                if (Vector3.Dot(segway.transform.forward, lastHitNormal) > 0f && targetVelocity > 0f)
                    targetVelocity = 0f;
        }

        // -------- Filters / slew --------
        cmdSpeed = Mathf.Lerp(cmdSpeed, targetVelocity, Time.deltaTime * speedFilter);

        // Decide which slew to use this frame
        float dv = targetVelocity - cmdSpeed;
        float slew;

        if (Mathf.Abs(targetVelocity) < Mathf.Abs(cmdSpeed))
        {
            // decelerating toward target
            slew = slewDecel;
        }
        else
        {
            // accelerating toward target
            slew = (targetVelocity >= 0f) ? slewAccelForward : slewAccelBackward;
        }

        cmdSpeed = MoveTowardsPerSec(cmdSpeed, targetVelocity, slew, Time.deltaTime);


        cmdTurn = Mathf.Lerp(cmdTurn, targetTurn, Time.deltaTime * turnFilter);
        cmdTurn = MoveTowardsPerSec(cmdTurn, targetTurn, turnSlewPerSec, Time.deltaTime);

        // -------- Telemetry --------
        if (StudyLogger.Instance != null)
            StudyLogger.Instance.LogTelemetry(Time.deltaTime, segway.transform, dataReceiver, targetVelocity, targetTurn);

        // -------- Output to Segway --------
        // -------- Output to Segway --------
        float outVelocity;
        if (useDebugSpeed)
        {
            // honor debug speed; do NOT idle-brake on neutral lean
            outVelocity = Mathf.Lerp(segway.getVelosity(), cmdSpeed, Time.deltaTime * smoothing);
        }
        else
        {
            // normal anti-creep behavior using IMU lean
            outVelocity = (Mathf.Abs(speedNorm) < 1e-3f)
                ? Mathf.MoveTowards(segway.getVelosity(), 0f, idleBrakePerSec * Time.deltaTime)
                : Mathf.Lerp(segway.getVelosity(), cmdSpeed, Time.deltaTime * smoothing);
        }


        float outTurn = Mathf.Lerp(segway.targetIncline, cmdTurn, Time.deltaTime * smoothing);

        segway.setVelocity(outVelocity);
        segway.setSideIncline(outTurn);

        // -------- HUD --------
        if (debugText != null)
        {
            debugText.text =
                $"IMU speedAxis:{speedAxis} raw:{rawSpeedDeg:F1}° n:{speedNorm:F2}  " +
                $"steerAxis:{steerAxis} raw:{rawSteerDeg:F1}° n:{steerNorm:F2}\n" +
                $"spdNow:{speedNow:F2}  outV:{outVelocity:F2}  outT:{outTurn:F1}°";
        }
    }

    // -------- Collision: upright + short recovery + log --------
    void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.gameObject.layer == coinsLayer) return;

        segway.TriggerCollisionBlackout();

        segway.getup(0f, 0f);
        var rb = segway.getRigidbody();
        if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        if (collision.contacts != null && collision.contacts.Length > 0)
            lastHitNormal = collision.contacts[0].normal;
        else
            lastHitNormal = -segway.transform.forward;
        recoverUntil = Time.time + recoverDuration;

        if (StudyLogger.Instance != null)
        {
            string layerName = LayerMask.LayerToName(collision.collider.gameObject.layer);
            Vector3 point = (collision.contacts != null && collision.contacts.Length > 0)
                ? collision.contacts[0].point
                : transform.position;
            StudyLogger.Instance.LogCollision(collision.collider.tag, layerName, point, collision.relativeVelocity);
        }
    }

    void OnTriggerEnter(Collider other)
    {
       
        if (other.gameObject.layer == LayerMask.NameToLayer("Coins")) return;

        segway.TriggerCollisionBlackout();

        segway.getup(0f, 0f);
        var rb = segway.getRigidbody();
        if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        Vector3 cp = other.ClosestPoint(segway.transform.position);
        Vector3 approxNormal = (segway.transform.position - cp);
        lastHitNormal = approxNormal.sqrMagnitude > 1e-4f ? approxNormal.normalized : -segway.transform.forward;
        recoverUntil = Time.time + recoverDuration;

        if (StudyLogger.Instance != null)
        {
            string layerName = LayerMask.LayerToName(other.gameObject.layer);
            StudyLogger.Instance.LogCollision(other.tag, layerName, segway.transform.position, Vector3.zero);
        }
    }

    // -------- helpers --------
    private float ReadAxisDeg(Axis axis)
    {
        switch (axis)
        {
            case Axis.Yaw: return dataReceiver.imuYaw;
            case Axis.Roll: return dataReceiver.imuRoll;
            case Axis.Pitch: return dataReceiver.imuPitch;
            default: return dataReceiver.imuYaw;
        }
    }

    private static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    private static float ApplyDeadzoneSigned(float v, float deadzone)
    {
        if (Mathf.Abs(v) <= deadzone) return 0f;
        return Mathf.Sign(v) * (Mathf.Abs(v) - deadzone);
    }

    private static float ApplyExpo01(float x, float expo)
    {
        float curved = x * x;
        return Mathf.Lerp(x, curved, Mathf.Clamp01(expo));
    }

    private static float ApplyExpoSigned(float x, float expo)
    {
        float a = Mathf.Abs(x);
        float curved = a * a;
        float outMag = Mathf.Lerp(a, curved, Mathf.Clamp01(expo));
        return Mathf.Sign(x) * outMag;
    }

    private static float MoveTowardsPerSec(float current, float target, float ratePerSec, float dt)
    {
        float maxDelta = Mathf.Max(0f, ratePerSec) * dt;
        return Mathf.MoveTowards(current, target, maxDelta);
    }
}
