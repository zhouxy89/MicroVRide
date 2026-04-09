using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using VK.BikeLab.Segway;

public class DataLoggerNetwork : MonoBehaviour
{
    public FootSensorInput footSensorInput;
    public Segway segway;

    [Header("Receiver Settings")]
    public string receiverIP = "192.168.1.100";  // 🔔 Replace with your Mac's IP address
    public int receiverPort = 5678;

    private UdpClient udpClient;

    private bool calibrationLogged = false;
    private float logInterval = 0.05f;
    private float logTimer = 0f;

    void Start()
    {
        udpClient = new UdpClient();
        Debug.Log("📡 DataLoggerNetwork initialized");
    }

    void Update()
    {
        if (!calibrationLogged && footSensorInput != null && footSensorInput.IsCalibrationComplete())
        {
            SendCalibrationData();
            calibrationLogged = true;
        }

        logTimer += Time.deltaTime;
        if (logTimer >= logInterval)
        {
            logTimer = 0f;
            SendSensorData();
        }
    }

    void SendCalibrationData()
    {
        var baseline = footSensorInput.GetBaselineData();
        var max = footSensorInput.GetMaxData();

        SendLine("### Calibration Baseline Values");
        foreach (var kv in baseline)
            SendLine($"{kv.Key} baseline = {kv.Value}");

        SendLine("### Calibration Max Values");
        foreach (var kv in max)
            SendLine($"{kv.Key} max = {kv.Value}");

        SendLine("### End Calibration Data");
    }

    void SendSensorData()
    {
        float time = Time.time;
        float speed = segway != null ? segway.getVelosity() : 0f;
        float turn = segway != null ? segway.getTurnDir() : 0f;

        string line = $"{time:F3}," +
                      $"{footSensorInput.leftHeel:F3},{footSensorInput.leftToe:F3},{footSensorInput.leftMidL:F3},{footSensorInput.leftMidR:F3}," +
                      $"{footSensorInput.rightHeel:F3},{footSensorInput.rightToe:F3},{footSensorInput.rightMidL:F3},{footSensorInput.rightMidR:F3}," +
                      $"{speed:F3},{turn:F3}";

        SendLine(line);
    }

    void SendLine(string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        udpClient.Send(bytes, bytes.Length, receiverIP, receiverPort);
    }

    void OnApplicationQuit()
    {
        if (udpClient != null)
            udpClient.Close();
    }
}
