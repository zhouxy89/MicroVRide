using UnityEngine;
using System.Collections.Generic;

public class BlockManager : MonoBehaviour
{
    public Transform player;           // XR Rig or vehicle root
    public float loadDistance = 40f;   // distance at which blocks activate
    public float unloadDistance = 80f; // distance at which blocks deactivate

    [System.Serializable]
    public class BlockData
    {
        public GameObject blockObject; // the block prefab instance in the scene
        public Vector3 center;         // world position of the block’s center
        [HideInInspector] public bool isActive = false;
    }

    public List<BlockData> blocks = new List<BlockData>();

    void Update()
    {
        if (player == null) return;

        Vector3 playerPos = player.position;

        foreach (var block in blocks)
        {
            float dist = Vector3.Distance(playerPos, block.center);

            // Activate when near
            if (!block.isActive && dist < loadDistance)
            {
                block.blockObject.SetActive(true);
                block.isActive = true;
                Debug.Log("✅ Activated block: " + block.blockObject.name);
            }

            // Deactivate when far
            if (block.isActive && dist > unloadDistance)
            {
                block.blockObject.SetActive(false);
                block.isActive = false;
                Debug.Log("❌ Deactivated block: " + block.blockObject.name);
            }
        }
    }
}
