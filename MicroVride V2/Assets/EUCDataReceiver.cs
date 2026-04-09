using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class EUCDataReceiver : MonoBehaviour
{
    public int imuPort = 1235;

    [Header("Latest Sensor Readings")]
    public float imuPitch = 0f;
    public float imuRoll = 0f;
    public float imuYaw = 0f; // optional, if you want to track yaw for orientation

    private UdpClient imuClient;
    private IPEndPoint imuEndPoint;

    void Start()
    {
        imuClient = new UdpClient(imuPort);
        imuClient.Client.ReceiveTimeout = 1;
        imuEndPoint = new IPEndPoint(IPAddress.Any, imuPort);
    }

    void Update()
    {
        ReceiveIMUData();
    }

    void ReceiveIMUData()
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

    Dictionary<string, string> ParseMessage(string msg)
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
