using UnityEngine;

/// <summary>
/// Standard shambling zombie variant.
/// </summary>
public class NormalZombie : WallZombie
{
    public override void Initialize(GridManager manager, int laneRow, Vector3 startPos, ZombieType type = ZombieType.Normal)
    {
        base.Initialize(manager, laneRow, startPos, ZombieType.Normal);
    }
}
