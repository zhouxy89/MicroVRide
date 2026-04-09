using UnityEngine;
using UnityEngine.SceneManagement;
using VK.BikeLab.Segway;
using TMPro;
using Study;

public class ElectricUnicycleController : MonoBehaviour
{
    [Header("References")]
    public Segway segway;
    public VehicleSelectionManager vehicleSelectionManager;
    private VehicleDataReceiver dataReceiver;

    [Header("Speed from IMU (Pitch by default)")]
    public Axis speedAxis = Axis.Pitch;                // Pitch → forward/back
    [Tooltip("Ignore tiny lean (deg).")]
    public float speedDeadzoneDeg = 2.0f;
    [Tooltip("Zero offset for speed axis (deg). Use auto or call CalibrateSpeedZero().")]
    public float speedZeroDeg = 0f;
    [Tooltip("Invert speed sign if forward/back feels reversed.")]
    public bool invertSpeed = false;
    [Tooltip("How many degrees equals full speed (→1.0). Higher = less sensitive.")]
    public float speedSensitivityDeg = 20f;
    [Range(0f, 1f)] public float speedExpo = 0.35f;

    [Header("Turn from IMU (Yaw by default)")]
    public Axis steerAxis = Axis.Yaw;                  // Yaw → turn
    [Tooltip("Ignore tiny steering angle (deg).")]
    public float yawDeadzoneDeg = 3f;
    [Tooltip("Zero offset for steer axis (deg).")]
    public float steerZeroDeg = 0f;
    [Tooltip("Invert steering sign if turns feel backwards.")]
    public bool invertSteer = false;
    [Tooltip("How many degrees equals full turn (-1..1). Higher = less sensitive.")]
    public float yawSensitivityDeg = 55f;
    [Range(0f, 1f)] public float yawExpo = 0.35f;

    [Header("Caps")]
    [Tooltip("Max forward speed (m/s).")]
    public float maxSpeed = 10f;
    [Tooltip("Max turn command (deg).")]
    public float maxTurnAngle = 25f;

    [Header("Additional Smoothing")]
    [Tooltip("Extra smoothing for speed command.")]
    public float speedFilter = 6f;
    [Tooltip("Extra smoothing for steering command.")]
    public float turnFilter = 8f;

    [Header("Slew-Rate Limits")]
    [Tooltip("Max change of speed command per second (m/s^2).")]
    public float speedSlewPerSec = 6f;
    [Tooltip("Max change of turn command per second (deg/s).")]
    public float turnSlewPerSec = 120f;

    [Header("Speed-Scaled Steering")]
    [Range(0.2f, 1f)] public float minSteerScaleAtHighSpeed = 0.55f;
    [Tooltip("Speed (m/s) where steering begins to tighten.")]
    public float steerTightenStartSpeed = 3.5f;
    [Tooltip("Speed (m/s) where steering is most reduced.")]
    public float steerTightenEndSpeed = 9.0f;

    [Header("Legacy Blend")]
    [Tooltip("Blending to segway internal states.")]
    public float smoothing = 5f;

    [Header("Anti-Creep / Neutral")]
    [Tooltip("Braking decel (m/s^2) to bring speed to zero when lean is near neutral.")]
    public float idleBrakePerSec = 3f;
    [Tooltip("Minimum speed (m/s) required to apply lean. Prevents 'lean propelling' at rest.")]
    public float minSpeedForLean = 0.7f;
    [Tooltip("Allow turning command when stationary (visual lean only).")]
    public bool allowTurnWhenStationary = false;

    [Header("Collision guards (auto-resume)")]
    [Tooltip("LAYERS to treat as environment (curbs/walls). Leave empty to disable bumper.")]
    public LayerMask environmentLayers = 0;    // 0 disables bumper
    [Tooltip("Meters ahead to block forward speed (front bumper).")]
    public float bumperDistance = 0.5f;
    [Tooltip("Small time (s) to suppress forward drive into the obstacle normal after a collision.")]
    public float recoverDuration = 0.35f;

    [Header("Coins")]
    [Tooltip("Layers that contain coins/pickups (ignored by collision stop and bumper).")]
    //public LayerMask coinLayers;               // set to “Coins” layer in Inspector
    private int coinLayers = -1;
    // ---- internal state
    private bool controllerEnabled = false;
    private float cmdSpeed;     // filtered/slewed command to segway (m/s, signed)
    private float cmdTurn;      // filtered/slewed command to segway (deg, signed)

    private float recoverUntil = -1f;
    private Vector3 lastHitNormal = Vector3.zero;
    private float sceneStartTime;

    // --- Direction flip boost timer ---
    private float _flipBoostUntil = -1f;


    public TMP_Text debugText; // optional

    public enum Axis { Yaw, Roll, Pitch }

    // -------- Debug speed override --------
    [Header("Debug / Editor Overrides")]
    [Tooltip("If true, ignore IMU pitch for speed and use Debug Speed below.")]
    public bool useDebugSpeed = false;
    [Tooltip("Speed (m/s) when debug override is active.")]
    public float debugSpeed = 3.0f;

