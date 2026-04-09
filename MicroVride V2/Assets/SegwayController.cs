using UnityEngine;
using UnityEngine.SceneManagement;
using VK.BikeLab.Segway;
using TMPro;
using Study;


public class SegwayController : MonoBehaviour
{
    [Header("References")]
    public Segway segway;
    public VehicleSelectionManager vehicleSelectionManager;
    private VehicleDataReceiver dataReceiver;

    [Tooltip("Preferred source for feet (normalized 0..1). If null, falls back to VehicleDataReceiver.footSensors.")]
    public FootSensorInput footInput;

    [Header("Top-Level Caps")]
    public float maxSpeed = 10f;       // Segway slower than scooter by default
    public float maxTurnAngle = 25f;

    [Header("Input Filtering (IMU & feet)")]
    [Tooltip("Ignore tiny IMU steering angles (deg).")]
    public float yawDeadzoneDeg = 3f;

    [Tooltip("Ignore tiny feet forward/back intent (0..1).")]
    public float foreAftDeadzone = 0.07f;

    [Tooltip("Extra margin before flipping direction to avoid chatter.")]
    public float foreAftHysteresis = 0.03f;

    public enum SteerAxis { Yaw, Roll, Pitch }

    [Tooltip("Which IMU axis controls turning. Set to Yaw for handlebars.")]
    public SteerAxis steerAxis = SteerAxis.Yaw;

    [Tooltip("Subtract this zero from the raw axis (deg). Use auto or call CalibrateSteerZero().")]
    public float steerZeroDeg = 0f;

    [Tooltip("Invert steering sign if turning feels backwards.")]
    public bool invertSteer = false;

    [Tooltip("Capture current axis as zero on Start().")]
    public bool autoZeroOnStart = true;

    [Header("Sensitivity / Expo")]
    [Tooltip("How many degrees equals 'full turn' (-1..1). Higher = less sensitive.")]
    public float yawSensitivityDeg = 55f;

    [Range(0f, 1f)] public float yawExpo = 0.35f;       // steering shaping
    [Range(0f, 1f)] public float foreAftExpo = 0.35f;   // speed shaping on feet

    [Header("Additional Smoothing")]
    public float speedFilter = 6f;
    public float turnFilter = 8f;

    [Header("Slew-Rate Limits")]
    public float speedSlewPerSec = 6f;
    public float turnSlewPerSec = 120f;

    [Header("Speed-Scaled Steering")]
    [Range(0.2f, 1f)] public float minSteerScaleAtHighSpeed = 0.55f;  // segway steadier at speed
    public float steerTightenStartSpeed = 3.5f;
    public float steerTightenEndSpeed = 9.0f;

    [Header("Legacy Blend")]
    public float smoothing = 5f;

    [Header("Turn Gain")]
    public float turnGain = 1.25f;

    [Header("Anti-Creep / Neutral")]
    [Tooltip("If true, Segway will not move unless |fore–aft| > deadzone.")]
    public bool requireIntentToMove = true;

    [Tooltip("Braking decel (m/s^2) to bring speed to zero when intent is neutral.")]
    public float idleBrakePerSec = 3f;

    [Tooltip("Minimum speed (m/s) required to apply lean. Prevents lean from propelling at rest.")]
    public float minSpeedForLean = 0.7f;

    public bool allowTurnWhenStationary = false;

    [Header("Collision guards (auto-resume)")]
    [Tooltip("Layers considered solid (sidewalks/walls).")]
    public LayerMask environmentLayers = ~0;

    [Tooltip("Meters ahead to block forward speed (front bumper).")]
    public float bumperDistance = 0.5f;

    [Tooltip("Small time (s) to suppress forward drive into the obstacle normal after a collision.")]
    public float recoverDuration = 0.4f;

    // --- Internal state ---
    private bool controllerEnabled = false;
    private float cmdSpeed;
    private float cmdTurn;
    private float recoverUntil = -1f;
    private Vector3 lastHitNormal = Vector3.zero;

    public TMP_Text debugText; // optional
    private int coinsLayer = -1;

    // HCI helpers for hysteresis
    private int lastDir = 0;   // -1 = backward, 0 = neutral, 1 = forward

    // --- Debug / Diagnostics ---
    [Header("Debug / Diagnostics")]
    [Tooltip("Write a brief status line to the Console/Logcat once per second.")]
    public bool logStatusToConsole = true;

    [Tooltip("How often to print the status line (seconds).")]
    public float statusLogInterval = 1f;

    private float _statusLogT = 0f;
    private bool _feetKeysLogged = false;

    // -------- Debug speed override --------
    [Header("Debug / Editor Overrides")]
    [Tooltip("If true, ignore IMU pitch for speed and use Debug Speed below.")]
    public bool useDebugSpeed = false;
    [Tooltip("Speed (m/s) when debug override is active.")]
    public float debugSpeed = 3.0f;

    [Header("Feet Gain")]
    [Tooltip("Multiply the mapped foot magnitude before converting to m/s.")]
    public float speedGain = 1.5f; // 1.0 = unchanged, >1 = stronger


