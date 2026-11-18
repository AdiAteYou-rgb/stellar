#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Stellar.Combat;

/// <summary>
/// Editor utility that generates persistent Unity Sprite imports, Material assets,
/// 2D/3D Prefabs, and ScriptableObject data assets, and wires them into the active scene.
/// </summary>
public static class ProjectAssetGenerator
{
    private const string ConfigRoot = "Assets/Resources/Config";
    private const string PlantsConfigDir = "Assets/Resources/Config/Plants";
    private const string ZombiesConfigDir = "Assets/Resources/Config/Zombies";
    private const string MaterialsDir = "Assets/Resources/Materials";
    private const string PrefabsRoot = "Assets/Resources/Prefabs";
    private const string Units2DDir = "Assets/Resources/Prefabs/Units2D";
    private const string ProjectilesDir = "Assets/Resources/Prefabs/Projectiles";
    private const string CollectiblesDir = "Assets/Resources/Prefabs/Collectibles";
    private const string GridPrefabsDir = "Assets/Resources/Prefabs/Grid";
    private const string Arena3DDir = "Assets/Resources/Prefabs/Arena3D";

    [MenuItem("Tools/Setup & Generate Stellar Prefabs and Data Assets")]
    public static void GenerateAllAssetsAndPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        EnsureDirectories();
        GameVisuals.EnsureSpritesLoaded();
        ConfigureSpriteImporters();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var materialAssetCache = new Dictionary<string, Material>();

        // 1. Generate 2D Unit Prefabs
        GameObject mfPrefab2D = CreatePlantPrefab2D(PlantType.Moonflower, "Plant_Moonflower", "Assets/Sprites/Moonflower.png");
        GameObject rosePrefab2D = CreatePlantPrefab2D(PlantType.Rose, "Plant_Rose", "Assets/Sprites/Rose.png");
        GameObject durianPrefab2D = CreatePlantPrefab2D(PlantType.Durian, "Plant_Durian", "Assets/Sprites/Durian.png");

        GameObject zNormalPrefab2D = CreateZombiePrefab2D(ZombieType.Normal, "Zombie_Normal", "Assets/Sprites/Zombie.png");
        GameObject zBucketPrefab2D = CreateZombiePrefab2D(ZombieType.Buckethead, "Zombie_Buckethead", "Assets/Sprites/BucketheadZombie.png");
        GameObject zBunnyPrefab2D = CreateZombiePrefab2D(ZombieType.Bunny, "Zombie_Bunny", "Assets/Sprites/BunnyZombie.png");
        GameObject zHazmatPrefab2D = CreateZombiePrefab2D(ZombieType.Hazmat, "Zombie_Hazmat", "Assets/Sprites/HazmatZombie.png");

        // 2. Generate Projectile, Collectible, and Grid Tile Prefabs
        GameObject roseProjPrefab = CreateProjectilePrefab("Projectile_RoseThorn", "Assets/Sprites/RoseProjectile.png", 0.35f, 20, true);
        GameObject acidProjPrefab = CreateProjectilePrefab("Projectile_HazmatAcid", "Assets/Sprites/AcidProjectile.png", 0.36f, 21, false);
        GameObject moonPrefab = CreateMoonPrefab();
        GameObject tilePrefab = CreateTilePrefab(materialAssetCache);

        // 3. Generate 3D Arena Visual Prefabs
        GameObject mfPrefab3D = CreateArena3DPlantPrefab(PlantType.Moonflower, "Arena3D_Plant_Moonflower", materialAssetCache);
        GameObject rosePrefab3D = CreateArena3DPlantPrefab(PlantType.Rose, "Arena3D_Plant_Rose", materialAssetCache);
        GameObject durianPrefab3D = CreateArena3DPlantPrefab(PlantType.Durian, "Arena3D_Plant_Durian", materialAssetCache);

        GameObject zNormalPrefab3D = CreateArena3DZombiePrefab(ZombieType.Normal, "Arena3D_Zombie_Normal", materialAssetCache);
        GameObject zBucketPrefab3D = CreateArena3DZombiePrefab(ZombieType.Buckethead, "Arena3D_Zombie_Buckethead", materialAssetCache);
        GameObject zBunnyPrefab3D = CreateArena3DZombiePrefab(ZombieType.Bunny, "Arena3D_Zombie_Bunny", materialAssetCache);
        GameObject zHazmatPrefab3D = CreateArena3DZombiePrefab(ZombieType.Hazmat, "Arena3D_Zombie_Hazmat", materialAssetCache);

