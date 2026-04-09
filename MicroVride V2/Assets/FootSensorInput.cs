using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;

public class FootSensorInput : MonoBehaviour
{

    public static FootSensorInput Instance { get; private set; }

    [Header("Network Settings")]
    public int listenPort = 1234;
    public string leftBoardIP = "172.20.10.2";
    public string rightBoardIP = "172.20.10.3";

    [Header("UI")]
    public TMP_Text calibrationMessage;
    public VehicleSelectionManager vehicleSelectionManager;

    [Header("Sensor state")]
    public float currentForwardForce = 0f;
    public float currentSideForce = 0f;

    [Header("Raw sensor values for logging")]
    public float leftHeel = 0f, leftToe = 0f, leftMidL = 0f, leftMidR = 0f;
    public float rightHeel = 0f, rightToe = 0f, rightMidL = 0f, rightMidR = 0f;

    private UdpClient udpClient;
    private Thread receiveThread;
    private Queue<string> messageQueue = new Queue<string>();
    private object queueLock = new object();

    private bool calibrationComplete = false;

    private bool leftPrepReceived = false;
    private bool rightPrepReceived = false;
    private bool leftBaselineDone = false;
    private bool rightBaselineDone = false;
    private bool leftMaxDone = false;
    private bool rightMaxDone = false;
    private bool baselinePhaseCompleted = false;
    private bool maxPhaseCompleted = false;

    private enum Phase { None, Prep, Baseline, Max }
    private Phase currentPhase = Phase.None;
    private float retryTimer = 0f;
    private float retryInterval = 2f;

    public TMP_Text debugText;

    [Header("Debug")]
    public float lastFeetUpdateTime = -1f;  // Time.realtimeSinceStartup of last fs packet


