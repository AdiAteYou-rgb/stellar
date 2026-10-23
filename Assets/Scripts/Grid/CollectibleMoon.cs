using System.Collections.Generic;
using UnityEngine;

public class CollectibleMoon : MonoBehaviour
{
    public static readonly List<CollectibleMoon> ActiveMoons = new List<CollectibleMoon>();

    public GridManager gridManager;
    public int moonValue = 50;

    [Header("Movement")]
    public bool isFallingFromSky = false;
    public Vector3 fallTargetPosition;
    public float fallSpeed = 2.2f;
    public float travelToBankSpeed = 22.0f;

    private bool isCollected = false;
    private float spawnTime;
    private Vector3 popStartPos;
    private Vector3 popEndPos;
    private bool isPopping = false;

    public bool IsCollected => isCollected;

    private void OnEnable()
    {
        if (!ActiveMoons.Contains(this))
        {
            ActiveMoons.Add(this);
        }
        spawnTime = Time.time;
    }

    private void OnDisable()
    {
        ActiveMoons.Remove(this);
    }

    public void SetupSkyFall(GridManager manager, Vector3 startPos, Vector3 landingPos, int value = 50)
    {
        gridManager = manager;
        moonValue = value;
        transform.position = startPos;
        fallTargetPosition = landingPos;
        isFallingFromSky = true;
        isPopping = false;
    }

    public void SetupMoonflowerDrop(GridManager manager, Vector3 flowerPos, int value = 50)
    {
        gridManager = manager;
        moonValue = value;
        popStartPos = flowerPos;
        float offsetX = Random.Range(-0.45f, 0.45f);
        float offsetY = Random.Range(-0.35f, 0.25f);
        popEndPos = flowerPos + new Vector3(offsetX, offsetY, -0.2f);
        transform.position = popStartPos;
        isFallingFromSky = false;
        isPopping = true;
        spawnTime = Time.time;
    }

    public void Collect()
    {
        if (isCollected)
        {
            return;
        }
        isCollected = true;
        isFallingFromSky = false;
        isPopping = false;
    }

    private void Update()
    {
        // Gentle spin and pulse
        transform.Rotate(0f, 0f, 45f * Time.deltaTime, Space.Self);

        if (isCollected)
        {
            Vector3 bankWorldPos = gridManager != null
                ? gridManager.GetMoonBankWorldPosition()
                : transform.position + new Vector3(-6f, 4f, 0f);

            transform.position = Vector3.MoveTowards(
                transform.position,
                bankWorldPos,
                travelToBankSpeed * Time.deltaTime
            );

            // Shrink slightly as it enters the bank
            float dist = Vector3.Distance(transform.position, bankWorldPos);
            if (dist < 0.2f)
            {
                if (gridManager != null)
                {
                    gridManager.AddMoons(moonValue);
                }
                Destroy(gameObject);
            }
            return;
        }

        if (gridManager != null && gridManager.is2DWorldPaused && !isCollected)
        {
            return;
        }

        if (isFallingFromSky)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                fallTargetPosition,
                fallSpeed * Time.deltaTime
            );
            if (Vector3.Distance(transform.position, fallTargetPosition) < 0.02f)
            {
                isFallingFromSky = false;
            }
        }
        else if (isPopping)
        {
            float t = Mathf.Clamp01((Time.time - spawnTime) / 0.45f);
            float arc = Mathf.Sin(t * Mathf.PI) * 0.45f;
            transform.position = Vector3.Lerp(popStartPos, popEndPos, t) + new Vector3(0f, arc, 0f);
            if (t >= 1.0f)
            {
                isPopping = false;
            }
        }
    }
}
