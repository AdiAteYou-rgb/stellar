using UnityEngine;

/// <summary>
/// Placeholder plant component that does nothing for now.
/// Placed on a tile of the 5x8 wall grid.
/// </summary>
public class PlaceholderPlant : MonoBehaviour
{
    [Header("Grid Coordinates")]
    public int row;
    public int column;

    public void Initialize(int tileRow, int tileColumn)
    {
        row = tileRow;
        column = tileColumn;
    }

    // Placeholder: does nothing yet.
    private void Update()
    {
    }
}
