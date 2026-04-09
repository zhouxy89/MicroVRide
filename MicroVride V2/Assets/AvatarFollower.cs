using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AvatarFollower : MonoBehaviour
{
    public Transform avatarRoot;
    public Transform segwayTransform;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;

    void Start()
    {
        if (avatarRoot != null && segwayTransform != null)
        {
            initialLocalPosition = avatarRoot.localPosition;
            initialLocalRotation = avatarRoot.localRotation;
        }
    }

    void LateUpdate()
    {
        if (avatarRoot != null && segwayTransform != null)
        {
            avatarRoot.localPosition = initialLocalPosition;
            avatarRoot.localRotation = initialLocalRotation;
        }
    }
}
