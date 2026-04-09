using UnityEngine;
using UnityEngine.SceneManagement;

public class CoinLifecycleController : MonoBehaviour
{
    [Header("Assign in this simulator scene")]
    public TransitionCoinSpawner spawner;     // your existing spawner in this scene
    public Transform xrCamera;                // XR rig's Main Camera (the headset)
    [Tooltip("Name of the Start scene to load when exiting.")]
    public string startSceneName = "Start";

    void Start()
    {
        if (!spawner)
        {
            spawner = FindObjectOfType<TransitionCoinSpawner>(true);
            if (!spawner)
            {
                Debug.LogWarning("[CoinLifecycleController] No TransitionCoinSpawner found.");
                return;
            }
        }

        // Inject XR camera so coins can billboard
        spawner.xrCamera = xrCamera ? xrCamera : (Camera.main ? Camera.main.transform : null);

        // Force runtime spawn now (independent of editor settings)
        spawner.SpawnRuntime();
    }

    /// <summary>Wire this to your Menu/Back button (UI or controller) to exit.</summary>
    public void BackToStart()
    {
        if (spawner) spawner.ClearBaked();

        StartSceneFlow.SkipCalibrationNextLoad = true;

        SceneManager.LoadScene(startSceneName, LoadSceneMode.Single);
    }

    // Safety: if scene unloads or this object is disabled, ensure coins are cleaned up.
    void OnDisable()
    {
        if (spawner && Application.isPlaying)
            spawner.ClearBaked();
    }
}
