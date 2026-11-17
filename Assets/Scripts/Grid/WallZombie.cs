using System.Collections.Generic;
using UnityEngine;

public enum ZombieType
{
    Normal = 0,
    Buckethead = 1,
    Bunny = 2,
    Hazmat = 3
}

public class WallZombie : MonoBehaviour
{
    public static readonly List<WallZombie> ActiveZombies = new List<WallZombie>();

    public GridManager gridManager;
    public ZombieType zombieType = ZombieType.Normal;
    public int row;
    public float moveSpeed = 0.58f; // Walks forward (left) slowly at classic shambling pace
    public int maxHealth = 100;
    public int currentHealth = 100;

    [Header("Hazmat Ranged Settings")]
    public float hazmatSpitInterval = 2.2f;
    public float hazmatSpitRange = 7.0f;
    public int hazmatAcidDamage = 30;

    private float bobTimer = 0f;
    private float baseY;
    private bool triggeredClash = false;
    private float hazmatSpitTimer = 0f;
    private float hazmatSpitAnimTimer = 0f;
    private Vector3 baseLocalScale;

    private Transform legL;
    private Transform legR;
    private Transform armL;
    private Transform armR;
    private Transform head;
    private Transform acidTankGlow;
    private Transform nozzleGlow;
    private SpriteRenderer acidTankGlowRenderer;
    private SpriteRenderer nozzleGlowRenderer;

    private void OnEnable()
    {
        if (!ActiveZombies.Contains(this))
        {
            ActiveZombies.Add(this);
        }
        CacheLimbs();
    }

    private void CacheLimbs()
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

    private void OnDisable()
    {
        ActiveZombies.Remove(this);
    }

    public void Initialize(GridManager manager, int laneRow, Vector3 startPos, ZombieType type = ZombieType.Normal)
    {
        gridManager = manager;
        zombieType = type;
        row = laneRow;
        transform.position = startPos;
        baseY = startPos.y;
        hazmatSpitTimer = 0f;
        hazmatSpitAnimTimer = 0f;

        switch (zombieType)
        {
            case ZombieType.Buckethead:
                maxHealth = 500; // 5x normal zombie health
                moveSpeed = 0.46f; // Slightly slower heavy tank pace
                break;
            case ZombieType.Bunny:
                maxHealth = 80;
                moveSpeed = 1.08f; // Fast hopping speed zombie
                break;
            case ZombieType.Hazmat:
                maxHealth = 125;
                moveSpeed = 0.50f;
                hazmatSpitTimer = 1.2f; // Ready to spit shortly after spawning
                break;
            case ZombieType.Normal:
            default:
                maxHealth = 100;
                moveSpeed = 0.58f;
                break;
        }

        currentHealth = maxHealth;
        triggeredClash = false;
        CacheLimbs();
    }

    public void MarkClashResolved()
    {
        triggeredClash = true;
    }

    public void ResetForNextClash()
    {
        triggeredClash = false;
    }

    public void TakeDamage(int amount)
    {
        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || !gameObject.activeInHierarchy || gridManager == null)
        {
            return;
        }

        // Freeze completely when the 2D world is paused (during zoom-in, 3D 1v1 arena, or game over)
        if (gridManager.is2DWorldPaused || gridManager.matchResult != GridManager.MatchResult.InProgress)
        {
            return;
        }

        // Hazmat 2D Ranged Acid Spit check & animation timer
        float effectiveSpeed = moveSpeed;
        if (zombieType == ZombieType.Hazmat)
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
                effectiveSpeed *= 0.15f;
            }
        }

        // Walk or hop forward (to the left along the row)
        float bobFreq = (zombieType == ZombieType.Bunny)
            ? 8.5f
            : ((zombieType == ZombieType.Buckethead) ? 4.2f : ((zombieType == ZombieType.Hazmat) ? 4.8f : 5.2f));
        float bobAmp = (zombieType == ZombieType.Bunny) ? 0.16f : ((zombieType == ZombieType.Hazmat) ? 0.09f : 0.08f);
        bobTimer += Time.deltaTime * bobFreq;

        float newX = transform.position.x - (effectiveSpeed * Time.deltaTime);
        float bobOffset = Mathf.Abs(Mathf.Sin(bobTimer)) * bobAmp;
        transform.position = new Vector3(newX, baseY + bobOffset, transform.position.z);

        // Shambling or hopping torso wobble
        float wobbleZ = Mathf.Sin(bobTimer) * ((zombieType == ZombieType.Bunny) ? 6.5f : 4.5f);

        if (zombieType == ZombieType.Hazmat)
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
                    // Wind-up lean back & chest inflation
                    wobbleZ = -11f;
                    scaleMulX = 1.10f;
                    scaleMulY = 0.92f;
                }
                else
                {
                    // Forward spit lunge & stretch
                    wobbleZ = 14f;
                    scaleMulX = 1.14f;
                    scaleMulY = 0.90f;
                }
            }
            else
            {
                // Subtle respirator breathing squash-and-stretch
                float breath = 1.0f + Mathf.Sin(bobTimer * 0.8f) * 0.035f;
                scaleMulX = breath;
                scaleMulY = 2.0f - breath;
            }

            if (baseLocalScale.x > 0.01f)
            {
                transform.localScale = new Vector3(baseLocalScale.x * scaleMulX, baseLocalScale.y * scaleMulY, baseLocalScale.z);
            }

            UpdateHazmatGlows();
        }

        transform.rotation = Quaternion.Euler(0f, 0f, wobbleZ);

        // Animate limbs if found
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

        // Check if this zombie touches any plant in its row
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

        // Check if zombie crossed the defense line (the other line of the board) -> Zombies Win!
        if (transform.position.x <= gridManager.DefenseLineX)
        {
            gridManager.TriggerZombiesWin(notifyNetwork: true);
            return;
        }

        // Despawn if it walks completely past the left edge of the wall
        float leftLimit = gridManager.DefenseLineX - 1.0f;
        if (transform.position.x < leftLimit)
        {
            Destroy(gameObject);
        }
    }

    private void UpdateHazmatGlows()
    {
        bool spitting = hazmatSpitAnimTimer > 0f;
        float pulse = 1.0f + Mathf.Sin(bobTimer * 1.4f) * 0.14f;
        float alpha = spitting ? 0.95f : (0.55f + 0.25f * Mathf.Sin(bobTimer * 1.4f));

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

public class HazmatAcidProjectile : MonoBehaviour
{
    public GridManager gridManager;
    public int row;
    public float speed = 6.8f;
    public int damage = 30;
    public float minX = -9.0f;

    public void Initialize(GridManager manager, int laneRow, int acidDamage, float wallLeftLimit)
    {
        gridManager = manager;
        row = laneRow;
        damage = acidDamage;
        minX = wallLeftLimit - 1.0f;
    }

    private void Update()
    {
        if (gridManager != null && (gridManager.is2DWorldPaused || gridManager.matchResult != GridManager.MatchResult.InProgress))
        {
            return;
        }

        transform.position += Vector3.left * (speed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 16f) * 10f);

        if (gridManager != null)
        {
            PlaceholderPlant hitPlant = gridManager.GetPlantHitByProjectile(row, transform.position.x, 0.45f);
            if (hitPlant != null)
            {
                gridManager.DamagePlantOnTile(hitPlant, damage);
                Destroy(gameObject);
                return;
            }
        }

        if (transform.position.x < minX)
        {
            Destroy(gameObject);
        }
    }
}
