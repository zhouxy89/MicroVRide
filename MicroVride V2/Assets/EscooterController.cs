using UnityEngine;
using UnityEngine.SceneManagement;
using VK.BikeLab.Segway;
using TMPro;
using Study;

public class EscooterController : MonoBehaviour
{
    [Header("References")]
    public Segway segway;
    public VehicleSelectionManager vehicleSelectionManager;
    private VehicleDataReceiver dataReceiver;

    [Header("Top-Level Caps")]
    public float maxSpeed = 12f;
    public float maxTurnAngle = 30f;

    [Header("Input Filtering")]
    [Tooltip("Ignore tiny throttle values.")]
    public float throttleDeadzone = 0.08f;
    [Tooltip("Ignore tiny steering angle (deg).")]
    public float yawDeadzoneDeg = 3f;

    public enum SteerAxis { Yaw, Roll, Pitch }
    [Tooltip("Which IMU axis controls turning. Set to Yaw.")]
    public SteerAxis steerAxis = SteerAxis.Yaw;
    [Tooltip("Subtract this zero from the raw axis (deg). Use auto or call CalibrateSteerZero().")]
    public float steerZeroDeg = 0f;
    [Tooltip("Invert steering sign if turning feels backwards.")]
    public bool invertSteer = false;
    [Tooltip("Capture current axis as zero on Start().")]
    public bool autoZeroOnStart = true;

    [Header("Sensitivity (Yaw)")]
    [Tooltip("How many degrees equals 'full turn' (-1..1). Higher = less sensitive.")]
    public float yawSensitivityDeg = 60f;

    [Header("Expo Curves (0=linear, 1=strong expo)")]
    [Range(0f, 1f)] public float throttleExpo = 0.35f;
    [Range(0f, 1f)] public float yawExpo = 0.4f;

    [Header("Additional Smoothing")]
    public float speedFilter = 6f;
    public float turnFilter = 8f;

    [Header("Slew-Rate Limits")]
    public float speedSlewPerSec = 6f;
    public float turnSlewPerSec = 120f;

    [Header("Speed-Scaled Steering")]
    [Range(0.2f, 1f)] public float minSteerScaleAtHighSpeed = 0.45f;
    public float steerTightenStartSpeed = 4f;
    public float steerTightenEndSpeed = 12f;

    [Header("Legacy Blend")]
    public float smoothing = 5f;

    [Header("Turn Gain")]
    [Tooltip("Multiply final steering command. >1 = bigger turns, <1 = smaller.")]
    public float turnGain = 1.5f;

    [Header("Throttle Gating / Anti-Creep")]
    [Tooltip("If true, scooter will not move unless throttle > deadzone.")]
    public bool requireThrottleToMove = true;
    [Tooltip("Braking decel (m/s^2) to bring speed to zero when throttle is off.")]
    public float idleBrakePerSec = 3f;
    [Tooltip("Minimum speed (m/s) required to apply lean. Prevents 'lean propelling' when stopped.")]
    public float minSpeedForLean = 0.8f;
    [Tooltip("Allow turning command when stationary (visual lean only). If false, lean is zeroed near stop.")]
    public bool allowTurnWhenStationary = false;

    // ---------------- NEW: collision helpers ----------------
    [Header("Collision guards (auto-resume)")]
    [Tooltip("Layers considered solid (sidewalks/walls).")]
    public LayerMask environmentLayers = ~0;     // include your sidewalks/walls layers
    [Tooltip("Meters ahead to block forward speed (front bumper).")]
    public float bumperDistance = 0.5f;
    [Tooltip("Small time (s) to suppress forward drive into the obstacle normal after a collision.")]
    public float recoverDuration = 0.4f;

    [Header("Debug / Editor Overrides")]
    [Tooltip("If true, ignore throttle and use Debug Speed instead.")]
    public bool useDebugSpeed = false;

    [Tooltip("Speed (m/s) used when UseDebugSpeed = true.")]
    public float debugSpeed = 0f;  // adjustable live in Inspector


