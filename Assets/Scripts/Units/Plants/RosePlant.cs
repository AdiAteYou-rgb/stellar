using UnityEngine;

/// <summary>
/// Rose plant: fires thorn projectiles whenever a zombie is detected in its lane.
/// </summary>
public class RosePlant : PlaceholderPlant
{
    public override void Initialize(GridManager manager, PlantType type, int tileRow, int tileColumn)
    {
        base.Initialize(manager, PlantType.Rose, tileRow, tileColumn);
    }

    protected override void OnPlantTick()
    {
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
