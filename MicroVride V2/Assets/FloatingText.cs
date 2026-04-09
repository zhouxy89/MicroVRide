using UnityEngine;
using TMPro;

public class FloatingText : MonoBehaviour
{
    public float riseSpeed = 1f;
    public float lifetime = 1f;
    public float fadeDuration = 0.5f;

    private TMP_Text textMesh;
    private Color startColor;
    private float timer = 0f;

    private void Awake()
    {
        textMesh = GetComponent<TMP_Text>();
        startColor = textMesh.color;
    }

    private void Update()
    {
        transform.position += Vector3.up * riseSpeed * Time.deltaTime;

        timer += Time.deltaTime;
        if (timer > lifetime)
        {
            float fadeT = (timer - lifetime) / fadeDuration;
            textMesh.color = Color.Lerp(startColor, new Color(startColor.r, startColor.g, startColor.b, 0), fadeT);

            if (fadeT >= 1f)
                Destroy(gameObject);
        }
    }
}