    // ================= Lifecycle =================
    void Start()
    {
        dataReceiver = VehicleDataReceiver.Instance;
        if (autoZeroOnStart) CalibrateSteerZero();

        // Bind to persistent instance
        footInput = FootSensorInput.Instance;
        if (footInput == null)
            Debug.LogError("[SegwayController] No FootSensorInput.Instance found");

        if (SceneManager.GetActiveScene().name != "Start")
            EnableControl(true);

        if (footInput != null)
            Debug.Log($"[SegwayController] using FootSensorInput id={footInput.GetInstanceID()}");

        coinsLayer = LayerMask.NameToLayer("Coins");
    }

    public void EnableControl(bool enable) => controllerEnabled = enable;


    public void CalibrateSteerZero()
    {
        if (dataReceiver == null) return;
        steerZeroDeg = ReadSteerAxisDeg();
        Debug.Log($"[SegwayController] Steer zero = {steerZeroDeg:F1}° ({steerAxis})");
    }


    // ================= Update =================
    private void Update()
    {
        if (!controllerEnabled || segway == null || dataReceiver == null) return;

        // --- TURN (IMU) ---
        float rawAxisDeg = ReadSteerAxisDeg();
        float rawYawDeg = (rawAxisDeg - steerZeroDeg) * (invertSteer ? -1f : 1f);
        float yawDeg = ApplyDeadzoneSigned(rawYawDeg, yawDeadzoneDeg);
        float yawNorm = Mathf.Clamp(yawDeg / Mathf.Max(1e-3f, yawSensitivityDeg), -1f, 1f);
        yawNorm = ApplyExpoSigned(yawNorm, yawExpo);
        float targetTurn = Mathf.Clamp(-yawNorm * maxTurnAngle, -maxTurnAngle, maxTurnAngle);

        // --- SPEED (Feet) ---
        float lHeel, lToe, lMidL, lMidR, rHeel, rToe, rMidL, rMidR;
        ReadFeet(out lHeel, out lToe, out lMidL, out lMidR,
                 out rHeel, out rToe, out rMidL, out rMidR);

        float toesMax = Mathf.Max(lToe, rToe);
        float heelsMax = Mathf.Max(lHeel, rHeel);
        float intentRaw = toesMax - heelsMax;  // >0 forward, <0 back
        float intentMag = Mathf.Abs(intentRaw);

        int desiredDir = 0;
        if (intentMag > (foreAftDeadzone + foreAftHysteresis)) desiredDir = (intentRaw > 0f) ? 1 : -1;
        else if (intentMag < (foreAftDeadzone - foreAftHysteresis)) desiredDir = 0;
        else desiredDir = lastDir;

        lastDir = desiredDir;

        // Map magnitude with expo
        float mag = 0f;
        if (intentMag > foreAftDeadzone)
        {
            float t = Mathf.InverseLerp(foreAftDeadzone, 1f, intentMag);
            t = ApplyExpo01(t, foreAftExpo);
            mag = Mathf.Clamp01(t * speedGain);

        }

        float targetVelocity = (requireIntentToMove && desiredDir == 0)
            ? 0f
            : desiredDir * mag * maxSpeed;

        // --- Speed-scaled steering ---
        float speedNow = Mathf.Abs(segway.getVelosity());
        float steerScale = 1f - (1f - minSteerScaleAtHighSpeed) *
                           Mathf.Clamp01(Mathf.InverseLerp(steerTightenStartSpeed, steerTightenEndSpeed, speedNow));

        bool canApplyLean = allowTurnWhenStationary || (speedNow >= minSpeedForLean) || (Mathf.Abs(targetVelocity) > 0.01f);
        targetTurn = canApplyLean ? (-yawNorm) * maxTurnAngle * steerScale * turnGain : 0f;

        // --- DEBUG SPEED OVERRIDE ---
        if (useDebugSpeed)
            targetVelocity = Mathf.Clamp(debugSpeed, -maxSpeed, maxSpeed);

        // --- Collision guards ---
        bool bumperBlock = Physics.Raycast(
            segway.transform.position + Vector3.up * 0.2f,
            segway.transform.forward,
            out RaycastHit hit,
            bumperDistance,
            environmentLayers,
            QueryTriggerInteraction.Ignore
        ) && hit.collider.gameObject.layer != coinsLayer;

        if (bumperBlock && targetVelocity > 0f) targetVelocity = 0f;

        if (Time.time < recoverUntil && lastHitNormal != Vector3.zero)
        {
            if (Vector3.Dot(segway.transform.forward, lastHitNormal) > 0f && targetVelocity > 0f)
                targetVelocity = 0f;
        }

        // --- Filters / slew ---
        if (requireIntentToMove && desiredDir == 0)
            cmdSpeed = 0f;
        else
        {
            cmdSpeed = Mathf.Lerp(cmdSpeed, targetVelocity, Time.deltaTime * speedFilter);
            cmdSpeed = MoveTowardsPerSec(cmdSpeed, targetVelocity, speedSlewPerSec, Time.deltaTime);
        }

        cmdTurn = Mathf.Lerp(cmdTurn, targetTurn, Time.deltaTime * turnFilter);
        cmdTurn = MoveTowardsPerSec(cmdTurn, targetTurn, turnSlewPerSec, Time.deltaTime);

        // --- Telemetry ---
        if (StudyLogger.Instance != null)
            StudyLogger.Instance.LogTelemetry(Time.deltaTime, segway.transform, dataReceiver, targetVelocity, targetTurn);

        // --- Output to Segway ---

        float outVelocity;
        if (useDebugSpeed)
        {
            // honor debug speed; do NOT idle-brake on neutral lean
            outVelocity = Mathf.Lerp(segway.getVelosity(), cmdSpeed, Time.deltaTime * smoothing);
        }
        else
        {
            // normal anti-creep behavior using IMU lean
            outVelocity = (requireIntentToMove && desiredDir == 0)
            ? Mathf.MoveTowards(segway.getVelosity(), 0f, idleBrakePerSec * Time.deltaTime)
            : Mathf.Lerp(segway.getVelosity(), cmdSpeed, Time.deltaTime * smoothing);
        }
   

        float outTurn = Mathf.Lerp(segway.targetIncline, cmdTurn, Time.deltaTime * smoothing);

        segway.setVelocity(outVelocity);
        segway.setSideIncline(outTurn);

        // --- HUD ---
        float feetAge = (footInput != null && footInput.lastFeetUpdateTime > 0f)
            ? (Time.realtimeSinceStartup - footInput.lastFeetUpdateTime)
            : -1f;

        if (debugText != null)
        {
            debugText.text =
                $"src:FootInput age:{feetAge:0.00}s lToe:{lToe:F2} rToe:{rToe:F2} " +
                $"lHeel:{lHeel:F2} rHeel:{rHeel:F2}\n" +
                $"feet: toeMax {toesMax:F2} heelMax {heelsMax:F2} dir:{desiredDir} mag:{mag:F2}\n" +
                $"turnAxis:{steerAxis} yaw:{rawYawDeg:F1}° yn:{yawNorm:F2} spd:{speedNow:F2}\n" +
                $"outV:{outVelocity:F2} outT:{outTurn:F1}°";
        }

        // --- Console log every second ---
        _statusLogT += Time.deltaTime;
        if (logStatusToConsole && _statusLogT >= statusLogInterval)
        {
            int dictCount = (dataReceiver != null && dataReceiver.footSensors != null) ? dataReceiver.footSensors.Count : 0;
            Debug.Log($"[SegwayController] feetSrc:{(footInput != null ? "FootInput" : (dictCount > 0 ? "ReceiverDict" : "NONE"))} " +
                      $"toesMax:{toesMax:F2} heelsMax:{heelsMax:F2} dir:{desiredDir} mag:{mag:F2} dictCount:{dictCount}");
            _statusLogT = 0f;
        }

        if (!_feetKeysLogged && dataReceiver != null && dataReceiver.footSensors != null && dataReceiver.footSensors.Count > 0)
        {
            try
            {
                var keys = string.Join(",", dataReceiver.footSensors.Keys);
                Debug.Log($"[SegwayController] receiver foot keys: {keys}");
            }
            catch { }
            _feetKeysLogged = true;
        }
    }