    private float recoverUntil = -1f;
    private Vector3 lastHitNormal = Vector3.zero;
    private int coinsLayer = -1;
    // ---------------------------------------------------------

    private bool controllerEnabled = false;
    private float cmdSpeed;     // filtered/slewed command to segway
    private float cmdTurn;      // filtered/slewed command to segway

    public TMP_Text debugText; // optional

    void Start()
    {
        dataReceiver = VehicleDataReceiver.Instance;
        if (dataReceiver == null)
        {
            Debug.LogError("[EscooterController] No VehicleDataReceiver found!");
            return;
        }

        if (autoZeroOnStart) CalibrateSteerZero();

        if (SceneManager.GetActiveScene().name != "Start")
            EnableControl(true);

        coinsLayer = LayerMask.NameToLayer("Coins"); // coins are on a LAYER, not tag
    }

    public void EnableControl(bool enable) => controllerEnabled = enable;

    /// <summary>Capture the current IMU steering axis as zero.</summary>
    public void CalibrateSteerZero()
    {
        if (dataReceiver == null) return;
        float rawAxis = ReadSteerAxisDeg();
        steerZeroDeg = rawAxis;
        Debug.Log($"[EscooterController] Calibrated steer zero = {steerZeroDeg:F1} deg (axis={steerAxis})");
    }

