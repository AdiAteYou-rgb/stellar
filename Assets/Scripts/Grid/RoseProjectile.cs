using UnityEngine;

/// <summary>
/// Ruby thorn projectile fired rightward by Rose plants to damage zombies in the same lane.
/// </summary>
public class RoseProjectile : GridProjectileBase
{
    public float maxX = 9.0f;

    private void Reset()
    {
        speed = 7.5f;
        damage = 20;
    }

    public void Initialize(GridManager manager, int laneRow, float wallRightLimit)
    {
        gridManager = manager;
        row = laneRow;
        maxX = wallRightLimit + 1.0f;
    }

    public void Initialize(float wallRightLimit)
    {
        maxX = wallRightLimit + 1.0f;
    }

    protected override void MoveProjectile()
    {
        transform.position += Vector3.right * (speed * Time.deltaTime);
    }

    protected override void AnimateProjectile()
    {
        transform.Rotate(0f, 0f, 540f * Time.deltaTime, Space.Self);
    }

    protected override bool TryHitTarget()
    {
        for (int i = WallZombie.ActiveZombies.Count - 1; i >= 0; i--)
        {
            WallZombie zombie = WallZombie.ActiveZombies[i];
            if (zombie == null || zombie.row != row) continue;

            if (Mathf.Abs(transform.position.x - zombie.transform.position.x) <= 0.45f)
            {
                zombie.TakeDamage(damage);
                return true;
            }
        }
        return false;
    }

    protected override bool IsOutOfBounds() => transform.position.x > maxX;
}
