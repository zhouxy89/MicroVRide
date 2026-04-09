using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class VehicleDataReceiver : MonoBehaviour
{
    public static VehicleDataReceiver Instance { get; private set; }

    [Header("Ports")]
    public int throttlePort = 4210;
    public int imuPort = 1235;
    public int footSensorPort = 1234;

    [Header("Throttle Data")]
    [Range(0f, 1f)] public float throttle = 0f;

    [Header("IMU Data")]
    public float imuPitch = 0f;
    public float imuRoll = 0f;
    public float imuYaw = 0f;

    [Header("Foot Sensor Data (Normalized)")]
    public Dictionary<string, float> footSensors = new Dictionary<string, float>();

    private UdpClient throttleClient;
    private UdpClient imuClient;
    private UdpClient footSensorClient;

    private IPEndPoint throttleEndPoint;
    private IPEndPoint imuEndPoint;
    private IPEndPoint footSensorEndPoint;

    [Header("Foot Sensor Debug")]
    public bool logFirstFootKeys = true;
    private bool _footKeysLogged = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); // persist across scenes
    }

    private void Start()
    {
        throttleClient = new UdpClient(throttlePort);
        imuClient = new UdpClient(imuPort);
        footSensorClient = new UdpClient(footSensorPort);

        // Non-blocking (no timeout stalls on main thread)
        throttleClient.Client.ReceiveTimeout = 0;
        imuClient.Client.ReceiveTimeout = 0;
        footSensorClient.Client.ReceiveTimeout = 0;

        throttleEndPoint = new IPEndPoint(IPAddress.Any, throttlePort);
        imuEndPoint = new IPEndPoint(IPAddress.Any, imuPort);
        footSensorEndPoint = new IPEndPoint(IPAddress.Any, footSensorPort);

        Debug.Log($"[VehicleDataReceiver] Listening Throttle:{throttlePort}, IMU:{imuPort}, Feet:{footSensorPort}");
    }

    private void Update()
    {
        ReceiveThrottleData();
        ReceiveIMUData();
        ReceiveFootSensorData();
    }

    private void ReceiveThrottleData()
    {
        try
        {
            while (throttleClient.Available > 0) // drain
            {
                byte[] data = throttleClient.Receive(ref throttleEndPoint);
                string msg = Encoding.UTF8.GetString(data);
                if (float.TryParse(msg, out float val))
                    throttle = Mathf.Clamp01(val);
            }
        }
        catch { }
    }

    private void ReceiveIMUData()
    {
        try
        {
            while (imuClient.Available > 0) // drain all queued packets
            {
                byte[] data = imuClient.Receive(ref imuEndPoint);
                string msg = Encoding.UTF8.GetString(data);
                var parsed = ParseMessage(msg);

                if (parsed.TryGetValue("imu-pitch", out string pitchStr) && float.TryParse(pitchStr, out float pitch)) imuPitch = pitch;
                if (parsed.TryGetValue("imu-roll", out string rollStr) && float.TryParse(rollStr, out float roll)) imuRoll = roll;
                if (parsed.TryGetValue("imu-yaw", out string yawStr) && float.TryParse(yawStr, out float yaw)) imuYaw = yaw;
            }
        }
        catch { }
    }


    private void ReceiveFootSensorData()
    {
        try
        {
            bool any = false;
            while (footSensorClient.Available > 0) // drain
            {
                byte[] data = footSensorClient.Receive(ref footSensorEndPoint);
                string msg = Encoding.UTF8.GetString(data);
                var parsed = ParseMessage(msg);

                foreach (var kv in parsed)
                {
                    if (!float.TryParse(kv.Value, out float val)) continue;
                    string key = kv.Key.Trim();

                    // Store *_norm as-is
                    if (key.EndsWith("_norm"))
                    {
                        footSensors[key] = val;

                        // Also mirror to base key (no _norm) so controllers can read either
                        string baseKey = key.Substring(0, key.Length - 5);
                        if (!footSensors.ContainsKey(baseKey))
                            footSensors[baseKey] = val;

                        any = true;
                    }
                    else
                    {
                        // If sender uses base keys, store them too
                        footSensors[key] = val;
                        any = true;
                    }
                }
            }

            if (any && logFirstFootKeys && !_footKeysLogged)
            {
                try
                {
                    var keys = string.Join(",", footSensors.Keys);
                    Debug.Log($"[VehicleDataReceiver] Foot keys: {keys}");
                }
                catch { }
                _footKeysLogged = true;
            }
        }
        catch { }
    }

    private Dictionary<string, string> ParseMessage(string msg)
    {
        var dict = new Dictionary<string, string>();
        foreach (var part in msg.Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2)
                dict[kv[0]] = kv[1];
        }
        return dict;
    }
}