    // ================= Collision =================
    void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.gameObject.layer == coinsLayer) return; // ignore coins layer

        segway.TriggerCollisionBlackout();

        segway.getup(0f, 0f);

        var rb = segway.getRigidbody();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

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
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

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


    // ================= Helpers =================
    private void ReadFeet(out float lHeel, out float lToe, out float lMidL, out float lMidR,
                          out float rHeel, out float rToe, out float rMidL, out float rMidR)
    {
        // default zeros
        lHeel = lToe = lMidL = lMidR = rHeel = rToe = rMidL = rMidR = 0f;

        if (footInput != null)
        {
            lHeel = footInput.leftHeel;
            lToe = footInput.leftToe;
            lMidL = footInput.leftMidL;
            lMidR = footInput.leftMidR;
            rHeel = footInput.rightHeel;
            rToe = footInput.rightToe;
            rMidL = footInput.rightMidL;
            rMidR = footInput.rightMidR;
            return;
        }

        if (dataReceiver != null && dataReceiver.footSensors != null)
        {
            dataReceiver.footSensors.TryGetValue("left_heel_norm", out lHeel);
            dataReceiver.footSensors.TryGetValue("left_toe_norm", out lToe);
            dataReceiver.footSensors.TryGetValue("left_mid_l_norm", out lMidL);
            dataReceiver.footSensors.TryGetValue("left_mid_r_norm", out lMidR);
            dataReceiver.footSensors.TryGetValue("right_heel_norm", out rHeel);
            dataReceiver.footSensors.TryGetValue("right_toe_norm", out rToe);
            dataReceiver.footSensors.TryGetValue("right_mid_l_norm", out rMidL);
            dataReceiver.footSensors.TryGetValue("right_mid_r_norm", out rMidR);
        }
    }

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
