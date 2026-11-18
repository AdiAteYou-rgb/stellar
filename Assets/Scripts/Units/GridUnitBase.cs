using UnityEngine;

/// <summary>
/// Abstract base class for 2D grid units (Plants and Zombies), encapsulating shared health,
/// lane identity, pause guards, and damage/death lifecycle hooks.
/// </summary>
public abstract class GridUnitBase : MonoBehaviour, IDamageable
{
    [HideInInspector] public GridManager gridManager;
    public int row;

    [Header("Stats")]
    public int maxHealth = 100;
    public int currentHealth = 100;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsAlive => currentHealth > 0;

    protected bool IsWorldPaused =>
        !Application.isPlaying
        || gridManager == null
        || gridManager.is2DWorldPaused
        || gridManager.matchResult != GridManager.MatchResult.InProgress;

    public virtual void TakeDamage(int amount)
    {
        if (amount <= 0 || currentHealth <= 0) return;

        currentHealth -= amount;
        OnDamaged(amount);

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            OnDeath();
        }
    }

    protected virtual void OnDamaged(int amount) { }

    protected virtual void OnDeath()
    {
        Destroy(gameObject);
    }
}
