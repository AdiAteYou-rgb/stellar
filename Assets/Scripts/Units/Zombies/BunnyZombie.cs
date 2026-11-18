using UnityEngine;

/// <summary>
/// Fast hopping Bunny Speed zombie variant.
/// </summary>
public class BunnyZombie : WallZombie
{
    public override void Initialize(GridManager manager, int laneRow, Vector3 startPos, ZombieType type = ZombieType.Bunny)
    {
        base.Initialize(manager, laneRow, startPos, ZombieType.Bunny);
    }

    protected override float GetWobbleAmplitude() => 6.5f;
}
