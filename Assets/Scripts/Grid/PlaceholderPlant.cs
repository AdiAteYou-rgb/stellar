using UnityEngine;

public enum PlantType
{
    Moonflower = 0,
    Rose = 1,
    Roze = 1, // Backwards-compatible alias for existing references
    Durian = 2
}

/// <summary>
/// Plant component placed on a tile of the 5x8 wall grid.
/// Supports Moonflower (produces 50 moons every 15s), Rose (shoots thorn darts when a zombie is in its lane), and Durian (high-HP tank).
/// </summary>
public class PlaceholderPlant : MonoBehaviour
{
    [Header("Plant Identity & Coordinates")]
    public PlantType plantType = PlantType.Moonflower;
    public int row;
    public int column;

    [Header("Stats")]
    public int maxHealth = 100;
    public int currentHealth = 100;

    [Header("Moonflower Settings")]
    public float moonProduceInterval = 15.0f;
    public int moonProduceAmount = 50;

    [Header("Rose (Peashooter) Settings")]
    public float shootInterval = 1.8f;

    private GridManager gridManager;
    private float actionTimer = 0f;

    public void Initialize(GridManager manager, PlantType type, int tileRow, int tileColumn)
    {
        gridManager = manager;
        plantType = type;
        row = tileRow;
        column = tileColumn;

        switch (plantType)
        {
            case PlantType.Moonflower:
                maxHealth = 100;
                break;
            case PlantType.Rose:
                maxHealth = 100;
                break;
            case PlantType.Durian:
                maxHealth = 400; // Tanky like Wall-nut
                break;
        }

        currentHealth = maxHealth;
        actionTimer = 0f;
    }

    public void Initialize(int tileRow, int tileColumn)
    {
        Initialize(FindFirstObjectByType<GridManager>(), PlantType.Moonflower, tileRow, tileColumn);
    }

    private void Update()
    {
        if (!Application.isPlaying || gridManager == null)
        {
            return;
        }

        // Pause completely when a zombie touches a plant (zoom-in / 3D 1v1 arena)
        if (gridManager.is2DWorldPaused)
        {
            return;
        }

        if (plantType == PlantType.Moonflower)
        {
            actionTimer += Time.deltaTime;
            if (actionTimer >= moonProduceInterval)
            {
                actionTimer = 0f;
                gridManager.SpawnMoonFromFlower(transform.position, moonProduceAmount);
                GetComponent<PlantBobber>()?.TriggerWobble(10f);
            }
        }
        else if (plantType == PlantType.Rose)
        {
            // Rose ONLY shoots when there is a zombie in the same row ahead of it!
            if (gridManager.HasZombieInRow(row, transform.position.x - 0.25f))
            {
                actionTimer += Time.deltaTime;
                if (actionTimer >= shootInterval)
                {
                    actionTimer = 0f;
                    gridManager.SpawnRoseProjectile(row, transform.position + new Vector3(0.48f, 0.54f, -0.1f));
                    GetComponent<PlantBobber>()?.TriggerRecoil(0.25f);
                }
            }
            else
            {
                // Keep ready to shoot quickly as soon as a zombie enters the lane
                actionTimer = Mathf.Min(actionTimer + Time.deltaTime, shootInterval * 0.85f);
            }
        }
    }
}
