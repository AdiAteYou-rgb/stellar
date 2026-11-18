using UnityEngine;

/// <summary>
/// Heavy armored Buckethead zombie variant with 5x health and slower march pace.
/// </summary>
public class BucketheadZombie : WallZombie
{
    public override void Initialize(GridManager manager, int laneRow, Vector3 startPos, ZombieType type = ZombieType.Buckethead)
    {
        base.Initialize(manager, laneRow, startPos, ZombieType.Buckethead);
    }
}