    [Header("Speed gain")]
    [Tooltip("Multiply forward speed ( > 1 makes small forward lean feel stronger ).")]
    public float forwardGain = 1.25f;

    [Tooltip("Optional: multiply reverse speed ( < 1 to make reverse gentler ). Set to 1 to keep reverse unchanged.")]
    public float backwardGain = 1.0f;

    private float filteredSpeedDeg = 0f;
    private float filteredSteerDeg = 0f;
    [Header("IMU Filtering")]
    public float imuFilterStrength = 8f; // higher = smoother, slower

    void Start()
    {
        dataReceiver = VehicleDataReceiver.Instance;
        if (dataReceiver == null)
        {
            Debug.LogError("[ElectricUnicycleController] No VehicleDataReceiver found!");
            return;
        }

        // Auto-enable when not in Start scene
        if (SceneManager.GetActiveScene().name != "Start")
            EnableControl(true);

        sceneStartTime = Time.time;
        coinLayers = LayerMask.NameToLayer("Coins");
    }

    public void EnableControl(bool enable) => controllerEnabled = enable;

    /// <summary>Calibrate both speed and steer zeros using current IMU readings.</summary>
    public void CalibrateAllZeros()
    {
        if (dataReceiver == null) return;
        speedZeroDeg = ReadAxisDeg(speedAxis);
        steerZeroDeg = ReadAxisDeg(steerAxis);
        Debug.Log($"[ElectricUnicycleController] Zeroes set: speedZero={speedZeroDeg:F1}°, steerZero={steerZeroDeg:F1}°");
    }

    public void CalibrateSpeedZero()
    {
        if (dataReceiver == null) return;
        speedZeroDeg = ReadAxisDeg(speedAxis);
        Debug.Log($"[ElectricUnicycleController] Speed zero = {speedZeroDeg:F1}° ({speedAxis})");
    }

    public void CalibrateSteerZero()
    {
        if (dataReceiver == null) return;
        steerZeroDeg = ReadAxisDeg(steerAxis);
        Debug.Log($"[ElectricUnicycleController] Steer zero = {steerZeroDeg:F1}° ({steerAxis})");
    }

