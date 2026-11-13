using System.Collections.Generic;
using UnityEngine;

public enum ZombieType
{
    Normal = 0,
    Buckethead = 1,
    Bunny = 2
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

    private float bobTimer = 0f;
    private float baseY;
    private bool triggeredClash = false;

    private Transform legL;
    private Transform legR;
    private Transform armL;
    private Transform armR;
    private Transform head;

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
        if (legL == null) legL = transform.Find("Leg_L");
        if (legR == null) legR = transform.Find("Leg_R");
        if (armL == null) armL = transform.Find("Arm_L");
        if (armR == null) armR = transform.Find("Arm_R");
        if (head == null) head = transform.Find("Head");
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
        if (!Application.isPlaying || gridManager == null)
        {
            return;
        }

        // Freeze completely when the 2D world is paused (during zoom-in, 3D 1v1 arena, or game over)
        if (gridManager.is2DWorldPaused || gridManager.matchResult != GridManager.MatchResult.InProgress)
        {
            return;
        }

        // Walk or hop forward (to the left along the row)
        float bobFreq = (zombieType == ZombieType.Bunny) ? 8.5f : ((zombieType == ZombieType.Buckethead) ? 4.2f : 5.2f);
        float bobAmp = (zombieType == ZombieType.Bunny) ? 0.16f : 0.08f;
        bobTimer += Time.deltaTime * bobFreq;

        float newX = transform.position.x - (moveSpeed * Time.deltaTime);
        float bobOffset = Mathf.Abs(Mathf.Sin(bobTimer)) * bobAmp;
        transform.position = new Vector3(newX, baseY + bobOffset, transform.position.z);

        // Shambling or hopping torso wobble
        float wobbleZ = Mathf.Sin(bobTimer) * ((zombieType == ZombieType.Bunny) ? 6.5f : 4.5f);
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
}