        // 4. Generate ScriptableObject Data Assets
        PlantDefinitionSO mfData = CreateOrUpdatePlantData(
            PlantType.Moonflower, $"{PlantsConfigDir}/MoonflowerData.asset",
            "Assets/Sprites/Moonflower.png", mfPrefab2D, mfPrefab3D);
        PlantDefinitionSO roseData = CreateOrUpdatePlantData(
            PlantType.Rose, $"{PlantsConfigDir}/RoseData.asset",
            "Assets/Sprites/Rose.png", rosePrefab2D, rosePrefab3D);
        PlantDefinitionSO durianData = CreateOrUpdatePlantData(
            PlantType.Durian, $"{PlantsConfigDir}/DurianData.asset",
            "Assets/Sprites/Durian.png", durianPrefab2D, durianPrefab3D);

        ZombieDefinitionSO zNormData = CreateOrUpdateZombieData(
            ZombieType.Normal, $"{ZombiesConfigDir}/NormalZombieData.asset",
            "Assets/Sprites/Zombie.png", zNormalPrefab2D, zNormalPrefab3D);
        ZombieDefinitionSO zBuckData = CreateOrUpdateZombieData(
            ZombieType.Buckethead, $"{ZombiesConfigDir}/BucketheadZombieData.asset",
            "Assets/Sprites/BucketheadZombie.png", zBucketPrefab2D, zBucketPrefab3D);
        ZombieDefinitionSO zBunData = CreateOrUpdateZombieData(
            ZombieType.Bunny, $"{ZombiesConfigDir}/BunnyZombieData.asset",
            "Assets/Sprites/BunnyZombie.png", zBunnyPrefab2D, zBunnyPrefab3D);
        ZombieDefinitionSO zHazData = CreateOrUpdateZombieData(
            ZombieType.Hazmat, $"{ZombiesConfigDir}/HazmatZombieData.asset",
            "Assets/Sprites/HazmatZombie.png", zHazmatPrefab2D, zHazmatPrefab3D);

        GameConfigSO masterConfig = CreateOrUpdateMasterConfig(
            tilePrefab, moonPrefab, roseProjPrefab, acidProjPrefab,
            new[] { mfData, roseData, durianData },
            new[] { zNormData, zBuckData, zBunData, zHazData });

