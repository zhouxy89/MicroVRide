using UnityEngine;

/// One-shot flag to tell the Start scene to skip calibration and show vehicle buttons.
public static class StartSceneFlow
{
    /// If true, the next time the Start scene loads it should skip calibration.
    public static bool SkipCalibrationNextLoad = false;
}