    private void Update()
    {
        if (!controllerEnabled || segway == null || dataReceiver == null) return;

        // -------- 1) Read raw inputs
        float rawThrottle = Mathf.Clamp01(dataReceiver.throttle);

        // Read selected steering axis and apply zero/invert
        float rawAxisDeg = ReadSteerAxisDeg();
        float rawYawDeg = (rawAxisDeg - steerZeroDeg) * (invertSteer ? -1f : 1f);

        // -------- 2) Apply deadzones
        bool throttleActive = rawThrottle >= throttleDeadzone;
        float th = throttleActive ? Remap01(rawThrottle, throttleDeadzone, 1f) : 0f;
        float yawDeg = ApplyDeadzoneSigned(rawYawDeg, yawDeadzoneDeg);

        // -------- 3) Normalize yaw by sensitivity (bigger sensitivity = less reactive)
        float yawNorm = Mathf.Clamp(yawDeg / Mathf.Max(1e-3f, yawSensitivityDeg), -1f, 1f);

        // -------- 4) Expo curves
        th = ApplyExpo01(th, throttleExpo);        // 0..1
        yawNorm = ApplyExpoSigned(yawNorm, yawExpo);    // -1..1

        // -------- 5) Targets (pre-filter)
        //float targetVelocity = th * maxSpeed;
        float targetVelocity;
        if (useDebugSpeed)
        {
            targetVelocity = Mathf.Clamp(debugSpeed, -maxSpeed, maxSpeed);
        }
        else
        {
            targetVelocity = th * maxSpeed;
        }


        // speed-scaled steering (reduce turn as speed rises)
        float speedNow = Mathf.Abs(segway.getVelosity());
        float steerScale = 1f - (1f - minSteerScaleAtHighSpeed) *
                           Mathf.Clamp01(Mathf.InverseLerp(steerTightenStartSpeed, steerTightenEndSpeed, speedNow));

        // If nearly stopped and not allowing turn-in-place, zero the turn to avoid lean propelling the rig
        bool canApplyLean = allowTurnWhenStationary || (speedNow >= minSpeedForLean) || throttleActive;
        float targetTurn = canApplyLean ? (-yawNorm) * maxTurnAngle * steerScale * turnGain : 0f;

        // ---------------- Guards against continuous push into walls ----------------

        // A) Front bumper: raycast ahead, block forward speed if something is directly in front
        bool bumperBlock = Physics.Raycast(
            segway.transform.position + Vector3.up * 0.2f,
            segway.transform.forward,
            out RaycastHit hit,
            bumperDistance,
            environmentLayers,
            QueryTriggerInteraction.Ignore
        ) && hit.collider.gameObject.layer != coinsLayer;

        if (bumperBlock)
            targetVelocity = 0f;

        // B) Short recovery window after collision: if still facing into last obstacle normal, block forward speed
        if (Time.time < recoverUntil && lastHitNormal != Vector3.zero)
        {
            // if forward is pointing into obstacle (dot > 0), clamp targetVelocity
            if (Vector3.Dot(segway.transform.forward, lastHitNormal) > 0f)
                targetVelocity = 0f;
        }

        // --------------------------------------------------------------------------

        // -------- 6) Filters
        // -------- 6) Filters
        if (useDebugSpeed)
        {
            // Ignore throttle gating completely when debug override is active
            cmdSpeed = Mathf.Lerp(cmdSpeed, targetVelocity, Time.deltaTime * speedFilter);
            cmdSpeed = MoveTowardsPerSec(cmdSpeed, targetVelocity, speedSlewPerSec, Time.deltaTime);
        }
        else if (!throttleActive && requireThrottleToMove)
        {
            cmdSpeed = 0f; // command zero
        }
        else
        {
            cmdSpeed = Mathf.Lerp(cmdSpeed, targetVelocity, Time.deltaTime * speedFilter);
            cmdSpeed = MoveTowardsPerSec(cmdSpeed, targetVelocity, speedSlewPerSec, Time.deltaTime);
        }


        cmdTurn = Mathf.Lerp(cmdTurn, targetTurn, Time.deltaTime * turnFilter);
        cmdTurn = MoveTowardsPerSec(cmdTurn, targetTurn, turnSlewPerSec, Time.deltaTime);

        if (StudyLogger.Instance != null)
        {
            StudyLogger.Instance.LogTelemetry(
                Time.deltaTime,
                segway.transform,
                dataReceiver,
                targetVelocity,
                targetTurn
            );
        }

        // -------- 7) Output to Segway (legacy blend + anti-creep)
        float outVelocity;
        if (useDebugSpeed)
        {
            // Always honor debugSpeed regardless of throttle gating
            outVelocity = Mathf.Lerp(segway.getVelosity(), cmdSpeed, Time.deltaTime * smoothing);
        }
        else
        {
            outVelocity = (!throttleActive && requireThrottleToMove)
                ? Mathf.MoveTowards(segway.getVelosity(), 0f, idleBrakePerSec * Time.deltaTime)
                : Mathf.Lerp(segway.getVelosity(), cmdSpeed, Time.deltaTime * smoothing);
        }

        float outTurn = Mathf.Lerp(segway.targetIncline, cmdTurn, Time.deltaTime * smoothing);

        segway.setVelocity(outVelocity);
        segway.setSideIncline(outTurn);

        // -------- 8) Debug HUD
        if (debugText != null)
        {
            debugText.text =
                $"thr:{rawThrottle:F2} th:{th:F2}  axis:{steerAxis} raw:{rawAxisDeg:F1}° -> yaw:{rawYawDeg:F1}° yn:{yawNorm:F2}\n" +
                $"spdNow:{speedNow:F2}  outV:{outVelocity:F2}  turn:{outTurn:F1}°";
        }
    }

    // ------- Collision handling: upright + short recovery + logging -------
    void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.gameObject.layer == coinsLayer) return; // ignore coins layer

        segway.TriggerCollisionBlackout();

        segway.getup(0f, 0f);
        var rb = segway.getRigidbody();
        if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        // remember normal & start recovery window
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
        if (other.gameObject.layer == coinsLayer) return; // ignore coins layer

        segway.TriggerCollisionBlackout();

        segway.getup(0f, 0f);
        var rb = segway.getRigidbody();
        if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        // approximate normal from closest point
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

    // ------- helpers -------
    private float ReadSteerAxisDeg()
    {
        switch (steerAxis)
        {
            case SteerAxis.Yaw: return dataReceiver.imuYaw;
            case SteerAxis.Roll: return dataReceiver.imuRoll;
            case SteerAxis.Pitch: return dataReceiver.imuPitch;
            default: return dataReceiver.imuYaw;
        }
    }

    private static float Remap01(float x, float dead, float max)
    {
        float t = Mathf.InverseLerp(dead, max, x);
        return Mathf.Clamp01(t);
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
