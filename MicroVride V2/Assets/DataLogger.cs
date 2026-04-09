using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VK.BikeLab.Segway;

public class DataLogger : MonoBehaviour
{
    private StreamWriter writer;
    private string filePath;

    [Header("References")]
    public FootSensorInput footSensorInput;
    public Segway segway;

    [Header("Logging Options")]
    public bool logEveryFrame = true;
    public float logInterval = 0.05f;

    private float logTimer = 0f;
    private bool calibrationLogged = false;

    void Start()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

#if UNITY_ANDROID && !UNITY_EDITOR
        string folderPath = "/storage/emulated/0/Documents";
#else
        string folderPath = Application.persistentDataPath;
#endif

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        filePath = Path.Combine(folderPath, $"log_{timestamp}.csv");
        writer = new StreamWriter(filePath);
        writer.WriteLine("Time,LeftHeel,LeftToe,LeftMidL,LeftMidR,RightHeel,RightToe,RightMidL,RightMidR,Speed,Turn,Event");

        Debug.Log($"📄 DataLogger started: {filePath}");
    }

    void Update()
    {
        if (!calibrationLogged && footSensorInput != null && footSensorInput.IsCalibrationComplete())
        {
            LogCalibrationData();
            calibrationLogged = true;
        }

        if (logEveryFrame || logTimer >= logInterval)
        {
            logTimer = 0f;

            float time = Time.time;
            float speed = segway != null ? segway.getVelosity() : 0f;
            float turn = segway != null ? segway.getTurnDir() : 0f;

            string line = $"{time:F3}," +
                          $"{footSensorInput.leftHeel:F3},{footSensorInput.leftToe:F3},{footSensorInput.leftMidL:F3},{footSensorInput.leftMidR:F3}," +
                          $"{footSensorInput.rightHeel:F3},{footSensorInput.rightToe:F3},{footSensorInput.rightMidL:F3},{footSensorInput.rightMidR:F3}," +
                          $"{speed:F3},{turn:F3},";
            writer.WriteLine(line);
        }

        logTimer += Time.deltaTime;
    }

    void LogCalibrationData()
    {
        writer.WriteLine("### Calibration Baseline Values");
        var baseline = footSensorInput.GetBaselineData();
        foreach (var kv in baseline)
            writer.WriteLine($"{kv.Key} baseline = {kv.Value}");

        writer.WriteLine("### Calibration Max Values");
        var max = footSensorInput.GetMaxData();
        foreach (var kv in max)
            writer.WriteLine($"{kv.Key} max = {kv.Value}");

        writer.WriteLine("### End Calibration Data");
    }

    public void LogEvent(string eventDescription)
    {
        string line = $"{Time.time:F3},,,,,,,,,,Event:{eventDescription}";
        writer.WriteLine(line);
    }

    void OnCollisionEnter(Collision collision)
    {
        LogEvent($"Collision with {collision.gameObject.name}");
    }

    private void OnApplicationQuit()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            Debug.Log($"📄 DataLogger saved to {filePath}");
        }
    }
}
