using System.Collections.Generic;
using UnityEngine;

public enum ZombieType
{
    Normal = 0,
    Buckethead = 1,
    Bunny = 2,
    Hazmat = 3
}

/// <summary>
/// Base zombie component marching leftward along a 2D wall lane.
/// Variant stats and special abilities are defined via ZombieDefinitionSO and polymorphic subclasses.
/// </summary>
public class WallZombie : GridUnitBase
{
    public static readonly List<WallZombie> ActiveZombies = new List<WallZombie>();

    public ZombieType zombieType = ZombieType.Normal;
    public float moveSpeed = 0.58f;

    protected float bobTimer = 0f;
    protected float bobFreq = 5.2f;
    protected float bobAmp = 0.08f;
    protected float baseY;
    protected bool triggeredClash = false;
    protected Vector3 baseLocalScale;
    protected ZombieDefinitionSO definition;

    private Transform legL;
    private Transform legR;
    private Transform armL;
    private Transform armR;
    private Transform head;

    protected virtual void OnEnable()
    {
        if (!ActiveZombies.Contains(this))
        {
            ActiveZombies.Add(this);
        }
        CacheLimbs();
    }

    protected virtual void OnDisable()
    {
        ActiveZombies.Remove(this);
    }

    protected virtual void CacheLimbs()
    {
        if (baseLocalScale.x <= 0.01f && transform.localScale.x > 0.01f)
        {
            baseLocalScale = transform.localScale;
        }
        if (legL == null) legL = transform.Find("Leg_L");
        if (legR == null) legR = transform.Find("Leg_R");
        if (armL == null) armL = transform.Find("Arm_L");
        if (armR == null) armR = transform.Find("Arm_R");
        if (head == null) head = transform.Find("Head");
    }

    public virtual void Initialize(GridManager manager, int laneRow, Vector3 startPos, ZombieType type = ZombieType.Normal)
    {
        gridManager = manager;
        zombieType = type;
        row = laneRow;
        transform.position = startPos;
        baseY = startPos.y;

        definition = manager != null ? manager.GetZombieDefinition(type) : ZombieDefinitionSO.CreateDefault(type);
        OnApplyDefinition(definition);

        currentHealth = maxHealth;
        triggeredClash = false;
        CacheLimbs();
    }

    protected virtual void OnApplyDefinition(ZombieDefinitionSO def)
    {
        if (def == null) return;
        maxHealth = def.maxHealth2D;
        moveSpeed = def.moveSpeed2D;
        bobFreq = def.bobFrequency2D;
        bobAmp = def.bobAmplitude2D;
    }

    public void MarkClashResolved()
    {
        triggeredClash = true;
    }

    public void ResetForNextClash()
    {
        triggeredClash = false;
    }

    protected virtual void OnZombieSpecialUpdate() { }

    protected virtual float GetEffectiveMoveSpeed() => moveSpeed;

    protected virtual float GetWobbleAmplitude() => zombieType == ZombieType.Bunny ? 6.5f : 4.5f;

    protected virtual void ApplyVariantVisuals(float currentBobTimer, ref float wobbleZ) { }

    private void Update()
    {
        if (!gameObject.activeInHierarchy || IsWorldPaused)
        {
            return;
        }

        OnZombieSpecialUpdate();

        float effectiveSpeed = GetEffectiveMoveSpeed();
        bobTimer += Time.deltaTime * bobFreq;

        float newX = transform.position.x - (effectiveSpeed * Time.deltaTime);
        float bobOffset = Mathf.Abs(Mathf.Sin(bobTimer)) * bobAmp;
        transform.position = new Vector3(newX, baseY + bobOffset, transform.position.z);

        float wobbleZ = Mathf.Sin(bobTimer) * GetWobbleAmplitude();
        ApplyVariantVisuals(bobTimer, ref wobbleZ);
        transform.rotation = Quaternion.Euler(0f, 0f, wobbleZ);

        AnimateLimbs();

        if (!triggeredClash)
        {
            PlaceholderPlant touchedPlant = gridManager.GetPlantTouchedByZombie(row, transform.position.x);
            if (touchedPlant != null)
            {
                triggeredClash = true;
                gridManager.TriggerZombiePlantClash(this, touchedPlant, touchedPlant.plantType, notifyNetwork: true);
                return;
            }
        }

        if (transform.position.x <= gridManager.DefenseLineX)
        {
            gridManager.TriggerZombiesWin(notifyNetwork: true);
            return;
        }

        float leftLimit = gridManager.DefenseLineX - 1.0f;
        if (transform.position.x < leftLimit)
        {
            Destroy(gameObject);
        }
    }

    private void AnimateLimbs()
    {
        if (legL != null && legR != null)
        {
            float legSwing = Mathf.Sin(bobTimer) * 28f;
            legL.localRotation = Quaternion.Euler(0f, 0f, legSwing);
            legR.localRotation = Quaternion.Euler(0f, 0f, -legSwing);
        }
        if (armL != null && armR != null)
        {
            float armSwing = Mathf.Cos(bobTimer * 0.6f) * 12f;
            armL.localRotation = Quaternion.Euler(0f, 0f, armSwing);
            armR.localRotation = Quaternion.Euler(0f, 0f, -armSwing);
        }
        if (head != null)
        {
            float headBob = Mathf.Sin(bobTimer * 0.5f) * 7f;
            head.localRotation = Quaternion.Euler(0f, 0f, headBob);
        }
    }
}
