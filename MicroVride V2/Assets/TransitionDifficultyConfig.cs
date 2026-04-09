using UnityEngine;

public enum DifficultyLabel { Easy, Medium, Hard }

[CreateAssetMenu(menuName = "Coins/Transition Difficulty Config", fileName = "TransitionDifficultyConfig")]
public class TransitionDifficultyConfig : ScriptableObject
{
    [Header("Lateral bounds (meters) relative to path center")]
    [Tooltip("Absolute max |lateral| offset allowed for any coin.")]
    public float maxLateralAmplitude = 0.8f;

    [Header("Difficulty buckets (delta = |lat[i] - lat[i-1]|)")]
    [Tooltip("Easy if delta < easyMax")]
    public float easyMax = 0.25f;
    [Tooltip("Medium if easyMax <= delta < medMax; Hard otherwise")]
    public float medMax = 0.6f;

    [Header("Target mix (sums to ~1.0)")]
    [Range(0, 1)] public float targetEasy = 0.4f;
    [Range(0, 1)] public float targetMedium = 0.4f;
    [Range(0, 1)] public float targetHard = 0.2f;

    [Header("Generator attempts")]
    [Tooltip("Tries per coin to sample a lateral that hits the requested bucket before relaxing.")]
    public int attemptsPerCoin = 30;
}
