C#

using UnityEngine;

public class GridManager : MonoBehaviour
{
    public int rows = 5;
    public int columns = 9;
    public float tileSize = 1.0f;

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Vector3 position = new Vector3(x * tileSize, y * tileSize, 0);
                Gizmos.DrawWireCube(position, new Vector3(tileSize, tileSize, 0));
            }
        }
    }
}