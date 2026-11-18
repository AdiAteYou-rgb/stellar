using UnityEngine;

/// <summary>
/// Abstract base class for 2D lane projectiles, sharing world-pause guards,
/// movement, animation, hit detection, and out-of-bounds cleanup lifecycle.
/// </summary>
public abstract class GridProjectileBase : MonoBehaviour
{
    public GridManager gridManager;
    public int row;
    public float speed = 7.0f;
    public int damage = 20;

    protected virtual void Update()
    {
        if (gridManager != null && (gridManager.is2DWorldPaused || gridManager.matchResult != GridManager.MatchResult.InProgress))
        {
            return;
        }

        MoveProjectile();
        AnimateProjectile();

        if (TryHitTarget())
        {
            Destroy(gameObject);
            return;
        }

        if (IsOutOfBounds())
        {
            Destroy(gameObject);
        }
    }

    protected abstract void MoveProjectile();
    protected virtual void AnimateProjectile() { }
    protected abstract bool TryHitTarget();
    protected abstract bool IsOutOfBounds();
}
