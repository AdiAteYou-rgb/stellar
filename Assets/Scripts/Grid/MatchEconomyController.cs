using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages Moon/Brain currency income, seed packet cooldown timers,
/// sky/flower moon spawning, and cluster moon collection.
/// </summary>
public class MatchEconomyController : MonoBehaviour
{
    private GridManager grid;
    private readonly float[] plantCooldownTimers = new float[3];
    private readonly float[] zombieCooldownTimers = new float[4];

    private float skyMoonTimer = 0f;
    private float passiveBrainTimer = 0f;
    private int nextMoonId = 1;

    public void Initialize(GridManager manager)
    {
        grid = manager;
    }

    public void TickEconomy(float deltaTime)
    {
        if (grid == null) return;

        for (int i = 0; i < plantCooldownTimers.Length; i++)
        {
            if (plantCooldownTimers[i] > 0f)
            {
                plantCooldownTimers[i] = Mathf.Max(0f, plantCooldownTimers[i] - deltaTime);
            }
        }

        for (int i = 0; i < zombieCooldownTimers.Length; i++)
        {
            if (zombieCooldownTimers[i] > 0f)
            {
                zombieCooldownTimers[i] = Mathf.Max(0f, zombieCooldownTimers[i] - deltaTime);
            }
        }

        skyMoonTimer += deltaTime;
        if (skyMoonTimer >= grid.skyMoonInterval && grid.IsMoonAuthority)
        {
            skyMoonTimer = 0f;
            SpawnSkyFallingMoon();
        }

        passiveBrainTimer += deltaTime;
        if (passiveBrainTimer >= grid.passiveBrainInterval)
        {
            passiveBrainTimer = 0f;
            grid.currentBrains += grid.passiveBrainAmount;
        }
    }

    public void ResetEconomy()
    {
        if (grid != null)
        {
            GameConfigSO cfg = grid.GameConfig;
            grid.currentMoons = cfg != null ? cfg.startingMoons : 100;
            grid.currentBrains = cfg != null ? cfg.startingBrains : 150;
        }

        skyMoonTimer = 0f;
        passiveBrainTimer = 0f;
        for (int i = 0; i < plantCooldownTimers.Length; i++) plantCooldownTimers[i] = 0f;
        for (int i = 0; i < zombieCooldownTimers.Length; i++) zombieCooldownTimers[i] = 0f;
    }

    public int GetPlantCost(PlantType type)
    {
        if (grid == null) return 50;
        switch (type)
        {
            case PlantType.Moonflower: return grid.moonflowerCost;
            case PlantType.Rose: return grid.roseCost;
            case PlantType.Durian: return grid.durianCost;
            default: return grid.GetPlantDefinition(type).cost;
        }
    }

    public float GetPlantMaxCooldown(PlantType type)
    {
        if (grid == null) return 5f;
        switch (type)
        {
            case PlantType.Moonflower: return grid.moonflowerCooldown;
            case PlantType.Rose: return grid.roseCooldown;
            case PlantType.Durian: return grid.durianCooldown;
            default: return grid.GetPlantDefinition(type).cooldown;
        }
    }

    public float GetPlantCooldownRemaining(PlantType type)
    {
        int idx = Mathf.Clamp((int)type, 0, plantCooldownTimers.Length - 1);
        return plantCooldownTimers[idx];
    }

    public bool CanAffordAndReadyPlant(PlantType type)
    {
        return grid != null && grid.currentMoons >= GetPlantCost(type) && GetPlantCooldownRemaining(type) <= 0.001f;
    }

    public void SpendForPlant(PlantType type)
    {
        if (grid == null) return;
        grid.currentMoons -= GetPlantCost(type);
        int idx = Mathf.Clamp((int)type, 0, plantCooldownTimers.Length - 1);
        plantCooldownTimers[idx] = GetPlantMaxCooldown(type);
    }

    public int GetZombieCost(ZombieType type)
    {
        if (grid == null) return 50;
        switch (type)
        {
            case ZombieType.Normal: return grid.zombieCost;
            case ZombieType.Buckethead: return grid.bucketheadCost;
            case ZombieType.Bunny: return grid.bunnyCost;
            case ZombieType.Hazmat: return grid.hazmatCost;
            default: return grid.GetZombieDefinition(type).cost;
        }
    }

    public float GetZombieMaxCooldown(ZombieType type)
    {
        if (grid == null) return 5f;
        switch (type)
        {
            case ZombieType.Normal: return grid.zombieCooldown;
            case ZombieType.Buckethead: return grid.bucketheadCooldown;
            case ZombieType.Bunny: return grid.bunnyCooldown;
            case ZombieType.Hazmat: return grid.hazmatCooldown;
            default: return grid.GetZombieDefinition(type).cooldown;
        }
    }

    public float GetZombieCooldownRemaining(ZombieType type)
    {
        int idx = Mathf.Clamp((int)type, 0, zombieCooldownTimers.Length - 1);
        return zombieCooldownTimers[idx];
    }

    public bool CanAffordAndReadyZombie(ZombieType type)
    {
        return grid != null && grid.currentBrains >= GetZombieCost(type) && GetZombieCooldownRemaining(type) <= 0.001f;
    }

    public void SpendForZombie(ZombieType type)
    {
        if (grid == null) return;
        grid.currentBrains -= GetZombieCost(type);
        int idx = Mathf.Clamp((int)type, 0, zombieCooldownTimers.Length - 1);
        zombieCooldownTimers[idx] = GetZombieMaxCooldown(type);
    }

