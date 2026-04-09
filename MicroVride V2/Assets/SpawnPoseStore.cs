// SpawnPoseStore.cs
using UnityEngine;

public static class SpawnPoseStore
{
    public static bool HasData;
    public static Vector3 TargetHeadWorldPos;
    public static float TargetHeadYawDeg;
    public static Vector3 DeckOffsetLocal; // segway local offset to the user's stand point

    public static void Capture(Transform headset, Vector3 deckOffsetLocal)
    {
        if (!headset) { HasData = false; return; }
        TargetHeadWorldPos = headset.position;
        TargetHeadYawDeg = headset.eulerAngles.y;
        DeckOffsetLocal = deckOffsetLocal;
        HasData = true;
    }

    public static void Clear() => HasData = false;
}
