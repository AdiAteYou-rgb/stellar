using UnityEngine;

public class RoseProjectile : MonoBehaviour
{
    public GridManager gridManager;
    public int row;
    public float speed = 7.5f;
    public int damage = 20;
    public float maxX = 9.0f;

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

    private void Update()
    {
        if (gridManager != null && gridManager.is2DWorldPaused)
        {
            return;
        }

        transform.position += Vector3.right * (speed * Time.deltaTime);

        // Check collision with any zombie in the same row
        for (int i = WallZombie.ActiveZombies.Count - 1; i >= 0; i--)
        {
            WallZombie zombie = WallZombie.ActiveZombies[i];
            if (zombie == null || zombie.row != row) continue;

            if (Mathf.Abs(transform.position.x - zombie.transform.position.x) <= 0.45f)
            {
                zombie.TakeDamage(damage);
                Destroy(gameObject);
                return;
            }
        }

        if (transform.position.x > maxX)
        {
            Destroy(gameObject);
        }
    }
}
