
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class ESP32Receiver : MonoBehaviour
{
    public int listenPort = 1234; // must match ESP32 sender port
    public string lastReceivedRaw = "";

    [Header("Sensor Values")]
    public int fsrLeftHeel;
    public int fsrRightHeel;
    public int fsrLeftToe;
    public int fsrRightToe;

    [Header("Segway Control")]
    public GameObject controlledObject;  // e.g., a cube
    public float forwardMultiplier = 0.01f;
    public float leanMultiplier = 0.03f;

    private UdpClient udpClient;
    private Thread receiveThread;

    void Start()
    {
        udpClient = new UdpClient(listenPort);
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    void ReceiveData()
    {
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, listenPort);
        while (true)
        {
            try
            {
                byte[] data = udpClient.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);
                lastReceivedRaw = text;

                if (text.StartsWith("fs-"))
                {
                    string[] parts = text.Substring(3).Split('-');
                    if (parts.Length >= 4)
                    {
                        int.TryParse(parts[0], out fsrLeftHeel);
                        int.TryParse(parts[1], out fsrRightHeel);
                        int.TryParse(parts[2], out fsrLeftToe);
                        int.TryParse(parts[3], out fsrRightToe);
                    }
                }
            }
            catch (System.Exception err)
            {
                Debug.LogError("UDP Receive error: " + err.ToString());
            }
        }
    }

    void Update()
    {
        if (controlledObject != null)
        {
            float forward = (fsrLeftToe + fsrRightToe - fsrLeftHeel - fsrRightHeel) * forwardMultiplier;
            float lean = (fsrRightHeel - fsrLeftHeel) * leanMultiplier;

            Vector3 move = new Vector3(lean, 0, forward);
            controlledObject.transform.Translate(move * Time.deltaTime, Space.World);
        }
    }

    void OnApplicationQuit()
    {
        if (receiveThread != null) receiveThread.Abort();
        if (udpClient != null) udpClient.Close();
    }
}
