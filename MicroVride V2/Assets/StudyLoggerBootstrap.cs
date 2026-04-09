using UnityEngine;


public class StudyLoggerBootstrap : MonoBehaviour
{
    public Study.VehicleKind vehicleKind = Study.VehicleKind.EScooter;

    void Start()
    {
        var metas = FindObjectsOfType<CoinMeta>(true);
        int totalCoins = metas != null ? metas.Length : 0;

        if (Study.StudyLogger.Instance != null)
        {
            Study.StudyLogger.Instance.StartRide(vehicleKind, totalCoins);
            Study.StudyLogger.Instance.RegisterCoinsCatalog(new System.Collections.Generic.List<CoinMeta>(metas));
            Debug.Log($"[StudyLoggerBootstrap] Logging started for {vehicleKind}, coins={totalCoins}");
        }
    }

}
