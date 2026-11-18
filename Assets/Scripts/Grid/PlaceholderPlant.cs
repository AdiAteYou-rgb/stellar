using UnityEngine;

public enum PlantType
{
    Moonflower = 0,
    Rose = 1,
    Roze = 1, // Backwards-compatible alias for existing references
    Durian = 2
}

/// <summary>
/// Base plant component placed on a tile of the 5x8 wall grid.
/// Concrete plant behaviors are implemented polymorphically by MoonflowerPlant, RosePlant, and DurianPlant.
/// </summary>
public class PlaceholderPlant : GridUnitBase
{
    [Header("Plant Identity & Coordinates")]
    public PlantType plantType = PlantType.Moonflower;
    public int column;

    [Header("Moonflower Settings")]
    public float moonProduceInterval = 15.0f;
    public int moonProduceAmount = 50;

    [Header("Rose (Peashooter) Settings")]
    public float shootInterval = 1.8f;

    protected PlantDefinitionSO definition;
    protected float actionTimer = 0f;

    public virtual void Initialize(GridManager manager, PlantType type, int tileRow, int tileColumn)
    {
        gridManager = manager;
        plantType = type;
        row = tileRow;
        column = tileColumn;

        definition = manager != null ? manager.GetPlantDefinition(type) : PlantDefinitionSO.CreateDefault(type);
        ApplyDefinition(definition);

        currentHealth = maxHealth;
        actionTimer = 0f;
    }

    public void Initialize(int tileRow, int tileColumn)
    {
        Initialize(FindAnyObjectByType<GridManager>(), plantType, tileRow, tileColumn);
    }

    protected virtual void ApplyDefinition(PlantDefinitionSO def)
    {
        if (def == null) return;
        maxHealth = def.maxHealth2D;
        if (plantType == PlantType.Moonflower)
        {
            moonProduceInterval = def.actionInterval2D;
            moonProduceAmount = def.moonProduceAmount;
        }
        else if (plantType == PlantType.Rose)
        {
            shootInterval = def.actionInterval2D;
        }
    }

    protected override void OnDamaged(int amount)
    {
        GetComponent<PlantBobber>()?.TriggerWobble(10f);
    }

    protected override void OnDeath()
    {
        if (gridManager != null)
        {
            gridManager.ClearPlantSlot(row, column, this);
        }
        base.OnDeath();
    }

    private void Update()
    {
        if (IsWorldPaused)
        {
            return;
        }

        OnPlantTick();
    }

    protected virtual void OnPlantTick()
    {
    }
}
