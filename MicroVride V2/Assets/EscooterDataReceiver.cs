using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using TMPro;

public class EscooterDataReceiver : MonoBehaviour
{
    public static EscooterDataReceiver Instance { get; private set; }

    public int throttlePort = 4210;
    public int imuPort = 1235;

    [Header("UI Debug Output")]
    public TMP_Text debugText; // Assign in the Start scene Canvas

    [Header("Latest Sensor Readings")]
    [Range(0f, 1f)] public float throttle = 0f;
    public float imuPitch = 0f;
    public float imuRoll = 0f;
    public float imuYaw = 0f;

    private UdpClient throttleClient;
    private UdpClient imuClient;

    private IPEndPoint throttleEndPoint;
    private IPEndPoint imuEndPoint;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); // Stay alive across scene loads
    }

    private void Start()
    {
        throttleClient = new UdpClient(throttlePort);
        imuClient = new UdpClient(imuPort);

        throttleClient.Client.ReceiveTimeout = 1;
        imuClient.Client.ReceiveTimeout = 1;

        throttleEndPoint = new IPEndPoint(IPAddress.Any, throttlePort);
        imuEndPoint = new IPEndPoint(IPAddress.Any, imuPort);
    }

    private void Update()
    {
        ReceiveThrottleData();
        ReceiveIMUData();
        UpdateDebugUI();
    }

    private void ReceiveThrottleData()
    {
        try
        {
            byte[] data = throttleClient.Receive(ref throttleEndPoint);
            string msg = Encoding.UTF8.GetString(data);
            if (float.TryParse(msg, out float val))
            {
                throttle = Mathf.Clamp01(val);
            }
        }
        catch { }
    }

    private void ReceiveIMUData()
    {
        try
        {
            byte[] data = imuClient.Receive(ref imuEndPoint);
            string msg = Encoding.UTF8.GetString(data);
            var parsed = ParseMessage(msg);

            if (parsed.TryGetValue("imu-pitch", out string pitchStr) && float.TryParse(pitchStr, out float pitch))
                imuPitch = pitch;
            if (parsed.TryGetValue("imu-roll", out string rollStr) && float.TryParse(rollStr, out float roll))
                imuRoll = roll;
            if (parsed.TryGetValue("imu-yaw", out string yawStr) && float.TryParse(yawStr, out float yaw))
                imuYaw = yaw;
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

    private void UpdateDebugUI()
    {
        if (debugText != null)
        {
            debugText.text =
                $"Throttle: {throttle:F2}\n" +
                $"Pitch: {imuPitch:F2}\n" +
                $"Roll: {imuRoll:F2}\n" +
                $"Yaw: {imuYaw:F2}";
        }
    }
}
