using UnityEngine;

/// <summary>
/// Moonflower plant: periodically produces collectible Moons on the 2D defense wall.
/// </summary>
public class MoonflowerPlant : PlaceholderPlant
{
    public override void Initialize(GridManager manager, PlantType type, int tileRow, int tileColumn)
    {
        base.Initialize(manager, PlantType.Moonflower, tileRow, tileColumn);
    }

    protected override void OnPlantTick()
    {
        actionTimer += Time.deltaTime;
        if (actionTimer >= moonProduceInterval)
        {
            actionTimer = 0f;
            gridManager.SpawnMoonFromFlower(transform.position, moonProduceAmount);
            GetComponent<PlantBobber>()?.TriggerWobble(10f);
        }
    }
}
