/// <summary>
/// Durian plant: high-HP armored tank defender that absorbs heavy zombie pressure.
/// </summary>
public class DurianPlant : PlaceholderPlant
{
    public override void Initialize(GridManager manager, PlantType type, int tileRow, int tileColumn)
    {
        base.Initialize(manager, PlantType.Durian, tileRow, tileColumn);
    }
}
