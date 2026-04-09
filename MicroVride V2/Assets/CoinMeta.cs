using UnityEngine;

public class CoinMeta : MonoBehaviour
{
    [Header("Index along path")]
    public int index;

    [Header("Distance along path (meters)")]
    public float s;                 // cumulative distance along path at placement

    [Header("Steering metrics (meters)")]
    public float lateral;           // lateral offset relative to path center
    public float deltaLateral;      // |lateral[i] - lateral[i-1]|

    [Header("Difficulty label from delta")]
    public DifficultyLabel label;

    [Header("Waypoint segment index (between wp[i] and wp[i+1])")]
    public int segmentIndex;
}