    private Dictionary<string, int> baselineValues = new Dictionary<string, int>();
    private Dictionary<string, int> maxValues = new Dictionary<string, int>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);           // kill duplicates (e.g., in sim scenes)
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);     // one receiver across all scenes
    }



    void Start()
    {
        udpClient = new UdpClient(listenPort);
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        BeginPrepPhase();
    }

    void Update()
    {
        ProcessUIQueue();

        if (!calibrationComplete)
        {
            retryTimer += Time.deltaTime;
            if (retryTimer >= retryInterval)
            {
                retryTimer = 0f;

                if (currentPhase == Phase.Prep)
                {
                    VRLog("Sending start_prep to both boards");
                    SendToBoard("start_prep", leftBoardIP);
                    SendToBoard("start_prep", rightBoardIP);
                }
                else if (currentPhase == Phase.Baseline)
                {
                    VRLog("Sending start_baseline to both boards");
                    SendToBoard("start_baseline", leftBoardIP);
                    SendToBoard("start_baseline", rightBoardIP);
                }
                else if (currentPhase == Phase.Max)
                {
                    VRLog("Sending start_max to both boards");
                    SendToBoard("start_max", leftBoardIP);
                    SendToBoard("start_max", rightBoardIP);
                }
            }
        }
    }

    private void ProcessUIQueue()
    {
        if (messageQueue.Count > 0)
        {
            string msg = "";
            lock (queueLock)
                msg = messageQueue.Dequeue();

            if (calibrationMessage != null)
                calibrationMessage.text = msg;
        }
    }

    private void SendToBoard(string msg, string boardIP)
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(boardIP), listenPort);
        byte[] payload = Encoding.UTF8.GetBytes(msg);
        udpClient.Send(payload, payload.Length, remoteEP);
    }

    private void ReceiveData()
    {
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, listenPort);
        while (true)
        {
            try
            {
                byte[] data = udpClient.Receive(ref anyIP);
                string message = Encoding.UTF8.GetString(data).Trim();
                VRLog("Received: " + message);
                ProcessMessage(message);
            }
            catch (Exception e)
            {
                VRLog("Receive error: " + e.Message);
            }
        }
    }

    private void ProcessMessage(string message)
    {
        if (message.Contains("status="))
        {
            string board = message.Contains("board=left") ? "Left" : (message.Contains("board=right") ? "Right" : "");

            if (message.Contains("status=prep_start"))
            {
                if (board == "Left") leftPrepReceived = true;
                else if (board == "Right") rightPrepReceived = true;
                CheckPrepReady();
            }
            else if (message.Contains("status=baseline_done"))
            {
                if (board == "Left") leftBaselineDone = true;
                else if (board == "Right") rightBaselineDone = true;
                CheckBaselineReady();
            }
            else if (message.Contains("status=max_done"))
            {
                if (board == "Left") leftMaxDone = true;
                else if (board == "Right") rightMaxDone = true;
                CheckMaxReady();
            }
            else if (message.StartsWith("baseline="))
            {
                ParseCalibrationData(message, baselineValues);
            }
            else if (message.StartsWith("max="))
            {
                ParseCalibrationData(message, maxValues);
            }
        }
        //else if (message.StartsWith("fs-") && calibrationComplete)
        else if (message.StartsWith("fs-"))
        {
            ParseSensorData(message);
        }
    }

    private void CheckPrepReady()
    {
        if (leftPrepReceived && rightPrepReceived)
            BeginBaselinePhase();
    }

    private void CheckBaselineReady()
    {
        if (!baselinePhaseCompleted && leftBaselineDone && rightBaselineDone)
        {
            baselinePhaseCompleted = true;
            currentPhase = Phase.None;
            EnqueueMessage("Baseline done, please stand on the board.");
            Invoke(nameof(BeginMaxPhase), 3f);
        }
    }

    private void CheckMaxReady()
    {
        if (!maxPhaseCompleted && leftMaxDone && rightMaxDone)
        {
            maxPhaseCompleted = true;
            calibrationComplete = true;
            currentPhase = Phase.None;

            EnqueueMessage("Calibration complete.");

            if (vehicleSelectionManager != null)
                UnityMainThreadDispatcher.Instance().Enqueue(() => vehicleSelectionManager.OnCalibrationComplete());

            Invoke(nameof(ClearMessage), 3f);
        }
    }

    private void ParseCalibrationData(string msg, Dictionary<string, int> dict)
    {
        foreach (var part in msg.Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && int.TryParse(kv[1], out int v))
                dict[kv[0]] = v;
        }
    }

    private void ParseSensorData(string message)
    {
        //debugText.text = "[FootSensorInput] RAW sensor msg: " + message;

        Dictionary<string, string> dict = ParseMessage(message);

        foreach (var kv in dict)
        {
            //debugText.text = $"[FootSensorInput] Key={kv.Key}, Value={kv.Value}";
        }



        leftHeel = ParseFloat(dict, "left_heel_norm");
        leftToe = ParseFloat(dict, "left_toe_norm");
        leftMidL = ParseFloat(dict, "left_mid_l_norm");
        leftMidR = ParseFloat(dict, "left_mid_r_norm");

        rightHeel = ParseFloat(dict, "right_heel_norm");
        rightToe = ParseFloat(dict, "right_toe_norm");
        rightMidL = ParseFloat(dict, "right_mid_l_norm");
        rightMidR = ParseFloat(dict, "right_mid_r_norm");

        lastFeetUpdateTime = Time.realtimeSinceStartup;


        //debugText.text = "[FootSensorInput] Parsed values: " + $"lToe={leftToe:F2}, rToe={rightToe:F2}, lHeel={leftHeel:F2}, rHeel={rightHeel:F2}";

        float leftForward = leftToe - leftHeel;
        float rightForward = rightToe - rightHeel;
        currentForwardForce = Mathf.Clamp((leftForward + rightForward) * 0.5f, -1f, 1f);

        float leftLean = leftMidR - leftMidL;
        float rightLean = rightMidL - rightMidR;
        currentSideForce = Mathf.Clamp((leftLean + rightLean) * 0.5f, -1f, 1f);
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

    private float ParseFloat(Dictionary<string, string> dict, string key)
    {
        if (dict.TryGetValue(key, out string val))
        {
            if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                return f;
        }
        return 0f;
    }


    private void BeginPrepPhase()
    {
        currentPhase = Phase.Prep;
        retryTimer = 0f;
        EnqueueMessage("Feet force calibration starting. Preparing...");
    }

    private void BeginBaselinePhase()
    {
        currentPhase = Phase.Baseline;
        retryTimer = 0f;
        EnqueueMessage("Baseline calibration: Please do not stand on the board.");
    }

    private void BeginMaxPhase()
    {
        currentPhase = Phase.Max;
        retryTimer = 0f;
        EnqueueMessage("Max feet force calibration: Press firmly with both feet.");
    }

    private void ClearMessage()
    {
        if (calibrationMessage != null)
            calibrationMessage.text = "";
    }

    private void EnqueueMessage(string msg)
    {
        lock (queueLock)
            messageQueue.Enqueue(msg);
    }

    private void VRLog(string message)
    {
        Debug.Log("[FootSensorInput] " + message);
        //EnqueueMessage("[ESP] " + message);
    }

    public bool IsCalibrationComplete() => calibrationComplete;
    public Dictionary<string, int> GetBaselineData() => baselineValues;
    public Dictionary<string, int> GetMaxData() => maxValues;

    private void OnApplicationQuit()
    {
        receiveThread?.Abort();
        udpClient?.Close();
    }
}
