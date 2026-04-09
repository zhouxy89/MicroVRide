using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class VehicleSelectionManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text statusMessage;
    public GameObject vehicleSelectionPanel;
    public TMP_Text controlActiveIndicator;

    public Button segwayButton;
    public Button escooterButton;
    public Button unicycleButton;
    public Button skateboardButton; // replaces onewheelButton

    [Header("XR References (Start scene)")]
    public Transform startSceneHeadset; // Main Camera in Start scene (XR)
    public Vector3 deckOffset = new Vector3(0f, 0.95f, 0f);

  


    [Header("Options")]
    [Tooltip("If true, the selection UI is shown only after OnCalibrationComplete() is called.")]
    public bool requireCalibration = true;

    //private bool calibrationComplete = false;
    private bool calibrationComplete = true;

    private void Start()
    {
        // If we're returning from a simulator, skip calibration once and show vehicle buttons immediately.
        if (StartSceneFlow.SkipCalibrationNextLoad)
        {
            StartSceneFlow.SkipCalibrationNextLoad = false;  // consume the one-shot flag

            requireCalibration = false;
            calibrationComplete = true;

            if (vehicleSelectionPanel != null)
                vehicleSelectionPanel.SetActive(true);

            // Only show a clean message, no calibration logs
            if (statusMessage != null)
                statusMessage.text = "Please select your vehicle.";
        }
        else
        {
            // Normal startup
            if (vehicleSelectionPanel != null)
                vehicleSelectionPanel.SetActive(!requireCalibration);
        }

        HideControlIndicator();

        // Hook up buttons
        if (segwayButton) segwayButton.onClick.AddListener(() => SelectVehicle(VehicleType.Segway));
        if (escooterButton) escooterButton.onClick.AddListener(() => SelectVehicle(VehicleType.EScooter));
        if (unicycleButton) unicycleButton.onClick.AddListener(() => SelectVehicle(VehicleType.Unicycle));
        if (skateboardButton) skateboardButton.onClick.AddListener(() => SelectVehicle(VehicleType.Skateboard));

        if (!requireCalibration)
            calibrationComplete = true;
    }




    // call this RIGHT BEFORE you start loading the simulator scene
    private IEnumerator LoadVehicleSceneAsync(VehicleType type)
    {


        string sceneName = TrainingSceneForVehicle(type);

        if (string.IsNullOrEmpty(sceneName))
        {
            ShowStatus("[ERROR] No scene mapped for " + type);
            yield break;
        }

        if (vehicleSelectionPanel != null) vehicleSelectionPanel.SetActive(false);
        ShowControlIndicator("Loading " + sceneName + "…");

        SessionState.SelectedVehicle = type;
        SessionState.CalibrationComplete = calibrationComplete;

        yield return null;
        SpawnPoseStore.Capture(startSceneHeadset, deckOffset);
        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;
    }

    /// <summary>
    /// Call this from your calibration flow once done.
    /// </summary>
    public void OnCalibrationComplete()
    {
        calibrationComplete = true;
        SessionState.CalibrationComplete = true;

        ShowStatus("[VehicleSelectionManager] Calibration complete. Please select your vehicle.");
        if (vehicleSelectionPanel != null)
            vehicleSelectionPanel.SetActive(true);
    }

    private void SelectVehicle(VehicleType type)
    {
        if (requireCalibration && !calibrationComplete)
        {
            ShowStatus("[WARN] Please complete calibration before selecting a vehicle.");
            return;
        }

        ShowStatus($"[LOG] Selecting vehicle: {type}");
        

        StartCoroutine(LoadVehicleSceneAsync(type));
    }



    private static string TrainingSceneForVehicle(VehicleType type)
    {
        switch (type)
        {
            case VehicleType.Segway: return "SegwayTraining";
            case VehicleType.EScooter: return "EScooterTraining";
            case VehicleType.Unicycle: return "UnicycleTraining";
            case VehicleType.Skateboard: return "SkateboardTraining";
        }
        return null;
    }

    private static string SimulatorSceneForVehicle(VehicleType type)
    {
        switch (type)
        {
            case VehicleType.Segway: return "SegwaySimulator";
            case VehicleType.EScooter: return "EScooterSimulator";
            case VehicleType.Unicycle: return "UnicycleSimulator";
            case VehicleType.Skateboard: return "SkateboardSimulator";
        }
        return null;
    }


    private void ShowControlIndicator(string text = "Control active — Ride safely.")
    {
        if (!controlActiveIndicator) return;

        controlActiveIndicator.text = text;
        controlActiveIndicator.gameObject.SetActive(true);
        CancelInvoke(nameof(HideControlIndicator));
        Invoke(nameof(HideControlIndicator), 5f);
    }

    private void HideControlIndicator()
    {
        if (controlActiveIndicator)
            controlActiveIndicator.gameObject.SetActive(false);
    }

    public void ShowStatus(string message)
    {
        Debug.Log("[VehicleSelectionManager] " + message);
        if (statusMessage)
        {
            statusMessage.text = message;
            statusMessage.gameObject.SetActive(true);
        }
    }
}
