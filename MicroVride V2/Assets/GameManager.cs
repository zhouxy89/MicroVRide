using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // Keep this enum INSIDE GameManager to avoid global enum conflicts
    public enum VehicleKind { EScooter, Segway, Unicycle, Skateboard }

    [Header("UI References")]
    public TMP_Text coinText;
    public GameObject finishMenu;
    public TMP_Text finishCoinText;
    public TMP_Text countdownText;   // optional; drag a TMP text in Finish Menu if you want countdown

    [Header("Finish Logic")]
    [Tooltip("Auto-finish when all coins in the scene are collected.")]
    public bool autoFinishWhenAllCollected = true;
    [Tooltip("If > 0, the spawner should call InitializeRun(totalCoins). If left 0, we count at Start().")]
    public int expectedTotalCoins = 0;
    [Tooltip("Seconds to show finish menu before auto-return. Set 0 to disable auto return.")]
    public float finishMenuDuration = 10f;

    // ---- events for HUD ----
    public event System.Action<int> OnCoinChanged;
    public event System.Action<VehicleKind> OnVehicleChanged;

    // ---- state ----
    [SerializeField] private VehicleKind currentVehicle = VehicleKind.EScooter;
    public VehicleKind CurrentVehicle => currentVehicle;

    // Public read-only coin count (use this in your HUD)
    public int CoinCount => _collected;

    // Internals
    int _collected = 0;
    int _totalCoins = 0;
    bool _finished = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // If you want this to persist across scenes, uncomment:
        // DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _collected = 0;
        _finished = false;

        // Determine total coin count for auto-finish
        if (expectedTotalCoins > 0)
            _totalCoins = expectedTotalCoins;
        else
            _totalCoins = CountCoinsInScene();

        UpdateCoinUI();
        if (finishMenu) finishMenu.SetActive(false);
        if (countdownText) countdownText.text = ""; // clear countdown at start
    }

    // ---- called by your simulator bootstrap when enabling a controller ----
    public void SetVehicle(VehicleKind kind)
    {
        currentVehicle = kind;
        OnVehicleChanged?.Invoke(currentVehicle);
    }

    // ---- spawner should call this after spawning coins (recommended) ----
    public void InitializeRun(int totalCoins)
    {
        _totalCoins = Mathf.Max(0, totalCoins);
        _collected = 0;
        _finished = false;
        UpdateCoinUI();
        if (finishMenu) finishMenu.SetActive(false);
        if (countdownText) countdownText.text = "";
        OnCoinChanged?.Invoke(_collected);
    }

    // ---- called by Coin.cs on pickup ----
    public void AddCoin(int amount)
    {
        if (_finished) return;

        _collected += Mathf.Max(0, amount);
        UpdateCoinUI();
        OnCoinChanged?.Invoke(_collected);

        if (autoFinishWhenAllCollected && _totalCoins > 0 && _collected >= _totalCoins)
            FinishRide();
    }

    void UpdateCoinUI()
    {
        if (!coinText) return;

        if (_totalCoins > 0)
            coinText.text = $"Coins: {_collected}/{_totalCoins}";
        else
            coinText.text = $"Coins: {_collected}";
    }

    public void FinishRide()
    {
        if (_finished) return;
        _finished = true;

        if (finishMenu) finishMenu.SetActive(true);
        if (finishCoinText) finishCoinText.text = $"You collected {_collected} coins!";

        if (Study.StudyLogger.Instance != null)
            Study.StudyLogger.Instance.FinishRide();

        if (finishMenuDuration > 0f)
            StartCoroutine(AutoReturnToStart(finishMenuDuration));


    }

    IEnumerator AutoReturnToStart(float delay)
    {
        float t = delay;
        while (t > 0f)
        {
            if (countdownText) countdownText.text = $"Returning in {Mathf.CeilToInt(t)}...";
            yield return new WaitForSeconds(1f);
            t -= 1f;
        }
        BackToStart();
    }

    public void BackToStart()
    {
        // Tell Start scene to skip calibration once
        StartSceneFlow.SkipCalibrationNextLoad = true;
        SceneManager.LoadScene("Start");
    }

    int CountCoinsInScene()
    {
        var coins = FindObjectsOfType<Coin>(true);
        return coins?.Length ?? 0;
    }
}
