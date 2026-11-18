using UnityEngine;

/// <summary>
/// Constructs the 3D 1v1 combat arena, its 12 randomized preset/procedural obstacle layouts,
/// remote opponent avatar hitboxes, and floating world-space HP bar.
/// </summary>
public static class ArenaEnvironmentBuilder
{
    public static readonly Vector3 ArenaPlantSpawn = new Vector3(0f, 1.5f, -38.0f);
    public static readonly Vector3 ArenaZombieSpawn = new Vector3(0f, 1.5f, -14.0f);

    public struct ArenaBuildResult
    {
        public GameObject arena3DRoot;
        public GameObject arenaObstaclesRoot;
        public Renderer arenaFloorRenderer;
        public GameObject remoteArenaAvatar;
        public CapsuleCollider remoteBodyHitbox;
        public SphereCollider remoteHeadHitbox;
        public Transform remoteZombieVisualRoot;
        public Transform remotePlantVisualRoot;
        public GameObject remoteParryShieldObj;
        public Transform remoteHpBarFill;
        public Renderer remoteHpBarFillRenderer;
        public Color remoteHpBarBaseColor;
        public string opponentName;
        public string presetName;
    }

    public static ArenaBuildResult BuildEnvironment(
        int seed,
        bool localIsPlant,
        PlantType plantType,
        ZombieType zombieType,
        GameConfigSO config = null)
    {
        var result = new ArenaBuildResult();

        GameObject arenaRoot = new GameObject("Arena3D_1v1");
        arenaRoot.transform.position = new Vector3(0f, 0f, -26.0f);
        result.arena3DRoot = arenaRoot;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "ArenaFloor";
        floor.transform.SetParent(arenaRoot.transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
        floor.transform.localScale = new Vector3(44f, 0.5f, 44f);
        result.arenaFloorRenderer = floor.GetComponent<Renderer>();
        result.arenaFloorRenderer.sharedMaterial = MaterialCache.GetColoredMaterial(new Color(0.22f, 0.28f, 0.24f));

        Color wallCol = new Color(0.32f, 0.36f, 0.42f);
        CreateArenaBlock(arenaRoot.transform, "Wall_North", new Vector3(0f, 1.75f, 22f), new Vector3(44f, 3.5f, 0.8f), wallCol);
        CreateArenaBlock(arenaRoot.transform, "Wall_South", new Vector3(0f, 1.75f, -22f), new Vector3(44f, 3.5f, 0.8f), wallCol);
        CreateArenaBlock(arenaRoot.transform, "Wall_East", new Vector3(22f, 1.75f, 0f), new Vector3(0.8f, 3.5f, 44f), wallCol);
        CreateArenaBlock(arenaRoot.transform, "Wall_West", new Vector3(-22f, 1.75f, 0f), new Vector3(0.8f, 3.5f, 44f), wallCol);

        RebuildRandomArenaLayout(seed, arenaRoot, ref result.arenaObstaclesRoot, result.arenaFloorRenderer, out result.presetName);

        GameObject avatar = new GameObject("Arena3D_RemotePlayer");
        avatar.transform.SetParent(arenaRoot.transform, false);
        Vector3 remoteSpawn = localIsPlant ? ArenaZombieSpawn : ArenaPlantSpawn;
        avatar.transform.SetPositionAndRotation(remoteSpawn, Quaternion.Euler(0f, localIsPlant ? 180f : 0f, 0f));
        result.remoteArenaAvatar = avatar;

        CapsuleCollider bodyHitbox = avatar.AddComponent<CapsuleCollider>();
        bodyHitbox.height = 2.0f;
        bodyHitbox.radius = (!localIsPlant && plantType == PlantType.Durian) ? 0.78f : 0.58f;
        bodyHitbox.center = Vector3.zero;
        result.remoteBodyHitbox = bodyHitbox;

        SphereCollider headHitbox = avatar.AddComponent<SphereCollider>();
        headHitbox.radius = (!localIsPlant && plantType == PlantType.Durian) ? 0.72f : 0.52f;
        headHitbox.center = new Vector3(0f, 0.35f, 0f);
        result.remoteHeadHitbox = headHitbox;

        Rigidbody rb = avatar.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        GameObject visual;
        if (localIsPlant)
        {
            visual = ArenaModelFactory.Create3DZombieModel(zombieType, $"Visual_3D_{zombieType}", out result.remoteParryShieldObj, config);
            result.opponentName = $"{zombieType.ToString().ToUpper()} ZOMBIE";
            result.remoteZombieVisualRoot = visual.transform;
            result.remotePlantVisualRoot = null;
        }
        else
        {
            visual = ArenaModelFactory.Create3DPlantModel(plantType, $"Visual_3D_{plantType}", config);
            result.opponentName = $"{plantType.ToString().ToUpper()} PLAYER";
            result.remoteZombieVisualRoot = null;
            result.remotePlantVisualRoot = visual.transform;
        }

        visual.transform.SetParent(avatar.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        CreateRemoteWorldHPBar(
            avatar.transform,
            localIsPlant,
            out result.remoteHpBarFill,
            out result.remoteHpBarFillRenderer,
            out result.remoteHpBarBaseColor);

        return result;
    }

    public static void RebuildRandomArenaLayout(
        int seed,
        GameObject arena3DRoot,
        ref GameObject arenaObstaclesRoot,
        Renderer arenaFloorRenderer,
        out string presetName)
    {
        presetName = "Random";
        if (arena3DRoot == null) return;

        if (arenaObstaclesRoot != null)
        {
            Object.Destroy(arenaObstaclesRoot);
            arenaObstaclesRoot = null;
        }

        arenaObstaclesRoot = new GameObject("ArenaObstacles_Random");
        arenaObstaclesRoot.transform.SetParent(arena3DRoot.transform, false);
        arenaObstaclesRoot.transform.localPosition = Vector3.zero;

        System.Random rng = new System.Random(seed);
        int presetIndex = rng.Next(0, 12);
        bool mirrorX = rng.NextDouble() > 0.5;
        bool mirrorZ = rng.NextDouble() > 0.5;

        Color[] floorTints = new Color[]
        {
            new Color(0.22f, 0.28f, 0.24f),
            new Color(0.26f, 0.24f, 0.30f),
            new Color(0.28f, 0.26f, 0.21f),
            new Color(0.19f, 0.25f, 0.30f),
            new Color(0.25f, 0.22f, 0.22f),
            new Color(0.20f, 0.29f, 0.27f)
        };
        if (arenaFloorRenderer != null)
        {
            arenaFloorRenderer.sharedMaterial = MaterialCache.GetColoredMaterial(floorTints[rng.Next(0, floorTints.Length)]);
        }

        Color pillarColor = new Color(0.40f + (float)rng.NextDouble() * 0.16f, 0.44f + (float)rng.NextDouble() * 0.16f, 0.42f + (float)rng.NextDouble() * 0.16f);
        Color wallColor = new Color(0.34f + (float)rng.NextDouble() * 0.14f, 0.38f + (float)rng.NextDouble() * 0.14f, 0.42f + (float)rng.NextDouble() * 0.14f);
        Color crateColor = new Color(0.46f + (float)rng.NextDouble() * 0.14f, 0.33f + (float)rng.NextDouble() * 0.10f, 0.20f + (float)rng.NextDouble() * 0.08f);
        Color accentColor = new Color(0.30f + (float)rng.NextDouble() * 0.22f, 0.46f + (float)rng.NextDouble() * 0.20f, 0.38f + (float)rng.NextDouble() * 0.22f);

        float mx = mirrorX ? -1f : 1f;
        float mz = mirrorZ ? -1f : 1f;
        Transform obsParent = arenaObstaclesRoot.transform;

        switch (presetIndex)
        {
            case 0:
                presetName = "Crossfire Bastion";
                SpawnRandomObstacle(obsParent, "Center_Bastion_W", new Vector3(-2.6f * mx, 1.3f, 0f), new Vector3(1.6f, 2.6f, 3.6f), 15f * mx, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Center_Bastion_E", new Vector3(2.6f * mx, 1.3f, 0f), new Vector3(1.6f, 2.6f, 3.6f), -15f * mx, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Wing_NW", new Vector3(-8.5f, 1.0f, 6.2f), new Vector3(4.2f, 2.0f, 1.1f), 28f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Wing_NE", new Vector3(8.5f, 1.0f, 6.2f), new Vector3(4.2f, 2.0f, 1.1f), -28f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Wing_SW", new Vector3(-8.5f, 1.0f, -6.2f), new Vector3(4.2f, 2.0f, 1.1f), -28f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Wing_SE", new Vector3(8.5f, 1.0f, -6.2f), new Vector3(4.2f, 2.0f, 1.1f), 28f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Flank_Crate_W", new Vector3(-15.0f, 0.8f, 0f), new Vector3(2.6f, 1.6f, 2.6f), 12f, crateColor, rng);
                SpawnRandomObstacle(obsParent, "Flank_Crate_E", new Vector3(15.0f, 0.8f, 0f), new Vector3(2.6f, 1.6f, 2.6f), -12f, crateColor, rng);
                break;

            case 1:
                presetName = "Twin Fortresses";
                SpawnRandomObstacle(obsParent, "Fort_North_Front", new Vector3(0f, 0.95f, 6.8f), new Vector3(6.4f, 1.9f, 1.1f), 0f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Fort_South_Front", new Vector3(0f, 0.95f, -6.8f), new Vector3(6.4f, 1.9f, 1.1f), 0f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Fort_NW_Side", new Vector3(-5.2f, 1.2f, 8.2f), new Vector3(1.2f, 2.4f, 3.8f), 18f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Fort_NE_Side", new Vector3(5.2f, 1.2f, 8.2f), new Vector3(1.2f, 2.4f, 3.8f), -18f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Fort_SW_Side", new Vector3(-5.2f, 1.2f, -8.2f), new Vector3(1.2f, 2.4f, 3.8f), -18f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Fort_SE_Side", new Vector3(5.2f, 1.2f, -8.2f), new Vector3(1.2f, 2.4f, 3.8f), 18f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Mid_Obelisk_W", new Vector3(-11.5f, 1.6f, 0f), new Vector3(2.2f, 3.2f, 2.2f), 45f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Mid_Obelisk_E", new Vector3(11.5f, 1.6f, 0f), new Vector3(2.2f, 3.2f, 2.2f), 45f, accentColor, rng);
                break;

            case 2:
                presetName = "Labyrinth Maze";
                SpawnRandomObstacle(obsParent, "Maze_C1", new Vector3(-4.5f * mx, 1.35f, 2.5f * mz), new Vector3(6.2f, 2.7f, 1.0f), 0f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Maze_C2", new Vector3(4.5f * mx, 1.35f, -2.5f * mz), new Vector3(6.2f, 2.7f, 1.0f), 0f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Maze_V1", new Vector3(-8.5f * mx, 1.35f, -4.0f * mz), new Vector3(1.0f, 2.7f, 6.5f), 0f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Maze_V2", new Vector3(8.5f * mx, 1.35f, 4.0f * mz), new Vector3(1.0f, 2.7f, 6.5f), 0f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Maze_Outer_NW", new Vector3(-13.5f, 1.2f, 9.5f), new Vector3(4.8f, 2.4f, 1.2f), 20f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Maze_Outer_SE", new Vector3(13.5f, 1.2f, -9.5f), new Vector3(4.8f, 2.4f, 1.2f), 20f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Maze_Outer_NE", new Vector3(13.5f, 1.2f, 9.5f), new Vector3(1.2f, 2.4f, 4.8f), -15f, crateColor, rng);
                SpawnRandomObstacle(obsParent, "Maze_Outer_SW", new Vector3(-13.5f, 1.2f, -9.5f), new Vector3(1.2f, 2.4f, 4.8f), -15f, crateColor, rng);
                break;

            case 3:
                presetName = "Colosseum Pillars";
                SpawnRandomObstacle(obsParent, "Colosseum_Altar", new Vector3(0f, 0.75f, 0f), new Vector3(3.4f, 1.5f, 3.4f), 45f, accentColor, rng);
                const int ringCount = 8;
                const float ringRadius = 9.8f;
                for (int i = 0; i < ringCount; i++)
                {
                    float ang = (i * Mathf.PI * 2f / ringCount) + 0.35f;
                    Vector3 pos = new Vector3(Mathf.Cos(ang) * ringRadius, 1.65f, Mathf.Sin(ang) * ringRadius);
                    SpawnRandomObstacle(obsParent, $"Colosseum_Pillar_{i}", pos, new Vector3(1.9f, 3.3f, 1.9f), ang * Mathf.Rad2Deg, pillarColor, rng);
                }
                break;

            case 4:
                presetName = "Three-Lane Trench";
                SpawnRandomObstacle(obsParent, "LaneDiv_W_North", new Vector3(-6.8f, 1.3f, 6.2f), new Vector3(1.2f, 2.6f, 6.8f), 0f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "LaneDiv_W_South", new Vector3(-6.8f, 1.3f, -6.2f), new Vector3(1.2f, 2.6f, 6.8f), 0f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "LaneDiv_E_North", new Vector3(6.8f, 1.3f, 6.2f), new Vector3(1.2f, 2.6f, 6.8f), 0f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "LaneDiv_E_South", new Vector3(6.8f, 1.3f, -6.2f), new Vector3(1.2f, 2.6f, 6.8f), 0f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Mid_Bridge_Cover", new Vector3(0f, 0.8f, 0f), new Vector3(4.2f, 1.6f, 1.5f), 25f * mx, crateColor, rng);
                SpawnRandomObstacle(obsParent, "Side_Bunker_W", new Vector3(-14.5f, 0.95f, 0f), new Vector3(3.2f, 1.9f, 2.0f), 0f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Side_Bunker_E", new Vector3(14.5f, 0.95f, 0f), new Vector3(3.2f, 1.9f, 2.0f), 0f, accentColor, rng);
                break;

            case 5:
                presetName = "Ancient Ruins";
                SpawnRandomObstacle(obsParent, "Ruin_Monolith_1", new Vector3(-3.8f * mx, 1.7f, 3.2f * mz), new Vector3(1.8f, 3.4f, 1.8f), 18f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Ruin_FallenColumn_1", new Vector3(4.5f * mx, 0.65f, 2.0f * mz), new Vector3(5.6f, 1.3f, 1.4f), -32f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Ruin_Monolith_2", new Vector3(3.8f * mx, 1.7f, -3.2f * mz), new Vector3(1.8f, 3.4f, 1.8f), -18f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Ruin_FallenColumn_2", new Vector3(-4.5f * mx, 0.65f, -2.0f * mz), new Vector3(5.6f, 1.3f, 1.4f), 32f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Ruin_Step_NW", new Vector3(-11.0f, 0.85f, 8.5f), new Vector3(3.5f, 1.7f, 2.4f), 40f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Ruin_Step_SE", new Vector3(11.0f, 0.85f, -8.5f), new Vector3(3.5f, 1.7f, 2.4f), 40f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Ruin_Step_NE", new Vector3(12.0f, 1.25f, 7.5f), new Vector3(2.4f, 2.5f, 2.4f), 15f, crateColor, rng);
                SpawnRandomObstacle(obsParent, "Ruin_Step_SW", new Vector3(-12.0f, 1.25f, -7.5f), new Vector3(2.4f, 2.5f, 2.4f), 15f, crateColor, rng);
                break;

            case 6:
                presetName = "Diamond Citadel";
                SpawnRandomObstacle(obsParent, "Diamond_NW", new Vector3(-4.6f, 1.25f, 4.6f), new Vector3(5.5f, 2.5f, 1.0f), -45f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Diamond_NE", new Vector3(4.6f, 1.25f, 4.6f), new Vector3(5.5f, 2.5f, 1.0f), 45f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Diamond_SW", new Vector3(-4.6f, 1.25f, -4.6f), new Vector3(5.5f, 2.5f, 1.0f), 45f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Diamond_SE", new Vector3(4.6f, 1.25f, -4.6f), new Vector3(5.5f, 2.5f, 1.0f), -45f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Diamond_Core", new Vector3(0f, 1.5f, 0f), new Vector3(1.8f, 3.0f, 1.8f), 45f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Outer_Pillar_W", new Vector3(-14.0f, 1.5f, 0f), new Vector3(2.2f, 3.0f, 2.2f), 0f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Outer_Pillar_E", new Vector3(14.0f, 1.5f, 0f), new Vector3(2.2f, 3.0f, 2.2f), 0f, pillarColor, rng);
                break;

            case 7:
                presetName = "Cargo Crate Yard";
                SpawnCrateCluster(obsParent, "Cluster_Center_W", new Vector3(-4.8f * mx, 0f, 1.5f * mz), crateColor, pillarColor, rng);
                SpawnCrateCluster(obsParent, "Cluster_Center_E", new Vector3(4.8f * mx, 0f, -1.5f * mz), crateColor, pillarColor, rng);
                SpawnCrateCluster(obsParent, "Cluster_NW", new Vector3(-11.5f, 0f, 7.5f), crateColor, accentColor, rng);
                SpawnCrateCluster(obsParent, "Cluster_NE", new Vector3(11.5f, 0f, 7.5f), crateColor, accentColor, rng);
                SpawnCrateCluster(obsParent, "Cluster_SW", new Vector3(-11.5f, 0f, -7.5f), crateColor, accentColor, rng);
                SpawnCrateCluster(obsParent, "Cluster_SE", new Vector3(11.5f, 0f, -7.5f), crateColor, accentColor, rng);
                break;

            case 8:
                presetName = "Chevron Gauntlet";
                SpawnRandomObstacle(obsParent, "Chev_N_Left", new Vector3(-3.2f, 1.15f, 5.8f), new Vector3(4.8f, 2.3f, 1.0f), 28f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Chev_N_Right", new Vector3(3.2f, 1.15f, 5.8f), new Vector3(4.8f, 2.3f, 1.0f), -28f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Chev_S_Left", new Vector3(-3.2f, 1.15f, -5.8f), new Vector3(4.8f, 2.3f, 1.0f), -28f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Chev_S_Right", new Vector3(3.2f, 1.15f, -5.8f), new Vector3(4.8f, 2.3f, 1.0f), 28f, wallColor, rng);
                SpawnRandomObstacle(obsParent, "Gauntlet_Mid_W", new Vector3(-9.5f, 1.4f, 0f), new Vector3(2.2f, 2.8f, 4.4f), 0f, pillarColor, rng);
                SpawnRandomObstacle(obsParent, "Gauntlet_Mid_E", new Vector3(9.5f, 1.4f, 0f), new Vector3(2.2f, 2.8f, 4.4f), 0f, pillarColor, rng);
                break;

            case 9:
                presetName = "Spiral Sanctuary";
                for (int b = 0; b < 4; b++)
                {
                    float baseAng = b * 90f + (mirrorX ? 32f : -32f);
                    float rad = baseAng * Mathf.Deg2Rad;
                    Vector3 pos = new Vector3(Mathf.Cos(rad) * 6.5f, 1.25f, Mathf.Sin(rad) * 6.5f);
                    SpawnRandomObstacle(obsParent, $"Spiral_Blade_{b}", pos, new Vector3(6.0f, 2.5f, 1.1f), baseAng + 35f, wallColor, rng);
                }
                SpawnRandomObstacle(obsParent, "Spiral_Outer_NW", new Vector3(-13.0f, 1.1f, 9.0f), new Vector3(2.6f, 2.2f, 2.6f), 20f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Spiral_Outer_NE", new Vector3(13.0f, 1.1f, 9.0f), new Vector3(2.6f, 2.2f, 2.6f), -20f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Spiral_Outer_SW", new Vector3(-13.0f, 1.1f, -9.0f), new Vector3(2.6f, 2.2f, 2.6f), -20f, accentColor, rng);
                SpawnRandomObstacle(obsParent, "Spiral_Outer_SE", new Vector3(13.0f, 1.1f, -9.0f), new Vector3(2.6f, 2.2f, 2.6f), 20f, accentColor, rng);
                break;

            case 10:
                presetName = "Checkered Bunkers";
                float[] xs = new float[] { -12f, -4.5f, 4.5f, 12f };
                float[] zs = new float[] { -6.5f, 0f, 6.5f };
                int idx = 0;
                for (int ix = 0; ix < xs.Length; ix++)
                {
                    for (int iz = 0; iz < zs.Length; iz++)
                    {
                        idx++;
                        if ((ix + iz) % 2 == 0)
                        {
                            SpawnRandomObstacle(obsParent, $"Checker_Pillar_{idx}", new Vector3(xs[ix], 1.55f, zs[iz]), new Vector3(1.8f, 3.1f, 1.8f), 15f * idx, pillarColor, rng);
                        }
                        else
                        {
                            SpawnRandomObstacle(obsParent, $"Checker_Low_{idx}", new Vector3(xs[ix], 0.75f, zs[iz]), new Vector3(3.0f, 1.5f, 1.4f), -20f * idx, crateColor, rng);
                        }
                    }
                }
                break;

            default:
                presetName = "Wildcard Chaos";
                break;
        }

        int extraCount = (presetIndex == 11) ? rng.Next(18, 25) : rng.Next(8, 14);
        for (int i = 0; i < extraCount; i++)
        {
            float rx = -17.5f + (float)rng.NextDouble() * 35.0f;
            float rz = -16.5f + (float)rng.NextDouble() * 33.0f;
            if (!IsArenaPositionSafeFromSpawns(rx, rz, 4.6f)) continue;

            int shapeKind = rng.Next(0, 5);
            float yaw = (float)rng.NextDouble() * 360f;
            Color col = (shapeKind == 0) ? pillarColor
                      : (shapeKind == 1) ? wallColor
                      : (shapeKind == 2) ? crateColor
                      : accentColor;

            if (shapeKind == 0)
            {
                float w = 1.4f + (float)rng.NextDouble() * 1.1f;
                float h = 2.4f + (float)rng.NextDouble() * 1.3f;
                CreateRandomObstacleBlock(obsParent, $"Proc_Pillar_{i}", new Vector3(rx, h * 0.5f, rz), new Vector3(w, h, w), yaw, col);
            }
            else if (shapeKind == 1)
            {
                float len = 3.0f + (float)rng.NextDouble() * 2.8f;
                float h = 1.6f + (float)rng.NextDouble() * 1.0f;
                CreateRandomObstacleBlock(obsParent, $"Proc_Wall_{i}", new Vector3(rx, h * 0.5f, rz), new Vector3(len, h, 0.95f), yaw, col);
            }
            else if (shapeKind == 2)
            {
                float len = 2.4f + (float)rng.NextDouble() * 2.0f;
                float h = 0.95f + (float)rng.NextDouble() * 0.45f;
                CreateRandomObstacleBlock(obsParent, $"Proc_Barrier_{i}", new Vector3(rx, h * 0.5f, rz), new Vector3(len, h, 1.3f), yaw, col);
            }
            else if (shapeKind == 3)
            {
                float s = 1.6f + (float)rng.NextDouble() * 1.2f;
                float h = 1.2f + (float)rng.NextDouble() * 0.9f;
                CreateRandomObstacleBlock(obsParent, $"Proc_Crate_{i}", new Vector3(rx, h * 0.5f, rz), new Vector3(s, h, s), yaw, col);
            }
            else
            {
                float s = 1.8f + (float)rng.NextDouble() * 0.8f;
                CreateRandomObstacleBlock(obsParent, $"Proc_StackBase_{i}", new Vector3(rx, 0.65f, rz), new Vector3(s, 1.3f, s), yaw, crateColor);
                CreateRandomObstacleBlock(obsParent, $"Proc_StackTop_{i}", new Vector3(rx + 0.15f, 1.75f, rz - 0.1f), new Vector3(s * 0.72f, 0.9f, s * 0.72f), yaw + 28f, accentColor);
            }
        }

        Physics.SyncTransforms();
    }

    private static void SpawnCrateCluster(Transform parent, string prefix, Vector3 baseLocalPos, Color primaryCol, Color secondaryCol, System.Random rng)
    {
        float jx = ((float)rng.NextDouble() - 0.5f) * 1.4f;
        float jz = ((float)rng.NextDouble() - 0.5f) * 1.4f;
        Vector3 p = new Vector3(baseLocalPos.x + jx, 0f, baseLocalPos.z + jz);
        if (!IsArenaPositionSafeFromSpawns(p.x, p.z, 4.2f)) return;

        float yaw = (float)rng.NextDouble() * 360f;
        CreateRandomObstacleBlock(parent, prefix + "_Main", new Vector3(p.x, 0.85f, p.z), new Vector3(2.4f, 1.7f, 2.4f), yaw, primaryCol);
        CreateRandomObstacleBlock(parent, prefix + "_Side", new Vector3(p.x + 1.6f, 0.55f, p.z + 0.6f), new Vector3(1.4f, 1.1f, 1.5f), yaw + 22f, secondaryCol);
        if (rng.NextDouble() > 0.35)
        {
            CreateRandomObstacleBlock(parent, prefix + "_Top", new Vector3(p.x - 0.2f, 2.15f, p.z + 0.1f), new Vector3(1.3f, 0.9f, 1.3f), yaw - 30f, secondaryCol);
        }
    }

    private static void SpawnRandomObstacle(Transform parent, string name, Vector3 localPos, Vector3 scale, float baseYaw, Color color, System.Random rng)
    {
        float jx = ((float)rng.NextDouble() - 0.5f) * 1.2f;
        float jz = ((float)rng.NextDouble() - 0.5f) * 1.2f;
        float jYaw = ((float)rng.NextDouble() - 0.5f) * 16.0f;

        Vector3 finalPos = new Vector3(localPos.x + jx, localPos.y, localPos.z + jz);
        if (!IsArenaPositionSafeFromSpawns(finalPos.x, finalPos.z, 4.0f)) return;

        CreateRandomObstacleBlock(parent, name, finalPos, scale, baseYaw + jYaw, color);
    }

    private static bool IsArenaPositionSafeFromSpawns(float localX, float localZ, float minClearance)
    {
        Vector2 pt = new Vector2(localX, localZ);
        if (Vector2.Distance(pt, new Vector2(0f, -12.0f)) < minClearance) return false;
        if (Vector2.Distance(pt, new Vector2(0f, 12.0f)) < minClearance) return false;
        if (Mathf.Abs(localX) > 19.2f || Mathf.Abs(localZ) > 19.2f) return false;
        return true;
    }

    private static void CreateRandomObstacleBlock(Transform parent, string name, Vector3 localPos, Vector3 scale, float yawDegrees, Color color)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = localPos;
        block.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
        block.transform.localScale = scale;
        block.GetComponent<Renderer>().sharedMaterial = MaterialCache.GetColoredMaterial(color);
    }

    private static void CreateArenaBlock(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = localPos;
        block.transform.localScale = scale;
        block.GetComponent<Renderer>().sharedMaterial = MaterialCache.GetColoredMaterial(color);
    }

    private static void CreateRemoteWorldHPBar(
        Transform parent,
        bool opponentIsZombie,
        out Transform hpBarFill,
        out Renderer hpBarFillRenderer,
        out Color hpBarBaseColor)
    {
        GameObject hpRoot = new GameObject("WorldHPBar");
        hpRoot.transform.SetParent(parent, false);
        hpRoot.transform.localPosition = new Vector3(0f, 1.38f, 0f);
        hpRoot.AddComponent<FaceCameraBillboard>();

        GameObject bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgQuad.name = "HPBar_BG";
        bgQuad.transform.SetParent(hpRoot.transform, false);
        bgQuad.transform.localPosition = Vector3.zero;
        bgQuad.transform.localScale = new Vector3(1.32f, 0.18f, 1f);
        MaterialCache.DestroyCollider(bgQuad);
        bgQuad.GetComponent<Renderer>().sharedMaterial = MaterialCache.GetColoredMaterial(new Color(0.08f, 0.08f, 0.10f));

        GameObject fillQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fillQuad.name = "HPBar_Fill";
        fillQuad.transform.SetParent(hpRoot.transform, false);
        fillQuad.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        fillQuad.transform.localScale = new Vector3(1.24f, 0.12f, 1f);
        MaterialCache.DestroyCollider(fillQuad);

        hpBarBaseColor = opponentIsZombie ? new Color(0.82f, 0.32f, 0.98f) : new Color(0.28f, 0.92f, 0.38f);
        hpBarFillRenderer = fillQuad.GetComponent<Renderer>();
        hpBarFillRenderer.sharedMaterial = MaterialCache.GetColoredMaterial(hpBarBaseColor);
        hpBarFill = fillQuad.transform;
    }
}
