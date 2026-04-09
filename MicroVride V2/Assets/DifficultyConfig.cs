using UnityEngine;

[CreateAssetMenu(menuName = "Study/Difficulty Config")]
public class DifficultyConfig : ScriptableObject
{
    [Header("Course & Coins")]
    public int coinCount = 40;
    public float minSpacing = 2f;
    public float maxSpacing = 5f;
    public float lateralAmplitude = 0.5f;   // how far coins drift from center
    public float zigZagAmplitude = 1.5f;    // path wiggle
    public float zigZagWavelength = 10f;    // wiggle frequency
    public float heightJitter = 0.1f;

    [Header("Time")]
    public float timeLimitSeconds = 0f;     // 0 = unlimited
}