        // 5. Wire Scene Objects
        WireSceneComponents(masterConfig);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ProjectAssetGenerator] Successfully generated 8 ScriptableObject assets, 18 Prefabs, and wired the scene.");
    }

    public static void EnsureAssetsAndSceneWired()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        GameConfigSO existingConfig = AssetDatabase.LoadAssetAtPath<GameConfigSO>($"{ConfigRoot}/DefaultGameConfig.asset");
        if (existingConfig == null)
        {
            GenerateAllAssetsAndPrefabs();
        }
        else
        {
            WireSceneComponents(existingConfig);
        }
    }

    private static void EnsureDirectories()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder(ConfigRoot);
        EnsureFolder(PlantsConfigDir);
        EnsureFolder(ZombiesConfigDir);
        EnsureFolder(MaterialsDir);
        EnsureFolder(PrefabsRoot);
        EnsureFolder(Units2DDir);
        EnsureFolder(ProjectilesDir);
        EnsureFolder(CollectiblesDir);
        EnsureFolder(GridPrefabsDir);
        EnsureFolder(Arena3DDir);
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;
        string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        string folderName = Path.GetFileName(assetPath);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void ConfigureSpriteImporters()
    {
        ConfigureSingleSprite("Assets/Sprites/Moonflower.png", new Vector2(0.5f, 0.15f), 128f);
        ConfigureSingleSprite("Assets/Sprites/Rose.png", new Vector2(0.5f, 0.15f), 128f);
        ConfigureSingleSprite("Assets/Sprites/Durian.png", new Vector2(0.5f, 0.15f), 128f);
        ConfigureSingleSprite("Assets/Sprites/Zombie.png", new Vector2(0.5f, 0.12f), 128f);
        ConfigureSingleSprite("Assets/Sprites/BucketheadZombie.png", new Vector2(0.5f, 0.12f), 128f);
        ConfigureSingleSprite("Assets/Sprites/BunnyZombie.png", new Vector2(0.5f, 0.12f), 128f);
        ConfigureSingleSprite("Assets/Sprites/HazmatZombie.png", new Vector2(0.5f, 0.12f), 128f);
        ConfigureSingleSprite("Assets/Sprites/Moon.png", new Vector2(0.5f, 0.5f), 128f);
        ConfigureSingleSprite("Assets/Sprites/RoseProjectile.png", new Vector2(0.5f, 0.5f), 100f);
        ConfigureSingleSprite("Assets/Sprites/AcidProjectile.png", new Vector2(0.5f, 0.5f), 100f);
    }

    private static void ConfigureSingleSprite(string path, Vector2 pivot, float ppu)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);

        bool needsReimport = importer.textureType != TextureImporterType.Sprite
            || importer.spriteImportMode != SpriteImportMode.Single
            || Mathf.Abs(importer.spritePixelsPerUnit - ppu) > 0.01f
            || Vector2.Distance(settings.spritePivot, pivot) > 0.005f
            || !importer.alphaIsTransparency;

        if (!needsReimport) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;

        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
    }

    private static GameObject CreatePlantPrefab2D(PlantType type, string prefabName, string spritePath)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        GameObject temp = new GameObject(prefabName);
        try
        {
            SpriteRenderer sr = temp.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 10;

            float targetHeight = 1.8f * 0.92f;
            float sprHeightUnits = sprite != null ? (sprite.rect.height / sprite.pixelsPerUnit) : 2.0f;
            float s = targetHeight / Mathf.Max(0.01f, sprHeightUnits);
            temp.transform.localScale = new Vector3(s, s, 1.0f);

            PlantBobber bobber = temp.AddComponent<PlantBobber>();
            if (type == PlantType.Moonflower)
            {
                bobber.breatheSpeed = 2.2f;
                bobber.swaySpeed = 1.8f;
                bobber.swayAngle = 3.0f;
                temp.AddComponent<MoonflowerPlant>();
            }
            else if (type == PlantType.Rose)
            {
                bobber.breatheSpeed = 2.6f;
                bobber.swaySpeed = 2.4f;
                bobber.swayAngle = 3.5f;
                temp.AddComponent<RosePlant>();
            }
            else
            {
                bobber.breatheSpeed = 1.6f;
                bobber.swaySpeed = 1.2f;
                bobber.swayAngle = 2.0f;
                temp.AddComponent<DurianPlant>();
            }

            return PrefabUtility.SaveAsPrefabAsset(temp, $"{Units2DDir}/{prefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    private static GameObject CreateZombiePrefab2D(ZombieType type, string prefabName, string spritePath)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        GameObject temp = new GameObject(prefabName);
        try
        {
            SpriteRenderer sr = temp.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 15;

            float targetHeight = 1.8f * 0.98f;
            float sprHeightUnits = sprite != null ? (sprite.rect.height / sprite.pixelsPerUnit) : 2.0f;
            float s = targetHeight / Mathf.Max(0.01f, sprHeightUnits);
            temp.transform.localScale = new Vector3(s, s, 1.0f);

            switch (type)
            {
                case ZombieType.Buckethead:
                    temp.AddComponent<BucketheadZombie>();
                    break;
                case ZombieType.Bunny:
                    temp.AddComponent<BunnyZombie>();
                    break;
                case ZombieType.Hazmat:
                    Sprite acidSpr = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/AcidProjectile.png");
                    if (acidSpr != null)
                    {
                        GameObject tankGlow = new GameObject("AcidTankGlow");
                        tankGlow.transform.SetParent(temp.transform, false);
                        tankGlow.transform.localPosition = new Vector3(0.32f, 0.88f, -0.02f);
                        tankGlow.transform.localScale = Vector3.one * 0.28f;
                        SpriteRenderer tankSr = tankGlow.AddComponent<SpriteRenderer>();
                        tankSr.sprite = acidSpr;
                        tankSr.sortingOrder = 16;
                        tankSr.color = new Color(0.55f, 1.0f, 0.35f, 0.72f);

                        GameObject nozzleGlow = new GameObject("NozzleGlow");
                        nozzleGlow.transform.SetParent(temp.transform, false);
                        nozzleGlow.transform.localPosition = new Vector3(-0.62f, 0.92f, -0.02f);
                        nozzleGlow.transform.localScale = Vector3.one * 0.22f;
                        SpriteRenderer nozzleSr = nozzleGlow.AddComponent<SpriteRenderer>();
                        nozzleSr.sprite = acidSpr;
                        nozzleSr.sortingOrder = 16;
                        nozzleSr.color = new Color(0.65f, 1.0f, 0.35f, 0.65f);
                    }
                    temp.AddComponent<HazmatZombie>();
                    break;
                default:
                    temp.AddComponent<NormalZombie>();
                    break;
            }

            return PrefabUtility.SaveAsPrefabAsset(temp, $"{Units2DDir}/{prefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    private static GameObject CreateProjectilePrefab(string prefabName, string spritePath, float scale, int sortingOrder, bool isRose)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        GameObject temp = new GameObject(prefabName);
        try
        {
            SpriteRenderer sr = temp.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortingOrder;
            float s = scale * 1.8f;
            temp.transform.localScale = new Vector3(s, s, 1.0f);

            if (isRose) temp.AddComponent<RoseProjectile>();
            else temp.AddComponent<HazmatAcidProjectile>();

            return PrefabUtility.SaveAsPrefabAsset(temp, $"{ProjectilesDir}/{prefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    private static GameObject CreateMoonPrefab()
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Moon.png");
        GameObject temp = new GameObject("Collectible_Moon");
        try
        {
            SpriteRenderer sr = temp.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 25;
            float s = 0.5f * 2.2f;
            temp.transform.localScale = new Vector3(s, s, 1.0f);
            temp.AddComponent<CollectibleMoon>();

            return PrefabUtility.SaveAsPrefabAsset(temp, $"{CollectiblesDir}/Collectible_Moon.prefab");
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    private static GameObject CreateTilePrefab(Dictionary<string, Material> materialCache)
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
        temp.name = "Grid_Tile";
        try
        {
            MaterialCache.DestroyCollider(temp);
            Renderer rend = temp.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = GetOrCreatePersistentMaterial(new Color(0.24f, 0.54f, 0.24f, 1.0f), 0.35f, materialCache);
            }
            return PrefabUtility.SaveAsPrefabAsset(temp, $"{GridPrefabsDir}/Grid_Tile.prefab");
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    private static GameObject CreateArena3DPlantPrefab(PlantType type, string prefabName, Dictionary<string, Material> materialCache)
    {
        GameObject temp = ArenaModelFactory.BuildProcedural3DPlantModel(type, prefabName);
        try
        {
            PersistMaterialsOnHierarchy(temp, materialCache);
            return PrefabUtility.SaveAsPrefabAsset(temp, $"{Arena3DDir}/{prefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    private static GameObject CreateArena3DZombiePrefab(ZombieType type, string prefabName, Dictionary<string, Material> materialCache)
    {
        GameObject temp = ArenaModelFactory.BuildProcedural3DZombieModel(type, prefabName, out _);
        try
        {
            PersistMaterialsOnHierarchy(temp, materialCache);
            return PrefabUtility.SaveAsPrefabAsset(temp, $"{Arena3DDir}/{prefabName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    private static void PersistMaterialsOnHierarchy(GameObject root, Dictionary<string, Material> cache)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].sharedMaterial;
            if (mat == null || AssetDatabase.Contains(mat)) continue;

            Color col = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
            float smoothness = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.35f;
            renderers[i].sharedMaterial = GetOrCreatePersistentMaterial(col, smoothness, cache);
        }
    }

    private static Material GetOrCreatePersistentMaterial(Color color, float smoothness, Dictionary<string, Material> cache)
    {
        Color32 c32 = color;
        int sm = Mathf.RoundToInt(Mathf.Clamp01(smoothness) * 100f);
        string key = $"{c32.r:X2}{c32.g:X2}{c32.b:X2}{c32.a:X2}_{sm}";

        if (cache.TryGetValue(key, out Material cached) && cached != null)
        {
            return cached;
        }

        string path = $"{MaterialsDir}/Mat_{key}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            cache[key] = existing;
            return existing;
        }

        Material created = new Material(MaterialCache.GetColoredMaterial(color, smoothness));
        AssetDatabase.CreateAsset(created, path);
        cache[key] = created;
        return created;
    }

    private static PlantDefinitionSO CreateOrUpdatePlantData(
        PlantType type,
        string assetPath,
        string spritePath,
        GameObject prefab2D,
        GameObject prefab3D)
    {
        PlantDefinitionSO asset = AssetDatabase.LoadAssetAtPath<PlantDefinitionSO>(assetPath);
        PlantDefinitionSO defaults = PlantDefinitionSO.CreateDefault(type);

        if (asset == null)
        {
            asset = defaults;
            AssetDatabase.CreateAsset(asset, assetPath);
        }
        else
        {
            EditorUtility.CopySerialized(defaults, asset);
            Object.DestroyImmediate(defaults);
        }

        asset.unitSprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        asset.prefab2D = prefab2D;
        asset.arenaVisualPrefab3D = prefab3D;
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static ZombieDefinitionSO CreateOrUpdateZombieData(
        ZombieType type,
        string assetPath,
        string spritePath,
        GameObject prefab2D,
        GameObject prefab3D)
    {
        ZombieDefinitionSO asset = AssetDatabase.LoadAssetAtPath<ZombieDefinitionSO>(assetPath);
        ZombieDefinitionSO defaults = ZombieDefinitionSO.CreateDefault(type);

        if (asset == null)
        {
            asset = defaults;
            AssetDatabase.CreateAsset(asset, assetPath);
        }
        else
        {
            EditorUtility.CopySerialized(defaults, asset);
            Object.DestroyImmediate(defaults);
        }

        asset.unitSprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        asset.prefab2D = prefab2D;
        asset.arenaVisualPrefab3D = prefab3D;
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static GameConfigSO CreateOrUpdateMasterConfig(
        GameObject tilePrefab,
        GameObject moonPrefab,
        GameObject roseProjPrefab,
        GameObject acidProjPrefab,
        PlantDefinitionSO[] plants,
        ZombieDefinitionSO[] zombies)
    {
        string path = $"{ConfigRoot}/DefaultGameConfig.asset";
        GameConfigSO asset = AssetDatabase.LoadAssetAtPath<GameConfigSO>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<GameConfigSO>();
            AssetDatabase.CreateAsset(asset, path);
        }

        asset.tilePrefab = tilePrefab;
        asset.collectibleMoonPrefab = moonPrefab;
        asset.roseProjectilePrefab = roseProjPrefab;
        asset.hazmatAcidProjectilePrefab = acidProjPrefab;
        asset.plantDefinitions = plants;
        asset.zombieDefinitions = zombies;
        asset.backgroundMusic2D = Resources.Load<AudioClip>("Audio/Gameplay2DTheme");
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static void WireSceneComponents(GameConfigSO config)
    {
        GridManager grid = Object.FindAnyObjectByType<GridManager>();
        if (grid != null)
        {
            grid.gameConfig = config;
            EnsureComponent<GridBoardRenderer>(grid.gameObject);
            EnsureComponent<MatchEconomyController>(grid.gameObject);
            EnsureComponent<GameplayAudioController>(grid.gameObject);
            EnsureComponent<GridHudRenderer>(grid.gameObject);
            EnsureComponent<ArenaCombatController>(grid.gameObject);
            EnsureComponent<HitscanRayParticleEffects>(grid.gameObject);
            EditorUtility.SetDirty(grid.gameObject);
        }

        GameObject playerObj = GameObject.Find("Player");
        if (playerObj != null)
        {
            Transform childPc = playerObj.transform.Find("PlayerController");
            if (childPc != null)
            {
                Undo.DestroyObjectImmediate(childPc.gameObject);
            }
            CapsuleCollider cap = playerObj.GetComponent<CapsuleCollider>();
            if (cap != null)
            {
                Undo.DestroyObjectImmediate(cap);
            }
            EditorUtility.SetDirty(playerObj);
        }

        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }
    }

    private static T EnsureComponent<T>(GameObject obj) where T : Component
    {
        T c = obj.GetComponent<T>();
        if (c == null) c = obj.AddComponent<T>();
        return c;
    }
}
#endif
