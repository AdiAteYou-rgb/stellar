using UnityEngine;

/// <summary>
/// Adds charming organic idle breathing, swaying, and action reactions (recoil, wobble)
/// to placeholder plants and props.
/// </summary>
public class PlantBobber : MonoBehaviour
{
    [Header("Idle Sway & Breathing")]
    public float breatheSpeed = 2.4f;
    public float breatheScale = 0.045f;
    public float swaySpeed = 2.0f;
    public float swayAngle = 3.5f;

    [Header("Reaction Springs")]
    private float recoilOffset = 0f;
    private float recoilVelocity = 0f;
    private float wobbleAngle = 0f;
    private float wobbleVelocity = 0f;

    private Vector3 initialLocalScale;
    private Quaternion initialLocalRotation;
    private Vector3 initialLocalPosition;
    private bool positionInitialized = false;
    private float timeOffset;

    private void Awake()
    {
        initialLocalScale = transform.localScale;
        initialLocalRotation = transform.localRotation;
        initialLocalPosition = transform.localPosition;
        positionInitialized = true;
        timeOffset = Random.Range(0f, 10f);
    }

    public void SetBasePosition(Vector3 pos)
    {
        initialLocalPosition = pos;
        positionInitialized = true;
    }

    private void Update()
    {
        if (!Application.isPlaying) return;

        if (!positionInitialized)
        {
            initialLocalPosition = transform.localPosition;
            positionInitialized = true;
        }

        float t = Time.time + timeOffset;

        // 1. Organic breathing on scale
        float breathe = 1.0f + Mathf.Sin(t * breatheSpeed) * breatheScale;
        transform.localScale = new Vector3(
            initialLocalScale.x * breathe,
            initialLocalScale.y * (2.0f - breathe), // squash & stretch
            initialLocalScale.z * breathe
        );

        // 2. Gentle pendulum sway
        float currentSway = Mathf.Sin(t * swaySpeed) * swayAngle;

        // 3. Spring physics for recoil (shooting)
        if (Mathf.Abs(recoilOffset) > 0.001f || Mathf.Abs(recoilVelocity) > 0.001f)
        {
            float springForce = -recoilOffset * 60f;
            float dampingForce = -recoilVelocity * 10f;
            recoilVelocity += (springForce + dampingForce) * Time.deltaTime;
            recoilOffset += recoilVelocity * Time.deltaTime;
        }
        else
        {
            recoilOffset = 0f;
            recoilVelocity = 0f;
        }

        // 4. Spring physics for wobble (impact/hit)
        if (Mathf.Abs(wobbleAngle) > 0.01f || Mathf.Abs(wobbleVelocity) > 0.01f)
        {
            float spring = -wobbleAngle * 45f;
            float damp = -wobbleVelocity * 8f;
            wobbleVelocity += (spring + damp) * Time.deltaTime;
            wobbleAngle += wobbleVelocity * Time.deltaTime;
        }
        else
        {
            wobbleAngle = 0f;
            wobbleVelocity = 0f;
        }

        Quaternion swayRot = Quaternion.Euler(0f, 0f, currentSway + wobbleAngle);
        transform.localRotation = initialLocalRotation * swayRot;

        // Apply recoil offset ONLY when active so plant position is never overridden unexpectedly
        if (Mathf.Abs(recoilOffset) > 0.0005f || Mathf.Abs(recoilVelocity) > 0.0005f)
        {
            transform.localPosition = new Vector3(
                initialLocalPosition.x - recoilOffset * 0.12f,
                initialLocalPosition.y,
                initialLocalPosition.z
            );
        }
        else if (isRecoiling)
        {
            isRecoiling = false;
            recoilOffset = 0f;
            recoilVelocity = 0f;
            transform.localPosition = initialLocalPosition;
        }
    }

    private bool isRecoiling = false;

    /// <summary>
    /// Called when Rose shoots a projectile to produce a snappy cannon recoil.
    /// </summary>
    public void TriggerRecoil(float intensity = 0.25f)
    {
        initialLocalPosition = transform.localPosition;
        isRecoiling = true;
        recoilOffset = intensity;
        recoilVelocity = -intensity * 12f;
    }

    /// <summary>
    /// Called when Durian or any unit gets hit to produce a springy jiggle.
    /// </summary>
    public void TriggerWobble(float angle = 12f)
    {
        wobbleAngle = angle;
        wobbleVelocity = angle * 8f;
    }
}
