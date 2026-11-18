using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Master ScriptableObject holding grid geometry, match rules, economy balance,
/// board palette, prefabs, and Plant/Zombie definition assets.
/// </summary>
[CreateAssetMenu(fileName = "DefaultGameConfig", menuName = "Stellar/Data/Game Config")]
public class GameConfigSO : ScriptableObject
{
    [Header("Grid Dimensions (5x8 Plant Spots)")]
    public int rows = 5;
    public int columns = 8;

    [Header("Wall 16:9 Dimensions")]
    public float wallWidth = 16.0f;
    public float wallHeight = 9.0f;
    public float wallThickness = 0.2f;
    public float tilePadding = 0.06f;

    [Header("Match Timer & Rules")]
    public float matchDuration = 300.0f;
    public int zombieAllowedColumnsFromRight = 2;

    [Header("Moon Economy & Cluster Pickup")]
    public int startingMoons = 100;
    public float skyMoonInterval = 15.0f;
    public int skyMoonValue = 50;
    public float moonClusterPickupRadius = 3.2f;

    [Header("Brains Economy")]
    public int startingBrains = 150;
    public float passiveBrainInterval = 10.0f;
    public int passiveBrainAmount = 25;

    [Header("Clash Zoom & 3D Arena")]
    public float clashZoomDuration = 1.35f;
    public float clashTargetOrthoSize = 1.15f;
    public float arenaResultPauseDuration = 2.5f;

    [Header("Visual Colors")]
    public Color tileColorLight = new Color(0.24f, 0.54f, 0.24f, 1.0f);
    public Color tileColorDark = new Color(0.18f, 0.44f, 0.18f, 1.0f);
    public Color tileHoverColor = new Color(0.45f, 0.92f, 0.45f, 1.0f);
    public Color tileZombieHoverColor = new Color(0.82f, 0.38f, 0.98f, 1.0f);
    public Color tileOccupiedColor = new Color(0.85f, 0.30f, 0.25f, 1.0f);

    [Header("Audio")]
    public AudioClip backgroundMusic2D;
    [Range(0f, 1f)] public float backgroundMusicVolume = 0.65f;

    [Header("Prefab References")]
    public GameObject tilePrefab;
    public GameObject collectibleMoonPrefab;
    public GameObject roseProjectilePrefab;
    public GameObject hazmatAcidProjectilePrefab;

    [Header("Unit Definitions")]
    public PlantDefinitionSO[] plantDefinitions = Array.Empty<PlantDefinitionSO>();
    public ZombieDefinitionSO[] zombieDefinitions = Array.Empty<ZombieDefinitionSO>();

    private Dictionary<PlantType, PlantDefinitionSO> runtimePlantDefaults;
    private Dictionary<ZombieType, ZombieDefinitionSO> runtimeZombieDefaults;

    public PlantDefinitionSO GetPlantDefinition(PlantType type)
    {
        if (plantDefinitions != null)
        {
            for (int i = 0; i < plantDefinitions.Length; i++)
            {
                if (plantDefinitions[i] != null && plantDefinitions[i].plantType == type)
                {
                    return plantDefinitions[i];
                }
            }
        }

        if (runtimePlantDefaults == null)
        {
            runtimePlantDefaults = new Dictionary<PlantType, PlantDefinitionSO>();
        }

        if (!runtimePlantDefaults.TryGetValue(type, out PlantDefinitionSO fallback) || fallback == null)
        {
            fallback = PlantDefinitionSO.CreateDefault(type);
            runtimePlantDefaults[type] = fallback;
        }
        return fallback;
    }

    public ZombieDefinitionSO GetZombieDefinition(ZombieType type)
    {
        if (zombieDefinitions != null)
        {
            for (int i = 0; i < zombieDefinitions.Length; i++)
            {
                if (zombieDefinitions[i] != null && zombieDefinitions[i].zombieType == type)
                {
                    return zombieDefinitions[i];
                }
            }
        }

        if (runtimeZombieDefaults == null)
        {
            runtimeZombieDefaults = new Dictionary<ZombieType, ZombieDefinitionSO>();
        }

        if (!runtimeZombieDefaults.TryGetValue(type, out ZombieDefinitionSO fallback) || fallback == null)
        {
            fallback = ZombieDefinitionSO.CreateDefault(type);
            runtimeZombieDefaults[type] = fallback;
        }
        return fallback;
    }

    public static GameConfigSO LoadOrCreateDefault()
    {
        GameConfigSO loaded = Resources.Load<GameConfigSO>("Config/DefaultGameConfig");
        if (loaded != null)
        {
            return loaded;
        }

        GameConfigSO cfg = CreateInstance<GameConfigSO>();
        cfg.name = "DefaultGameConfig_Runtime";
        cfg.PopulateDefaultUnitDefinitions();
        return cfg;
    }

    public void PopulateDefaultUnitDefinitions()
    {
        plantDefinitions = new[]
        {
            PlantDefinitionSO.CreateDefault(PlantType.Moonflower),
            PlantDefinitionSO.CreateDefault(PlantType.Rose),
            PlantDefinitionSO.CreateDefault(PlantType.Durian)
        };
        zombieDefinitions = new[]
        {
            ZombieDefinitionSO.CreateDefault(ZombieType.Normal),
            ZombieDefinitionSO.CreateDefault(ZombieType.Buckethead),
            ZombieDefinitionSO.CreateDefault(ZombieType.Bunny),
            ZombieDefinitionSO.CreateDefault(ZombieType.Hazmat)
        };
    }
}