    private void Update()
    {
        if (!controllerEnabled || segway == null || dataReceiver == null) return;

        // -------- IMU → speed (from chosen axis, typically Pitch) --------
        float rawSpeedDeg = ReadAxisDeg(speedAxis);
        if (invertSpeed) rawSpeedDeg = -rawSpeedDeg;

        // smooth raw IMU signal
        filteredSpeedDeg = Mathf.Lerp(filteredSpeedDeg, rawSpeedDeg, Time.deltaTime * imuFilterStrength);

        // apply zero + deadzone AFTER filtering
        float speedDeg = ApplyDeadzoneSigned(filteredSpeedDeg - speedZeroDeg, speedDeadzoneDeg);

        float speedNorm = Mathf.Clamp(speedDeg / Mathf.Max(1e-3f, speedSensitivityDeg), -1f, 1f);
        speedNorm = ApplyExpoSigned(speedNorm, speedExpo);  // -1..1

        // ---- Directional gain: amplify forward lean, optionally soften reverse ----
        if (speedNorm >= 0f)
            speedNorm *= forwardGain;     // e.g., 1.25 makes forward feel punchier
        else
            speedNorm *= backwardGain;    // usually 1.0 (unchanged) or <1.0 to tame reverse

        // keep it bounded
        speedNorm = Mathf.Clamp(speedNorm, -1f, 1f);


        float targetVelocity = speedNorm * maxSpeed;        // m/s, signed

        // -------- IMU → steer (from chosen axis, typically Yaw) --------
        float rawSteerDeg = ReadAxisDeg(steerAxis);
        if (invertSteer) rawSteerDeg = -rawSteerDeg;

        // smooth raw IMU signal
        filteredSteerDeg = Mathf.Lerp(filteredSteerDeg, rawSteerDeg, Time.deltaTime * imuFilterStrength);

        // apply zero + deadzone AFTER filtering
        float steerDeg = ApplyDeadzoneSigned(filteredSteerDeg - steerZeroDeg, yawDeadzoneDeg);

        float steerNorm = Mathf.Clamp(steerDeg / Mathf.Max(1e-3f, yawSensitivityDeg), -1f, 1f);
        steerNorm = ApplyExpoSigned(steerNorm, yawExpo);    // -1..1

        float speedNow = Mathf.Abs(segway.getVelosity());
        float steerScale = 1f - (1f - minSteerScaleAtHighSpeed) *
                           Mathf.Clamp01(Mathf.InverseLerp(steerTightenStartSpeed, steerTightenEndSpeed, speedNow));

        bool canApplyLean = allowTurnWhenStationary || (speedNow >= minSpeedForLean) || (Mathf.Abs(targetVelocity) > 0.01f);
        float targetTurn = canApplyLean ? (-steerNorm) * maxTurnAngle * steerScale : 0f;


        // Auto recenter if very still
        if (Mathf.Abs(segway.getVelosity()) < 0.1f && Mathf.Abs(rawSpeedDeg) < 1.0f)
        {
            // Slowly drift zero back towards current IMU reading
            speedZeroDeg = Mathf.Lerp(speedZeroDeg, ReadAxisDeg(speedAxis), Time.deltaTime * 0.2f);
        }


        // --- DEBUG SPEED OVERRIDE ---
        if (useDebugSpeed)
            targetVelocity = Mathf.Clamp(debugSpeed, -maxSpeed, maxSpeed);

        // -------- Guards against pushing into walls --------
        bool bumperBlock = false;
        if (environmentLayers != 0 && Time.time - sceneStartTime > 1f)
        {
            Vector3 rayOrigin = segway.transform.position + Vector3.up * 0.25f;
            if (Physics.Raycast(rayOrigin, segway.transform.forward, out RaycastHit hit,
                                bumperDistance, environmentLayers, QueryTriggerInteraction.Ignore))
            {
                if (!IsInLayerMask(hit.collider.gameObject.layer, coinLayers) &&
                    !hit.collider.isTrigger &&
                    !hit.collider.transform.IsChildOf(segway.transform))
                {
                    bumperBlock = true;
                }
            }
        }
        
            if (bumperBlock)
                targetVelocity = 0f;

            // After collision: don’t push back into wall
            if (Time.time < recoverUntil && lastHitNormal != Vector3.zero)
                if (Vector3.Dot(segway.transform.forward, lastHitNormal) > 0f && targetVelocity > 0f)
                    targetVelocity = 0f;

        var rb = segway.getRigidbody();
        if (Time.time > recoverUntil && rb != null)
            rb.constraints = RigidbodyConstraints.None;

        // --- Fast launch & quick direction-flip helpers ---
        float vNow = segway.getVelosity();

        // 1) Give a tiny "kick" when leaving near-zero so it doesn't feel sticky
        if (Mathf.Abs(vNow) < 0.25f && Mathf.Abs(targetVelocity) > 0.01f)
        {
            float kick = 0.8f * Mathf.Sign(targetVelocity); // ~0.8 m/s
            targetVelocity = Mathf.Max(Mathf.Abs(targetVelocity), Mathf.Abs(kick)) * Mathf.Sign(targetVelocity);
        }

        // 2) When target direction flips, use a much bigger slew for 0.2s
        bool dirFlip = (Mathf.Sign(targetVelocity) != Mathf.Sign(vNow)) && Mathf.Abs(targetVelocity) > 0.01f;

        const float flipBoostWindow = 0.20f;     // seconds
        const float flipSlewPerSec = 35f;       // very snappy during flip

        // cache outside Update in your class:
        // private float _flipBoostUntil = -1f;

        if (dirFlip) _flipBoostUntil = Time.time + flipBoostWindow;

        // choose effective slew for this frame
        float effectiveSlew = (Time.time < _flipBoostUntil) ? flipSlewPerSec : speedSlewPerSec;


        // -------- Filters / slew --------
        // -------- Filters / slew --------
        cmdSpeed = Mathf.Lerp(cmdSpeed, targetVelocity, Time.deltaTime * speedFilter);
        cmdSpeed = MoveTowardsPerSec(cmdSpeed, targetVelocity, effectiveSlew, Time.deltaTime);


        cmdTurn = Mathf.Lerp(cmdTurn, targetTurn, Time.deltaTime * turnFilter);
        cmdTurn = MoveTowardsPerSec(cmdTurn, targetTurn, turnSlewPerSec, Time.deltaTime);

        // -------- Telemetry (commanded) --------
        if (StudyLogger.Instance != null)
        {
            StudyLogger.Instance.LogTelemetry(Time.deltaTime, segway.transform, dataReceiver, targetVelocity, targetTurn);
        }

        // -------- Output to Segway (legacy blend + anti-creep) --------
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

    // ================= Collision Handling =================
void OnCollisionEnter(Collision collision)
{
    // 🚫 Ignore coins
    if (collision.collider.gameObject.layer == coinLayers) return;
    if (collision.collider.isTrigger) return;

    segway.TriggerCollisionBlackout();

    var rb = segway.getRigidbody();
    if (rb != null)
    {
        // 1) Stop motion instantly
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 2) Freeze rotation so physics can't tip it over
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // Optional: also freeze Y movement if you don't want bouncing
        // rb.constraints |= RigidbodyConstraints.FreezePositionY;
    }

    // 3) Keep upright but preserve current heading (yaw)
    Vector3 euler = segway.transform.rotation.eulerAngles;
    segway.transform.rotation = Quaternion.Euler(0f, euler.y, 0f);

    // 4) Enter recovery window (no forward motion allowed)
    recoverUntil = Time.time + recoverDuration;

    // 5) Log if needed
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
    // 🚫 Ignore coins
    if (other.gameObject.layer == coinLayers) return;
    if (other.isTrigger) return;

    segway.TriggerCollisionBlackout();

    var rb = segway.getRigidbody();
    if (rb != null)
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        // rb.constraints |= RigidbodyConstraints.FreezePositionY;
    }

    // Force upright but keep heading
    Vector3 euler = segway.transform.rotation.eulerAngles;
    segway.transform.rotation = Quaternion.Euler(0f, euler.y, 0f);

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
