using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;

public class ThrottleReceiver : MonoBehaviour
{
    public int listenPort = 4210;
    public float throttleValue = 0f;

    private UdpClient udpClient;

    void Start()
    {
        udpClient = new UdpClient(listenPort);
        udpClient.Client.ReceiveTimeout = 1;
    }

    void Update()
    {
        try
        {
            while (udpClient.Available > 0)
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, listenPort);
                byte[] data = udpClient.Receive(ref remoteEP);
                string msg = Encoding.ASCII.GetString(data);
                if (float.TryParse(msg, out float val))
                    throttleValue = Mathf.Clamp01(val);
            }
        }
        catch { }
    }

    private void OnApplicationQuit()
    {
        udpClient?.Close();
    }
}
