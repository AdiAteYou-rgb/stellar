/// <summary>
/// Common health and damage contract for 2D grid units (Plants and Zombies).
/// </summary>
public interface IDamageable
{
    int CurrentHealth { get; }
    int MaxHealth { get; }
    bool IsAlive { get; }
    void TakeDamage(int amount);
}
