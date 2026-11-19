using UnityEngine;

/// <summary>
/// Ranged Hazmat Spitter zombie variant that launches corrosive acid globs at plants ahead
/// and animates its biohazard tanks and sprayer glow.
/// </summary>
public class HazmatZombie : WallZombie
{
    [Header("Hazmat Ranged Settings")]
    public float hazmatSpitInterval = 2.2f;
    public float hazmatSpitRange = 7.0f;
    public int hazmatAcidDamage = 30;

    private float hazmatSpitTimer = 0f;
    private float hazmatSpitAnimTimer = 0f;

    private Transform acidTankGlow;
    private Transform nozzleGlow;
    private SpriteRenderer acidTankGlowRenderer;
    private SpriteRenderer nozzleGlowRenderer;

    public override void Initialize(GridManager manager, int laneRow, Vector3 startPos, ZombieType type = ZombieType.Hazmat)
    {
        hazmatSpitTimer = 1.2f; // Ready to spit shortly after spawning
        hazmatSpitAnimTimer = 0f;
        base.Initialize(manager, laneRow, startPos, ZombieType.Hazmat);
    }

    protected override void OnApplyDefinition(ZombieDefinitionSO def)
    {
        base.OnApplyDefinition(def);
        if (def == null) return;
        hazmatSpitInterval = def.spitInterval2D;
        hazmatSpitRange = def.spitRange2D;
        hazmatAcidDamage = def.spitAcidDamage2D;
    }

    protected override void CacheLimbs()
    {
        base.CacheLimbs();
        if (acidTankGlow == null) acidTankGlow = transform.Find("AcidTankGlow");
        if (nozzleGlow == null) nozzleGlow = transform.Find("NozzleGlow");
        if (acidTankGlow != null && acidTankGlowRenderer == null)
        {
            acidTankGlowRenderer = acidTankGlow.GetComponent<SpriteRenderer>();
        }
        if (nozzleGlow != null && nozzleGlowRenderer == null)
        {
            nozzleGlowRenderer = nozzleGlow.GetComponent<SpriteRenderer>();
        }
    }

    protected override void OnZombieSpecialUpdate()
    {
        if (!triggeredClash && gridManager.HasPlantAheadInRow(row, transform.position.x, hazmatSpitRange))
        {
            hazmatSpitTimer += Time.deltaTime;
            if (hazmatSpitTimer >= hazmatSpitInterval)
            {
                hazmatSpitTimer = 0f;
                hazmatSpitAnimTimer = 0.42f;
                gridManager.SpawnHazmatAcidProjectile(row, transform.position + new Vector3(-0.55f, 0.18f, -0.1f), hazmatAcidDamage);
            }
        }

        if (hazmatSpitAnimTimer > 0f)
        {
            hazmatSpitAnimTimer = Mathf.Max(0f, hazmatSpitAnimTimer - Time.deltaTime);
        }
    }

    protected override float GetEffectiveMoveSpeed()
    {
        return hazmatSpitAnimTimer > 0f ? moveSpeed * 0.15f : moveSpeed;
    }

    protected override void ApplyVariantVisuals(float currentBobTimer, ref float wobbleZ)
    {
        if (baseLocalScale.x <= 0.01f && transform.localScale.x > 0.01f)
        {
            baseLocalScale = transform.localScale;
        }

        float scaleMulX = 1.0f;
        float scaleMulY = 1.0f;
        if (hazmatSpitAnimTimer > 0f)
        {
            if (hazmatSpitAnimTimer > 0.24f)
            {
                wobbleZ = -11f;
                scaleMulX = 1.10f;
                scaleMulY = 0.92f;
            }
            else
            {
                wobbleZ = 14f;
                scaleMulX = 1.14f;
                scaleMulY = 0.90f;
            }
        }
        else
        {
            float breath = 1.0f + Mathf.Sin(currentBobTimer * 0.8f) * 0.035f;
            scaleMulX = breath;
            scaleMulY = 2.0f - breath;
        }

        if (baseLocalScale.x > 0.01f)
        {
            transform.localScale = new Vector3(baseLocalScale.x * scaleMulX, baseLocalScale.y * scaleMulY, baseLocalScale.z);
        }

        UpdateHazmatGlows(currentBobTimer);
    }

    private void UpdateHazmatGlows(float currentBobTimer)
    {
        bool spitting = hazmatSpitAnimTimer > 0f;
        float pulse = 1.0f + Mathf.Sin(currentBobTimer * 1.4f) * 0.14f;
        float alpha = spitting ? 0.95f : (0.55f + 0.25f * Mathf.Sin(currentBobTimer * 1.4f));

        if (acidTankGlow != null)
        {
            float tankScale = (spitting ? 0.34f : 0.28f) * pulse;
            acidTankGlow.localScale = new Vector3(tankScale, tankScale, 1.0f);
            if (acidTankGlowRenderer != null)
            {
                acidTankGlowRenderer.color = new Color(0.55f, 1.0f, 0.35f, alpha);
            }
        }

        if (nozzleGlow != null)
        {
            float nozzleScale = (spitting ? 0.32f : 0.20f) * pulse;
            nozzleGlow.localScale = new Vector3(nozzleScale, nozzleScale, 1.0f);
            if (nozzleGlowRenderer != null)
            {
                nozzleGlowRenderer.color = new Color(0.65f, 1.0f, 0.35f, spitting ? 1.0f : alpha * 0.8f);
            }
        }
    }
}
