using System.Collections.Generic;
using UnityEngine;

public class WallZombie : MonoBehaviour
{
    public static readonly List<WallZombie> ActiveZombies = new List<WallZombie>();

    public GridManager gridManager;
    public int row;
    public float moveSpeed = 0.85f; // Walks forward (left) slowly
    public int maxHealth = 100;
    public int currentHealth = 100;

    private float bobTimer = 0f;
    private float baseY;
    private bool triggeredClash = false;

    private void OnEnable()
    {
        if (!ActiveZombies.Contains(this))
        {
            ActiveZombies.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveZombies.Remove(this);
    }

    public void Initialize(GridManager manager, int laneRow, Vector3 startPos)
    {
        gridManager = manager;
        row = laneRow;
        transform.position = startPos;
        baseY = startPos.y;
        currentHealth = maxHealth;
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
        if (!Application.isPlaying || gridManager == null)
        {
            return;
        }

        // Freeze completely when the 2D world is paused (during zoom-in, 3D 1v1 arena, or game over)
        if (gridManager.is2DWorldPaused || gridManager.matchResult != GridManager.MatchResult.InProgress)
        {
            return;
        }

        // Walk slowly forward (to the left along the row)
        bobTimer += Time.deltaTime * 4.5f;
        float newX = transform.position.x - (moveSpeed * Time.deltaTime);
        float bobOffset = Mathf.Abs(Mathf.Sin(bobTimer)) * 0.06f;
        transform.position = new Vector3(newX, baseY + bobOffset, transform.position.z);

        // Slight zombie shambling wobble
        float wobbleZ = Mathf.Sin(bobTimer) * 4.0f;
        transform.rotation = Quaternion.Euler(0f, 0f, wobbleZ);

        // Check if this zombie touches any plant in its row
        if (!triggeredClash)
        {
            PlaceholderPlant touchedPlant = gridManager.GetPlantTouchedByZombie(row, transform.position.x);
            if (touchedPlant != null)
            {
                triggeredClash = true;
                gridManager.TriggerZombiePlantClash(this, touchedPlant);
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
