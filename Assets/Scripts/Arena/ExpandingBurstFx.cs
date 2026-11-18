using UnityEngine;

/// <summary>
/// Animates an expanding, rotating 3D burst effect and self-destructs upon completion.
/// </summary>
public sealed class ExpandingBurstFx : MonoBehaviour
{
    public float duration = 0.36f;
    public float startScale = 0.35f;
    public float endScale = 3.6f;
    private float elapsed = 0f;

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        float scale = Mathf.Lerp(startScale, endScale, eased);
        transform.localScale = Vector3.one * scale;
        transform.Rotate(0f, 190f * Time.deltaTime, 0f, Space.Self);
        if (elapsed >= duration)
        {
            Destroy(gameObject);
        }
    }
}
