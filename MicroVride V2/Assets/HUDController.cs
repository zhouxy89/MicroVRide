using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class HUDController : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text coinText;     // TMP text that shows coin count
    public Image vehicleIcon;     // Single image; assign the fixed icon sprite here

    [Header("Placement")]
    [Tooltip("Offset (meters) from XR camera local origin.")]
    public Vector3 localOffset = new Vector3(0.35f, -0.25f, 0.7f);
    [Tooltip("Rotation smoothing toward camera forward (0 = immediate).")]
    [Range(0f, 20f)] public float faceSmoothing = 12f;

    Transform cam;
    RectTransform rt;

    void Awake()
    {
        cam = Camera.main ? Camera.main.transform : null;
        if (cam != null && transform.parent != cam)
            transform.SetParent(cam, worldPositionStays: false);

        rt = GetComponent<RectTransform>();
    }

    void Start()
    {
        // Ensure the icon is visible
        if (vehicleIcon != null)
            vehicleIcon.enabled = (vehicleIcon.sprite != null);
    }

    void Update()
    {
        if (coinText != null && GameManager.Instance != null)
            coinText.text = $"Coins: {GameManager.Instance.CoinCount}";
    }

    void LateUpdate()
    {
        if (cam == null && Camera.main) cam = Camera.main.transform;
        if (cam == null || rt == null) return;

        rt.localPosition = localOffset;

        Quaternion targetRot = Quaternion.LookRotation(cam.forward, cam.up);
        transform.rotation = (faceSmoothing > 0f)
            ? Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * faceSmoothing)
            : targetRot;
    }
}
