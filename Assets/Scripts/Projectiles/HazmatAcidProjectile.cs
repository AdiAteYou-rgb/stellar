using UnityEngine;

/// <summary>
/// Corrosive acid glob fired leftward by Hazmat zombies to damage plants in the same lane.
/// </summary>
public class HazmatAcidProjectile : GridProjectileBase
{
    public float minX = -9.0f;

    private void Reset()
    {
        speed = 6.8f;
        damage = 30;
    }

    public void Initialize(GridManager manager, int laneRow, int acidDamage, float wallLeftLimit)
    {
        gridManager = manager;
        row = laneRow;
        damage = acidDamage;
        minX = wallLeftLimit - 1.0f;
    }

    protected override void MoveProjectile()
    {
        transform.position += Vector3.left * (speed * Time.deltaTime);
    }

    protected override void AnimateProjectile()
    {
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 16f) * 10f);
    }

    protected override bool TryHitTarget()
    {
        if (gridManager == null) return false;

        PlaceholderPlant hitPlant = gridManager.GetPlantHitByProjectile(row, transform.position.x, 0.45f);
        if (hitPlant != null)
        {
            gridManager.DamagePlantOnTile(hitPlant, damage);
            return true;
        }
        return false;
    }

    protected override bool IsOutOfBounds() => transform.position.x < minX;
}
