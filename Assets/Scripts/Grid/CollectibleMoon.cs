using System.Collections.Generic;
using UnityEngine;

public class CollectibleMoon : MonoBehaviour
{
    public static readonly List<CollectibleMoon> ActiveMoons = new List<CollectibleMoon>();

    public GridManager gridManager;
    public int networkId = 0;
    public int moonValue = 50;

    [Header("Movement")]
    public bool isFallingFromSky = false;
    public Vector3 fallTargetPosition;
    public float fallSpeed = 1.65f;
    public float travelToBankSpeed = 24.0f;

    private bool isCollected = false;
    private float spawnTime;
    private Vector3 popStartPos;
    private Vector3 popEndPos;
    private bool isPopping = false;
    private Vector3 baseScale;
    private float floatSeed;

    public bool IsCollected => isCollected;

    private void Awake()
    {
        baseScale = transform.localScale;
        if (baseScale.x < 0.01f) baseScale = Vector3.one * 0.9f;
        floatSeed = Random.Range(0f, 100f);
    }

    private void OnEnable()
    {
        if (!ActiveMoons.Contains(this))
        {
            ActiveMoons.Add(this);
        }
        spawnTime = Time.time;
        if (baseScale.x < 0.01f) baseScale = transform.localScale;
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
        if (baseScale.x < 0.01f) baseScale = transform.localScale;

        // Gentle celestial hovering sway and breathing pulse
        float t = Time.time + floatSeed;
        float swayZ = Mathf.Sin(t * 2.2f) * 6.0f;
        transform.rotation = Quaternion.Euler(0f, 0f, swayZ);

        float pulse = 1.0f + Mathf.Sin(t * 3.2f) * 0.065f;
        transform.localScale = baseScale * pulse;

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
            if (dist < 0.25f)
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
            float step = fallSpeed * Time.deltaTime;
            Vector3 target = fallTargetPosition;
            // Add subtle floating drift
            float driftX = Mathf.Sin(t * 2.5f) * 0.04f;
            target.x += driftX;

            transform.position = Vector3.MoveTowards(transform.position, target, step);
            if (Vector3.Distance(transform.position, fallTargetPosition) < 0.05f)
            {
                isFallingFromSky = false;
                transform.position = fallTargetPosition;
            }
        }
        else if (isPopping)
        {
            float pt = Mathf.Clamp01((Time.time - spawnTime) / 0.45f);
            float arc = Mathf.Sin(pt * Mathf.PI) * 0.45f;
            transform.position = Vector3.Lerp(popStartPos, popEndPos, pt) + new Vector3(0f, arc, 0f);
            if (pt >= 1.0f)
            {
                isPopping = false;
            }
        }
    }
}