    public void AddMoons(int amount)
    {
        if (grid != null)
        {
            grid.currentMoons += amount;
        }
    }

    public void SpawnSkyFallingMoon()
    {
        if (grid == null) return;

        float marginX = 1.2f;
        float randomX = Random.Range(
            transform.position.x - grid.wallWidth * 0.5f + marginX,
            transform.position.x + grid.wallWidth * 0.5f - marginX
        );
        float startY = transform.position.y + grid.wallHeight * 0.5f + 0.6f;
        float landingY = Random.Range(
            transform.position.y - grid.wallHeight * 0.38f,
            transform.position.y + grid.wallHeight * 0.25f
        );
        float z = transform.position.z - (grid.wallThickness * 0.5f) - 0.45f;

        int id = nextMoonId++;
        Vector3 startPos = new Vector3(randomX, startY, z);
        SpawnSkyMoonWithId(id, startPos, landingY, grid.skyMoonValue);
        grid.NotifySkyMoonSpawned(id, startPos, landingY, grid.skyMoonValue);
    }

    public void SpawnSkyMoonWithId(int id, Vector3 startPos, float landingY, int value)
    {
        GameObject moonObj = CreateMoonObject("SkyMoon");
        CollectibleMoon moon = moonObj.GetComponent<CollectibleMoon>();
        if (moon == null) moon = moonObj.AddComponent<CollectibleMoon>();
        moon.networkId = id;
        moon.SetupSkyFall(grid, startPos, new Vector3(startPos.x, landingY, startPos.z), value);
    }

    public void SpawnMoonFromFlower(Vector3 flowerWorldPos, int amount = 50)
    {
        if (grid == null || !grid.IsMoonAuthority) return;

        int id = nextMoonId++;
        SpawnFlowerMoonWithId(id, flowerWorldPos, amount);
        grid.NotifyFlowerMoonSpawned(id, flowerWorldPos, amount);
    }

    public void SpawnFlowerMoonWithId(int id, Vector3 flowerWorldPos, int amount)
    {
        GameObject moonObj = CreateMoonObject("MoonflowerMoon");
        CollectibleMoon moon = moonObj.GetComponent<CollectibleMoon>();
        if (moon == null) moon = moonObj.AddComponent<CollectibleMoon>();
        moon.networkId = id;
        moon.SetupMoonflowerDrop(grid, flowerWorldPos + new Vector3(0f, 0.2f, -0.2f), amount);
    }

    public void RemoveMoonsRemote(List<int> ids)
    {
        for (int i = CollectibleMoon.ActiveMoons.Count - 1; i >= 0; i--)
        {
            CollectibleMoon m = CollectibleMoon.ActiveMoons[i];
            if (m != null && ids.Contains(m.networkId))
            {
                Destroy(m.gameObject);
            }
        }
    }

    public bool TryClickCollectibleMoon(Camera targetCamera)
    {
        if (grid == null || grid.localRole == MultiplayerMenuManager.PlayerRole.Zombie || targetCamera == null)
        {
            return false;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 wallFrontCenter = transform.position - transform.forward * (grid.wallThickness * 0.5f + 0.4f);
        Plane plane = new Plane(-transform.forward, wallFrontCenter);

        if (!plane.Raycast(ray, out float enter))
        {
            return false;
        }

        Vector3 clickWorld = ray.GetPoint(enter);
        CollectibleMoon primaryMoon = null;
        float bestDist = 0.85f;

        for (int i = CollectibleMoon.ActiveMoons.Count - 1; i >= 0; i--)
        {
            CollectibleMoon moon = CollectibleMoon.ActiveMoons[i];
            if (moon == null || moon.IsCollected) continue;

            float d = Vector2.Distance(
                new Vector2(clickWorld.x, clickWorld.y),
                new Vector2(moon.transform.position.x, moon.transform.position.y)
            );
            if (d <= bestDist)
            {
                bestDist = d;
                primaryMoon = moon;
            }
        }

        if (primaryMoon == null)
        {
            return false;
        }

        Vector2 centerPos = new Vector2(primaryMoon.transform.position.x, primaryMoon.transform.position.y);
        var collectedIds = new List<int>();
        for (int i = CollectibleMoon.ActiveMoons.Count - 1; i >= 0; i--)
        {
            CollectibleMoon moon = CollectibleMoon.ActiveMoons[i];
            if (moon == null || moon.IsCollected) continue;

            float distToCluster = Vector2.Distance(
                centerPos,
                new Vector2(moon.transform.position.x, moon.transform.position.y)
            );
            float distToClick = Vector2.Distance(
                new Vector2(clickWorld.x, clickWorld.y),
                new Vector2(moon.transform.position.x, moon.transform.position.y)
            );

            if (distToCluster <= grid.moonClusterPickupRadius || distToClick <= grid.moonClusterPickupRadius)
            {
                moon.Collect();
                collectedIds.Add(moon.networkId);
            }
        }

        if (collectedIds.Count > 0)
        {
            grid.NotifyMoonsCollected(collectedIds);
        }

        return true;
    }

    private GameObject CreateMoonObject(string objectName)
    {
        GameObject prefab = grid != null && grid.GameConfig != null ? grid.GameConfig.collectibleMoonPrefab : null;
        GameObject moonObj = prefab != null ? Instantiate(prefab) : GameVisuals.CreateMoon(objectName);
        moonObj.name = objectName;

        if (grid != null && grid.TilesContainerTransform != null)
        {
            moonObj.transform.SetParent(grid.TilesContainerTransform, true);
        }
        return moonObj;
    }
}
