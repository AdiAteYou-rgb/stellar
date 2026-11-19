using System;
using System.Collections.Generic;
using UnityEngine;
using Stellar.Combat;

/// <summary>
/// Central coordinator for the 5x8 2D defense wall, match state, role permissions,
/// and transitions into the 3D 1v1 combat arena. Delegates rendering, economy, audio,
/// HUD, and 3D arena combat to dedicated single-responsibility subsystems.
/// </summary>
[ExecuteAlways]
public class GridManager : MonoBehaviour
{
    [Header("Data Asset Configuration")]
    public GameConfigSO gameConfig;

    [Header("Grid Dimensions (5x8 Plant Spots)")]
    public int rows = 5;
    public int columns = 8;

    [Header("Wall 16:9 Dimensions")]
    public float wallWidth = 16.0f;
    public float wallHeight = 9.0f;
    public float wallThickness = 0.2f;
    public float tilePadding = 0.06f;

    [Header("Camera Alignment")]
    public Camera targetCamera;
    public float cameraDistance = 10.0f;
    public bool enforce16By9Viewport = true;
    public bool lockCameraToThisWall = false;

    [Header("Assigned Role")]
    public MultiplayerMenuManager.PlayerRole localRole = MultiplayerMenuManager.PlayerRole.None;

    public enum MatchResult
    {
        InProgress,
        PlantsWin,
        ZombiesWin
    }

    [Header("Match Timer & Win Conditions (5-Minute Defense)")]
    public float matchDuration = 300.0f;
    public float matchTimeRemaining = 300.0f;
    public MatchResult matchResult = MatchResult.InProgress;

    public float DefenseLineX => transform.position.x - (wallWidth * 0.5f);

    public event Action<MatchResult> OnMatchEndedLocal;
    public event Action OnMatchRestartedLocal;

    [Header("Gameplay & Pause State")]
    public bool gameplayActive = true;
    public bool canPlacePlants = true;
    public bool canPlaceZombies = true;
    public bool is2DWorldPaused = false;

    [Header("Moon Economy & Cluster Pickup")]
    public int currentMoons = 100;
    public float skyMoonInterval = 15.0f;
    public int skyMoonValue = 50;
    [Tooltip("Radius around your click that automatically picks up all nearby gathered moons.")]
    public float moonClusterPickupRadius = 3.2f;

    [Header("Plant Costs & Cooldowns (PvZ Style)")]
    public int moonflowerCost = 50;
    public float moonflowerCooldown = 5.0f;
    public int roseCost = 100;
    public float roseCooldown = 7.5f;
    public int rozeCost { get => roseCost; set => roseCost = value; }
    public float rozeCooldown { get => roseCooldown; set => roseCooldown = value; }
    public int durianCost = 50;
    public float durianCooldown = 20.0f;

    [Header("Zombie Player: Brains & Zombie Spawning (PvZ Cooldowns)")]
    public int currentBrains = 150;
    public int zombieCost = 50;
    public float zombieCooldown = 5.0f;
    public int bucketheadCost = 100;
    public float bucketheadCooldown = 12.0f;
    public int bunnyCost = 75;
    public float bunnyCooldown = 7.5f;
    public int hazmatCost = 100;
    public float hazmatCooldown = 9.0f;
    public float passiveBrainInterval = 10.0f;
    public int passiveBrainAmount = 25;

    [Header("Zombie Placement Restriction")]
    [Tooltip("Number of columns on the right edge where zombies are allowed to spawn.")]
    public int zombieAllowedColumnsFromRight = 2;

    [Header("Current Selection")]
    public bool isPlantSelected = false;
    public PlantType selectedPlantType = PlantType.Moonflower;
    public bool isZombieSelected = false;
    public ZombieType selectedZombieType = ZombieType.Normal;

    [Header("Clash Zoom & 3D 1v1 Arena")]
    public float clashZoomDuration = 1.35f;
    public float clashTargetOrthoSize = 1.15f;
    public float moonflowerBurstCooldown = 10.0f;
    public int moonflowerBurstDamage = 20;
    public float moonflowerBurstRadius = 4.6f;
    [HideInInspector] public float moonBeamChargeDuration = 1.6f;
    public float roseThornChargeDuration = 0.25f;
    public float durianRollDuration = 2.6f;
    public float bucketParryDuration = 1.0f;
    public float bucketParryCooldown = 5.0f;
    public float bucketParrySpeedDuration = 3.0f;
    public float bunnySlideDuration = 0.90f;
    public float bunnySlideCooldown = 3.0f;
    public float feralLungeDuration = 0.65f;
    public float feralLungeCooldown = 3.5f;
    public float hazmatSludgeCooldown = 5.0f;
    public float hazmatDisruptDuration = 2.0f;
    public float arenaResultPauseDuration = 2.5f;

    [Header("Visual Colors")]
    public Color tileColorLight = new Color(0.24f, 0.54f, 0.24f, 1.0f);
    public Color tileColorDark = new Color(0.18f, 0.44f, 0.18f, 1.0f);
    public Color tileHoverColor = new Color(0.45f, 0.92f, 0.45f, 1.0f);
    public Color tileZombieHoverColor = new Color(0.82f, 0.38f, 0.98f, 1.0f);
    public Color tileOccupiedColor = new Color(0.85f, 0.30f, 0.25f, 1.0f);

    [Header("Hitscan Ray Abilities")]
    [SerializeField] private HitscanAttackConfig moonflowerSolarBoltHitscan = new HitscanAttackConfig("Solar Bolt", true, 60f);
    [SerializeField] private HitscanAttackConfig roseThornHitscan = new HitscanAttackConfig("Rose Thorn", true, 65f);
    [SerializeField] private HitscanAttackConfig roseHeavyVolleyHitscan = new HitscanAttackConfig("Rose Heavy Volley", true, 65f);
    [SerializeField] private HitscanAttackConfig hazmatAcidShotHitscan = new HitscanAttackConfig("Hazmat Acid Shot", true, 65f);
    [SerializeField] private HitscanAttackConfig hazmatSludgeBlastHitscan = new HitscanAttackConfig("Hazmat Bio-Disruptor", true, 55f);

    [Header("2D Scene Background Music")]
    public AudioClip backgroundMusic2D;
    [Range(0f, 1f)] public float backgroundMusicVolume = 0.65f;

    // ==================== NETWORK SYNC EVENTS ====================
    public event Action<int, int, PlantType> OnPlantPlacedLocal;
    public event Action<int, int, ZombieType> OnZombiePlacedLocal;
    public event Action<int, Vector3, float, int> OnSkyMoonSpawnedLocal;
    public event Action<int, Vector3, int> OnFlowerMoonSpawnedLocal;
    public event Action<List<int>> OnMoonsCollectedLocal;
    public event Action<int, int, PlantType, MultiplayerMenuManager.PlayerRole, ZombieType, int> OnClashStartedLocal;
    public event Action<Vector3, float, int, int> OnArenaPoseLocal;
    public event Action<bool, int, int, int> OnArenaResolvedLocal;
    public event Action<float> OnArenaStunLocal;
    public event Action<float> OnArenaDisruptLocal;
    public event Action<string, Vector3, Vector3> OnArenaAbilityFxLocal;
    public event Action<HitscanResult> OnArenaHitscanFiredLocal;
    public event Action<int> OnArenaSeedLocal;
    public event Action<int, float> OnArenaAttackHitLocal;
    public event Action<int, int> OnArenaParriedLocal;
    public event Action<Vector3> OnArenaKnockbackLocal;

    // ==================== SUBSYSTEM COMPONENTS ====================
    private GridBoardRenderer boardRenderer;
    private MatchEconomyController economyController;
    private GameplayAudioController audioController;
    private GridHudRenderer hudRenderer;
    private ArenaCombatController arenaController;

    private PlaceholderPlant[,] placedPlants;
    private MultiplayerMenuManager cachedMenuManager;
    private PlayerController cachedPlayerController;

    public GameConfigSO GameConfig => gameConfig;
    public Transform TilesContainerTransform => boardRenderer != null && boardRenderer.TilesContainer != null ? boardRenderer.TilesContainer.transform : null;
    public bool IsIn3DArena => arenaController != null && arenaController.IsIn3DArena;
    public bool IsMoonAuthority => GetEffectiveLocalRole() != MultiplayerMenuManager.PlayerRole.Zombie;
    public MultiplayerMenuManager.PlayerRole EffectiveRole => GetEffectiveLocalRole();
    public float TileWidth => columns > 0 ? wallWidth / columns : 2.0f;
    public float TileHeight => rows > 0 ? wallHeight / rows : 1.8f;

    private void Awake()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        SyncWallScale();

        if (Application.isPlaying)
        {
            LoadAndApplyConfig();
            EnsureSubsystems();
            InitializeRuntimeGrid();

            if (GetComponent<HitscanRayParticleEffects>() == null)
            {
                gameObject.AddComponent<HitscanRayParticleEffects>();
            }
        }
    }

    private void OnEnable()
    {
        SyncWallScale();
    }

    private void OnValidate()
    {
        rows = Mathf.Max(1, rows);
        columns = Mathf.Max(1, columns);
        wallWidth = Mathf.Max(1f, wallWidth);
        wallHeight = Mathf.Max(1f, wallHeight);
        SyncWallScale();
    }

    private void LoadAndApplyConfig()
    {
        if (gameConfig == null)
        {
            gameConfig = GameConfigSO.LoadOrCreateDefault();
        }
        if (gameConfig == null) return;

        rows = Mathf.Max(1, gameConfig.rows);
        columns = Mathf.Max(1, gameConfig.columns);
        wallWidth = Mathf.Max(1f, gameConfig.wallWidth);
        wallHeight = Mathf.Max(1f, gameConfig.wallHeight);
        wallThickness = gameConfig.wallThickness;
        tilePadding = gameConfig.tilePadding;

        matchDuration = gameConfig.matchDuration;
        matchTimeRemaining = gameConfig.matchDuration;
        zombieAllowedColumnsFromRight = gameConfig.zombieAllowedColumnsFromRight;

        currentMoons = gameConfig.startingMoons;
        skyMoonInterval = gameConfig.skyMoonInterval;
        skyMoonValue = gameConfig.skyMoonValue;
        moonClusterPickupRadius = gameConfig.moonClusterPickupRadius;

        currentBrains = gameConfig.startingBrains;
        passiveBrainInterval = gameConfig.passiveBrainInterval;
        passiveBrainAmount = gameConfig.passiveBrainAmount;

        clashZoomDuration = gameConfig.clashZoomDuration;
        clashTargetOrthoSize = gameConfig.clashTargetOrthoSize;
        arenaResultPauseDuration = gameConfig.arenaResultPauseDuration;

        tileColorLight = gameConfig.tileColorLight;
        tileColorDark = gameConfig.tileColorDark;
        tileHoverColor = gameConfig.tileHoverColor;
        tileZombieHoverColor = gameConfig.tileZombieHoverColor;
        tileOccupiedColor = gameConfig.tileOccupiedColor;

        if (backgroundMusic2D == null && gameConfig.backgroundMusic2D != null)
        {
            backgroundMusic2D = gameConfig.backgroundMusic2D;
        }
        backgroundMusicVolume = gameConfig.backgroundMusicVolume;

        PlantDefinitionSO mf = gameConfig.GetPlantDefinition(PlantType.Moonflower);
        moonflowerCost = mf.cost;
        moonflowerCooldown = mf.cooldown;
        moonflowerBurstCooldown = mf.secondaryAbilityCooldown;
        moonflowerBurstDamage = mf.secondaryAbilityDamage;
        moonflowerBurstRadius = mf.burstRadius;

        PlantDefinitionSO rose = gameConfig.GetPlantDefinition(PlantType.Rose);
        roseCost = rose.cost;
        roseCooldown = rose.cooldown;
        roseThornChargeDuration = rose.chargeDuration;

        PlantDefinitionSO durian = gameConfig.GetPlantDefinition(PlantType.Durian);
        durianCost = durian.cost;
        durianCooldown = durian.cooldown;
        durianRollDuration = durian.rollDuration;

        ZombieDefinitionSO zNorm = gameConfig.GetZombieDefinition(ZombieType.Normal);
        zombieCost = zNorm.cost;
        zombieCooldown = zNorm.cooldown;
        feralLungeDuration = zNorm.activeAbilityDuration;
        feralLungeCooldown = zNorm.secondaryAbilityCooldown;

        ZombieDefinitionSO zBuck = gameConfig.GetZombieDefinition(ZombieType.Buckethead);
        bucketheadCost = zBuck.cost;
        bucketheadCooldown = zBuck.cooldown;
        bucketParryDuration = zBuck.activeAbilityDuration;
        bucketParryCooldown = zBuck.secondaryAbilityCooldown;
        bucketParrySpeedDuration = zBuck.secondaryEffectDuration;

        ZombieDefinitionSO zBun = gameConfig.GetZombieDefinition(ZombieType.Bunny);
        bunnyCost = zBun.cost;
        bunnyCooldown = zBun.cooldown;
        bunnySlideDuration = zBun.activeAbilityDuration;
        bunnySlideCooldown = zBun.secondaryAbilityCooldown;

        ZombieDefinitionSO zHaz = gameConfig.GetZombieDefinition(ZombieType.Hazmat);
        hazmatCost = zHaz.cost;
        hazmatCooldown = zHaz.cooldown;
        hazmatSludgeCooldown = zHaz.secondaryAbilityCooldown;
        hazmatDisruptDuration = zHaz.secondaryEffectDuration;
    }

    private void EnsureSubsystems()
    {
        boardRenderer = GetOrAddSubsystem<GridBoardRenderer>();
        economyController = GetOrAddSubsystem<MatchEconomyController>();
        audioController = GetOrAddSubsystem<GameplayAudioController>();
        hudRenderer = GetOrAddSubsystem<GridHudRenderer>();
        arenaController = GetOrAddSubsystem<ArenaCombatController>();

        economyController.Initialize(this);
        audioController.Initialize(this);
        hudRenderer.Initialize(this);
        arenaController.Initialize(this);
    }

    private T GetOrAddSubsystem<T>() where T : Component
    {
        T comp = GetComponent<T>();
        if (comp == null)
        {
            comp = gameObject.AddComponent<T>();
        }
        return comp;
    }

    public PlantDefinitionSO GetPlantDefinition(PlantType type)
    {
        return gameConfig != null ? gameConfig.GetPlantDefinition(type) : PlantDefinitionSO.CreateDefault(type);
    }

    public ZombieDefinitionSO GetZombieDefinition(ZombieType type)
    {
        return gameConfig != null ? gameConfig.GetZombieDefinition(type) : ZombieDefinitionSO.CreateDefault(type);
    }

    // ==================== CAMERA & WALL ALIGNMENT ====================

    public Camera GetTargetCamera()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        return targetCamera;
    }

    public void SyncWallScale()
    {
        Vector3 targetScale = new Vector3(wallWidth, wallHeight, wallThickness);
        if (transform.localScale != targetScale) transform.localScale = targetScale;
        if (transform.rotation != Quaternion.identity) transform.rotation = Quaternion.identity;

        Camera cam = GetTargetCamera();
        bool inArenaOrZoom = arenaController != null && (arenaController.IsIn3DArena || arenaController.IsZoomingIntoClash);
        if (cam != null && enforce16By9Viewport && !inArenaOrZoom)
        {
            if (!cam.orthographic || Mathf.Abs(cam.orthographicSize - wallHeight * 0.5f) > 0.001f)
            {
                cam.orthographic = true;
                cam.orthographicSize = wallHeight * 0.5f;
            }
            Apply16By9Viewport(cam);
        }
    }

    [ContextMenu("Align Camera & Wall (16:9)")]
    public void SyncWallScaleAndCamera()
    {
        SyncWallScale();

        Camera cam = GetTargetCamera();
        bool inArenaOrZoom = arenaController != null && (arenaController.IsIn3DArena || arenaController.IsZoomingIntoClash);
        if (cam != null && !inArenaOrZoom)
        {
            if (!cam.orthographic || Mathf.Abs(cam.orthographicSize - wallHeight * 0.5f) > 0.001f)
            {
                cam.orthographic = true;
                cam.orthographicSize = wallHeight * 0.5f;
            }
            Vector3 idealPos = GetIdealCameraPosition();
            if (Vector3.Distance(cam.transform.position, idealPos) > 0.001f) cam.transform.position = idealPos;
            Quaternion idealRot = Quaternion.LookRotation(transform.forward, Vector3.up);
            if (cam.transform.rotation != idealRot) cam.transform.rotation = idealRot;

            if (enforce16By9Viewport) Apply16By9Viewport(cam);
        }
    }

    public Vector3 GetIdealCameraPosition() => transform.position - transform.forward * cameraDistance;

    public void Apply16By9Viewport(Camera cam)
    {
        if (cam == null) return;
        float targetAspect = 16.0f / 9.0f;
        float windowAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float scaleHeight = windowAspect / targetAspect;

        Rect targetRect;
        if (Mathf.Abs(scaleHeight - 1.0f) < 0.01f)
        {
            targetRect = new Rect(0f, 0f, 1f, 1f);
        }
        else if (scaleHeight < 1.0f)
        {
            targetRect = new Rect(0f, (1.0f - scaleHeight) * 0.5f, 1.0f, scaleHeight);
        }
        else
        {
            float scaleWidth = 1.0f / scaleHeight;
            targetRect = new Rect((1.0f - scaleWidth) * 0.5f, 0f, scaleWidth, 1.0f);
        }

        if (cam.rect != targetRect) cam.rect = targetRect;
    }

    private void InitializeRuntimeGrid()
    {
        EnsureSubsystems();
        placedPlants = new PlaceholderPlant[rows, columns];
        boardRenderer.InitializeBoard(this);
    }

    // ==================== MAIN UPDATE LOOP ====================

    private void Update()
    {
        if (!Application.isPlaying)
        {
            SyncWallScale();
            return;
        }

        EnsureSubsystems();
        Update2DBackgroundMusicPublic();

        if (placedPlants == null || placedPlants.GetLength(0) != rows || placedPlants.GetLength(1) != columns)
        {
            InitializeRuntimeGrid();
        }

        if (arenaController.IsIn3DArena)
        {
            MultiplayerMenuManager menu = GetMenuManager();
            bool isHostOrSolo = menu == null || menu.isHost || menu.localRole == MultiplayerMenuManager.PlayerRole.None;
            arenaController.TickArena(isHostOrSolo);
            return;
        }

        if (arenaController.IsZoomingIntoClash)
        {
            arenaController.UpdateClashZoomAnimation();
            return;
        }

        if (lockCameraToThisWall) SyncWallScaleAndCamera();
        else SyncWallScale();

        ApplyRolePlacementPermissions();

        if (!gameplayActive || is2DWorldPaused || matchResult != MatchResult.InProgress)
        {
            boardRenderer.HideAllPreviewGhosts();
            return;
        }

        matchTimeRemaining -= Time.deltaTime;
        if (matchTimeRemaining <= 0f)
        {
            matchTimeRemaining = 0f;
            TriggerPlantsWin(notifyNetwork: true);
            return;
        }

        economyController.TickEconomy(Time.deltaTime);
        Handle2DSelectionHotkeys();

        boardRenderer.UpdateHoveredTile(GetTargetCamera());
        boardRenderer.UpdateTileHighlightsAndGhost(
            placedPlants,
            isPlantSelected,
            selectedPlantType,
            isZombieSelected,
            selectedZombieType,
            CanAffordAndReady(selectedPlantType),
            CanAffordAndReadyZombie(selectedZombieType));
        HandleMouseClick();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || arenaController == null) return;
        arenaController.LateTickArena();
    }

    private void ApplyRolePlacementPermissions()
    {
        if (localRole == MultiplayerMenuManager.PlayerRole.Plant)
        {
            canPlacePlants = true;
            canPlaceZombies = false;
        }
        else if (localRole == MultiplayerMenuManager.PlayerRole.Zombie)
        {
            canPlacePlants = false;
            canPlaceZombies = true;
        }
    }

    private void Handle2DSelectionHotkeys()
    {
        if (canPlacePlants)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) TrySelectPlant(PlantType.Moonflower);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) TrySelectPlant(PlantType.Rose);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) TrySelectPlant(PlantType.Durian);
        }

        if (canPlaceZombies)
        {
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) TrySelectZombie(ZombieType.Normal);
            else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) TrySelectZombie(ZombieType.Buckethead);
            else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) TrySelectZombie(ZombieType.Bunny);
            else if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) TrySelectZombie(ZombieType.Hazmat);
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            isPlantSelected = false;
            isZombieSelected = false;
        }
    }

    private void HandleMouseClick()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        if (economyController.TryClickCollectibleMoon(GetTargetCamera())) return;

        Vector2 guiMousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (hudRenderer.IsPointOverTopHud(guiMousePos)) return;

        int hoveredRow = boardRenderer.HoveredRow;
        int hoveredCol = boardRenderer.HoveredCol;
        if (hoveredRow < 0 || hoveredCol < 0) return;

        if (isZombieSelected && CanAffordAndReadyZombie(selectedZombieType))
        {
            if (!IsColumnAllowedForZombie(hoveredCol)) return;

            economyController.SpendForZombie(selectedZombieType);
            SpawnZombieOnTile(hoveredRow, hoveredCol, selectedZombieType, notifyNetwork: true);
            isZombieSelected = false;
            return;
        }

        if (isPlantSelected && CanAffordAndReady(selectedPlantType))
        {
            if (placedPlants[hoveredRow, hoveredCol] == null)
            {
                economyController.SpendForPlant(selectedPlantType);
                PlacePlantOnTile(hoveredRow, hoveredCol, selectedPlantType, notifyNetwork: true);
                isPlantSelected = false;
            }
        }
    }

    // ==================== BOARD QUERIES & UNIT SPAWNING ====================

    public bool HasZombieInRow(int row, float minX)
    {
        for (int i = 0; i < WallZombie.ActiveZombies.Count; i++)
        {
            WallZombie z = WallZombie.ActiveZombies[i];
            if (z != null && z.row == row && z.transform.position.x >= minX) return true;
        }
        return false;
    }

    public PlaceholderPlant GetPlantTouchedByZombie(int row, float zombieX)
    {
        if (placedPlants == null || row < 0 || row >= rows) return null;

        for (int c = 0; c < columns; c++)
        {
            PlaceholderPlant plant = placedPlants[row, c];
            if (plant != null && Mathf.Abs(zombieX - plant.transform.position.x) <= 0.72f)
            {
                return plant;
            }
        }
        return null;
    }

    public bool HasPlantAheadInRow(int row, float zombieX, float maxRange)
    {
        if (placedPlants == null || row < 0 || row >= rows) return false;

        for (int c = 0; c < columns; c++)
        {
            PlaceholderPlant plant = placedPlants[row, c];
            if (plant != null)
            {
                float dx = zombieX - plant.transform.position.x;
                if (dx > 0.25f && dx <= maxRange) return true;
            }
        }
        return false;
    }

    public PlaceholderPlant GetPlantHitByProjectile(int row, float projectileX, float hitRadius = 0.45f)
    {
        if (placedPlants == null || row < 0 || row >= rows) return null;

        for (int c = columns - 1; c >= 0; c--)
        {
            PlaceholderPlant plant = placedPlants[row, c];
            if (plant != null && Mathf.Abs(projectileX - plant.transform.position.x) <= hitRadius)
            {
                return plant;
            }
        }
        return null;
    }

    public PlaceholderPlant GetPlacedPlantAt(int row, int col)
    {
        if (placedPlants == null || row < 0 || row >= rows || col < 0 || col >= columns) return null;
        return placedPlants[row, col];
    }

    public void ClearPlantSlot(int row, int col, PlaceholderPlant expected)
    {
        if (placedPlants != null && row >= 0 && row < rows && col >= 0 && col < columns && placedPlants[row, col] == expected)
        {
            placedPlants[row, col] = null;
        }
    }

    public void DamagePlantOnTile(PlaceholderPlant plant, int damage)
    {
        if (plant == null) return;
        plant.TakeDamage(damage);
    }

    public WallZombie FindClosestZombieInRow(int row, Vector3 refPos)
    {
        WallZombie best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < WallZombie.ActiveZombies.Count; i++)
        {
            WallZombie z = WallZombie.ActiveZombies[i];
            if (z == null || !z.gameObject.activeInHierarchy || z.row != row) continue;
            float d = Mathf.Abs(z.transform.position.x - refPos.x);
            if (d < bestDist)
            {
                bestDist = d;
                best = z;
            }
        }
        return best;
    }

    public Vector3 GetTileWorldPosition(int row, int column, float surfaceZOffset = -0.05f)
    {
        EnsureSubsystems();
        return boardRenderer.GetTileWorldPosition(row, column, surfaceZOffset);
    }

    public WallZombie SpawnZombieOnTile(int row, int col, ZombieType type = ZombieType.Normal, bool notifyNetwork = false)
    {
        if (row < 0 || row >= rows) return null;

        int clampedCol = Mathf.Clamp(col, 0, columns - 1);
        Vector3 spawnPos = GetTileWorldPosition(row, clampedCol, -0.3f);

        ZombieDefinitionSO def = GetZombieDefinition(type);
        GameObject prefab = def != null ? def.prefab2D : null;
        if (prefab == null) prefab = Resources.Load<GameObject>($"Prefabs/Units2D/Zombie_{type}");

        GameObject zombieObj = prefab != null
            ? Instantiate(prefab)
            : GameVisuals.CreateZombie(type, $"Zombie_{type}_{row}_{clampedCol}", isGhost: false, TileWidth, TileHeight);
        zombieObj.name = $"Zombie_{type}_{row}_{clampedCol}";

        if (TilesContainerTransform != null)
        {
            zombieObj.transform.SetParent(TilesContainerTransform, true);
        }

        WallZombie zombieComp = EnsureZombieComponent(zombieObj, type);
        zombieComp.Initialize(this, row, spawnPos, type);

        if (notifyNetwork) OnZombiePlacedLocal?.Invoke(row, clampedCol, type);
        return zombieComp;
    }

    public WallZombie SpawnZombieOnTile(int row, int col, bool notifyNetwork) => SpawnZombieOnTile(row, col, ZombieType.Normal, notifyNetwork);

    private static WallZombie EnsureZombieComponent(GameObject zombieObj, ZombieType type)
    {
        WallZombie existing = zombieObj.GetComponent<WallZombie>();
        if (existing != null) return existing;

        switch (type)
        {
            case ZombieType.Buckethead: return zombieObj.AddComponent<BucketheadZombie>();
            case ZombieType.Bunny:      return zombieObj.AddComponent<BunnyZombie>();
            case ZombieType.Hazmat:     return zombieObj.AddComponent<HazmatZombie>();
            default:                    return zombieObj.AddComponent<NormalZombie>();
        }
    }

    public PlaceholderPlant PlacePlantOnTile(int row, int col, PlantType type = PlantType.Moonflower, bool notifyNetwork = false)
    {
        if (row < 0 || row >= rows || col < 0 || col >= columns) return null;

        if (placedPlants == null) InitializeRuntimeGrid();
        if (placedPlants[row, col] != null) return placedPlants[row, col];

        PlantType normalized = type == PlantType.Roze ? PlantType.Rose : type;
        Vector3 spawnPos = GetTileWorldPosition(row, col, -0.25f);

        PlantDefinitionSO def = GetPlantDefinition(normalized);
        GameObject prefab = def != null ? def.prefab2D : null;
        if (prefab == null) prefab = Resources.Load<GameObject>($"Prefabs/Units2D/Plant_{normalized}");

        GameObject plantObj = prefab != null
            ? Instantiate(prefab)
            : GameVisuals.CreatePlant(normalized, $"{normalized}_{row}_{col}", isGhost: false, TileWidth, TileHeight);
        plantObj.name = $"{normalized}_{row}_{col}";
        plantObj.transform.position = spawnPos;

        if (TilesContainerTransform != null)
        {
            plantObj.transform.SetParent(TilesContainerTransform, true);
        }

        plantObj.GetComponent<PlantBobber>()?.SetBasePosition(plantObj.transform.localPosition);

        PlaceholderPlant plantComp = EnsurePlantComponent(plantObj, normalized);
        plantComp.Initialize(this, normalized, row, col);
        placedPlants[row, col] = plantComp;

        if (notifyNetwork) OnPlantPlacedLocal?.Invoke(row, col, normalized);
        return plantComp;
    }

    private static PlaceholderPlant EnsurePlantComponent(GameObject plantObj, PlantType type)
    {
        PlaceholderPlant existing = plantObj.GetComponent<PlaceholderPlant>();
        if (existing != null) return existing;

        switch (type)
        {
            case PlantType.Moonflower: return plantObj.AddComponent<MoonflowerPlant>();
            case PlantType.Rose:       return plantObj.AddComponent<RosePlant>();
            default:                   return plantObj.AddComponent<DurianPlant>();
        }
    }

    public void SpawnRoseProjectile(int row, Vector3 spawnPos)
    {
        GameObject prefab = gameConfig != null ? gameConfig.roseProjectilePrefab : null;
        GameObject proj = prefab != null ? Instantiate(prefab) : GameVisuals.CreateRoseProjectile("RoseThornProjectile");
        proj.name = "RoseThornProjectile";

        if (TilesContainerTransform != null) proj.transform.SetParent(TilesContainerTransform, true);
        proj.transform.position = spawnPos;

        RoseProjectile rp = proj.GetComponent<RoseProjectile>();
        if (rp == null) rp = proj.AddComponent<RoseProjectile>();
        rp.Initialize(this, row, transform.position.x + wallWidth * 0.5f);
    }

    public void SpawnRoseProjectile(Vector3 spawnPos) => SpawnRoseProjectile(0, spawnPos);

    public void SpawnHazmatAcidProjectile(int row, Vector3 spawnPos, int damage = 30)
    {
        GameObject prefab = gameConfig != null ? gameConfig.hazmatAcidProjectilePrefab : null;
        GameObject proj = prefab != null ? Instantiate(prefab) : GameVisuals.CreateAcidProjectile("HazmatAcidGlob");
        proj.name = "HazmatAcidGlob";

        if (TilesContainerTransform != null) proj.transform.SetParent(TilesContainerTransform, true);
        proj.transform.position = spawnPos;

        HazmatAcidProjectile ap = proj.GetComponent<HazmatAcidProjectile>();
        if (ap == null) ap = proj.AddComponent<HazmatAcidProjectile>();
        ap.Initialize(this, row, damage, DefenseLineX);
    }

    // ==================== ROLE, ECONOMY & SELECTION FORWARDING ====================

    private MultiplayerMenuManager GetMenuManager()
    {
        if (cachedMenuManager == null) cachedMenuManager = FindAnyObjectByType<MultiplayerMenuManager>();
        return cachedMenuManager;
    }

    public MultiplayerMenuManager.PlayerRole GetEffectiveLocalRole()
    {
        if (localRole != MultiplayerMenuManager.PlayerRole.None) return localRole;

        MultiplayerMenuManager menu = GetMenuManager();
        if (menu != null)
        {
            if (menu.localRole != MultiplayerMenuManager.PlayerRole.None)
            {
                localRole = menu.localRole;
                return localRole;
            }
            if (menu.isHost)
            {
                localRole = menu.hostRole != MultiplayerMenuManager.PlayerRole.None ? menu.hostRole : MultiplayerMenuManager.PlayerRole.Plant;
                return localRole;
            }
            else
            {
                localRole = menu.joinerRole != MultiplayerMenuManager.PlayerRole.None ? menu.joinerRole : MultiplayerMenuManager.PlayerRole.Zombie;
                return localRole;
            }
        }

        return localRole;
    }

    public void SetLocalRole(MultiplayerMenuManager.PlayerRole role)
    {
        localRole = role;
        if (role == MultiplayerMenuManager.PlayerRole.Plant)
        {
            canPlacePlants = true;
            canPlaceZombies = false;
        }
        else if (role == MultiplayerMenuManager.PlayerRole.Zombie)
        {
            canPlacePlants = false;
            canPlaceZombies = true;
        }
        else
        {
            canPlacePlants = true;
            canPlaceZombies = true;
        }

        if (!canPlacePlants) isPlantSelected = false;
        if (!canPlaceZombies) isZombieSelected = false;
        HideAllPreviewGhosts();
    }

    public void HideAllPreviewGhosts()
    {
        boardRenderer?.HideAllPreviewGhosts();
    }

    public int GetPlantCost(PlantType type) { EnsureSubsystems(); return economyController.GetPlantCost(type); }
    public float GetPlantMaxCooldown(PlantType type) { EnsureSubsystems(); return economyController.GetPlantMaxCooldown(type); }
    public float GetPlantCooldownRemaining(PlantType type) { EnsureSubsystems(); return economyController.GetPlantCooldownRemaining(type); }
    public bool CanAffordAndReady(PlantType type) { EnsureSubsystems(); return economyController.CanAffordAndReadyPlant(type); }

    public int GetZombieCost(ZombieType type) { EnsureSubsystems(); return economyController.GetZombieCost(type); }
    public float GetZombieMaxCooldown(ZombieType type) { EnsureSubsystems(); return economyController.GetZombieMaxCooldown(type); }
    public float GetZombieCooldownRemaining(ZombieType type) { EnsureSubsystems(); return economyController.GetZombieCooldownRemaining(type); }
    public bool CanAffordAndReadyZombie(ZombieType type) { EnsureSubsystems(); return economyController.CanAffordAndReadyZombie(type); }
    public bool CanAffordAndReadyZombie() => CanAffordAndReadyZombie(selectedZombieType);

    public void TrySelectPlant(PlantType type)
    {
        if (!canPlacePlants) return;
        isZombieSelected = false;

        if (isPlantSelected && selectedPlantType == type)
        {
            isPlantSelected = false;
            return;
        }
        if (!CanAffordAndReady(type)) return;

        selectedPlantType = type;
        isPlantSelected = true;
    }

    public void TrySelectZombie(ZombieType type = ZombieType.Normal)
    {
        if (!canPlaceZombies) return;
        isPlantSelected = false;

        if (isZombieSelected && selectedZombieType == type)
        {
            isZombieSelected = false;
            return;
        }
        if (!CanAffordAndReadyZombie(type)) return;

        selectedZombieType = type;
        isZombieSelected = true;
    }

    public bool IsColumnAllowedForZombie(int col) => col >= Mathf.Max(0, columns - zombieAllowedColumnsFromRight);

    public void AddMoons(int amount) { EnsureSubsystems(); economyController.AddMoons(amount); }
    public Vector3 GetMoonBankWorldPosition() { EnsureSubsystems(); return hudRenderer.GetMoonBankWorldPosition(GetTargetCamera()); }
    public void SpawnSkyFallingMoon() { EnsureSubsystems(); economyController.SpawnSkyFallingMoon(); }
    public void SpawnSkyMoonWithId(int id, Vector3 startPos, float landingY, int value) { EnsureSubsystems(); economyController.SpawnSkyMoonWithId(id, startPos, landingY, value); }
    public void SpawnMoonFromFlower(Vector3 flowerWorldPos, int amount = 50) { EnsureSubsystems(); economyController.SpawnMoonFromFlower(flowerWorldPos, amount); }
    public void SpawnFlowerMoonWithId(int id, Vector3 flowerWorldPos, int amount) { EnsureSubsystems(); economyController.SpawnFlowerMoonWithId(id, flowerWorldPos, amount); }
    public void RemoveMoonsRemote(List<int> ids) { EnsureSubsystems(); economyController.RemoveMoonsRemote(ids); }

    // ==================== CLASH & 3D ARENA FORWARDING ====================

    public void TriggerZombiePlantClash(WallZombie zombie, PlaceholderPlant plant, bool notifyNetwork = true)
    {
        PlantType pType = (plant != null) ? plant.plantType : PlantType.Rose;
        TriggerZombiePlantClash(zombie, plant, pType, notifyNetwork);
    }

    public void TriggerZombiePlantClash(WallZombie zombie, PlaceholderPlant plant, PlantType fallbackType, bool notifyNetwork = true)
    {
        EnsureSubsystems();
        arenaController.TriggerZombiePlantClash(zombie, plant, fallbackType, notifyNetwork);
    }

    public void TriggerRemoteClash(int row, int col)
    {
        TriggerRemoteClash(row, col, PlantType.Rose, MultiplayerMenuManager.PlayerRole.None, ZombieType.Normal, 0);
    }

    public void TriggerRemoteClash(
        int row,
        int col,
        PlantType plantType,
        MultiplayerMenuManager.PlayerRole remoteInitiatorRole = MultiplayerMenuManager.PlayerRole.None,
        ZombieType zombieType = ZombieType.Normal,
        int clashId = 0)
    {
        EnsureSubsystems();
        arenaController.TriggerRemoteClash(row, col, plantType, remoteInitiatorRole, zombieType, clashId);
    }

    public void Enter3D1v1Arena() { EnsureSubsystems(); arenaController.Enter3D1v1Arena(); }
    public void ResolveArena(bool zombieWon, bool notifyNetwork = true, int resolvedRow = -1, int resolvedCol = -1, int clashId = 0) { EnsureSubsystems(); arenaController.ResolveArena(zombieWon, notifyNetwork, resolvedRow, resolvedCol, clashId); }
    public void Exit3DArenaBackTo2DWall() => Exit3DArenaBackTo2DWall(destroyZombie: true);
    public void Exit3DArenaBackTo2DWall(bool destroyZombie) { EnsureSubsystems(); arenaController.Exit3DArenaBackTo2DWall(destroyZombie); }
    public void SetRemoteArenaPose(Vector3 pos, float yaw, int stateFlags = 0, int clashId = 0) { EnsureSubsystems(); arenaController.SetRemoteArenaPose(pos, yaw, stateFlags, clashId); }
    public void ApplyLocalArenaImmobilize(float duration) { EnsureSubsystems(); arenaController.ApplyLocalArenaImmobilize(duration); }
    public void ApplyLocalArenaDisrupt(float duration) { EnsureSubsystems(); arenaController.ApplyLocalArenaDisrupt(duration); }
    public void ApplyRemoteArenaDamage(int damage, float stunDuration) { EnsureSubsystems(); arenaController.ApplyRemoteArenaDamage(damage, stunDuration); }
    public void ApplyRemoteArenaKnockback(Vector3 knockbackVel) { EnsureSubsystems(); arenaController.ApplyRemoteArenaKnockback(knockbackVel); }
    public void ApplyRemoteParryNotification(int blockedDamage, int restoredRemoteHP) { EnsureSubsystems(); arenaController.ApplyRemoteParryNotification(blockedDamage, restoredRemoteHP); }
    public void SpawnRemoteArenaAbilityFx(string fxType, Vector3 startPos, Vector3 endPos) { EnsureSubsystems(); arenaController.SpawnRemoteArenaAbilityFx(fxType, startPos, endPos); }
    public void ApplyRemoteArenaSeed(int seed) { EnsureSubsystems(); arenaController.ApplyRemoteArenaSeed(seed); }
    public void RebuildRandomArenaLayout(int seed) { EnsureSubsystems(); arenaController.RebuildRandomArenaLayout(seed); }

    // ==================== HITSCAN CONFIG RESOLVERS ====================

    public HitscanAttackConfig GetMoonflowerSolarBoltConfig() => ResolveHitscanConfig(GetPlantDefinition(PlantType.Moonflower)?.primaryHitscanConfig, moonflowerSolarBoltHitscan);
    public HitscanAttackConfig GetRoseThornConfig() => ResolveHitscanConfig(GetPlantDefinition(PlantType.Rose)?.primaryHitscanConfig, roseThornHitscan);
    public HitscanAttackConfig GetRoseHeavyVolleyConfig() => ResolveHitscanConfig(GetPlantDefinition(PlantType.Rose)?.secondaryHitscanConfig, roseHeavyVolleyHitscan);
    public HitscanAttackConfig GetHazmatAcidShotConfig() => ResolveHitscanConfig(GetZombieDefinition(ZombieType.Hazmat)?.primaryHitscanConfig, hazmatAcidShotHitscan);
    public HitscanAttackConfig GetHazmatSludgeBlastConfig() => ResolveHitscanConfig(GetZombieDefinition(ZombieType.Hazmat)?.secondaryHitscanConfig, hazmatSludgeBlastHitscan);

    private static HitscanAttackConfig ResolveHitscanConfig(HitscanAttackConfig assetConfig, HitscanAttackConfig fallback)
    {
        if (assetConfig != null && !string.IsNullOrEmpty(assetConfig.AbilityName))
        {
            return assetConfig;
        }
        return fallback;
    }

    // ==================== INTERNAL EVENT DISPATCHERS FOR SUBSYSTEMS ====================

    internal void NotifySkyMoonSpawned(int id, Vector3 startPos, float landingY, int value) => OnSkyMoonSpawnedLocal?.Invoke(id, startPos, landingY, value);
    internal void NotifyFlowerMoonSpawned(int id, Vector3 flowerWorldPos, int amount) => OnFlowerMoonSpawnedLocal?.Invoke(id, flowerWorldPos, amount);
    internal void NotifyMoonsCollected(List<int> ids) => OnMoonsCollectedLocal?.Invoke(ids);
    internal void NotifyClashStarted(int row, int col, PlantType pType, MultiplayerMenuManager.PlayerRole role, ZombieType zType, int clashId) => OnClashStartedLocal?.Invoke(row, col, pType, role, zType, clashId);
    internal void NotifyArenaSeed(int seed) => OnArenaSeedLocal?.Invoke(seed);
    internal void NotifyArenaPose(Vector3 pos, float yaw, int stateFlags, int clashId) => OnArenaPoseLocal?.Invoke(pos, yaw, stateFlags, clashId);
    internal void NotifyArenaResolved(bool zombieWon, int row, int col, int clashId) => OnArenaResolvedLocal?.Invoke(zombieWon, row, col, clashId);
    internal void NotifyArenaStun(float duration) => OnArenaStunLocal?.Invoke(duration);
    internal void NotifyArenaDisrupt(float duration) => OnArenaDisruptLocal?.Invoke(duration);
    internal void NotifyArenaAbilityFx(string fxType, Vector3 startPos, Vector3 endPos) => OnArenaAbilityFxLocal?.Invoke(fxType, startPos, endPos);
    internal void NotifyArenaHitscanFired(HitscanResult result) => OnArenaHitscanFiredLocal?.Invoke(result);
    internal void NotifyArenaAttackHit(int damage, float stunDuration) => OnArenaAttackHitLocal?.Invoke(damage, stunDuration);
    internal void NotifyArenaParried(int blockedDamage, int restoredHP) => OnArenaParriedLocal?.Invoke(blockedDamage, restoredHP);
    internal void NotifyArenaKnockback(Vector3 velocity) => OnArenaKnockbackLocal?.Invoke(velocity);

    // ==================== AUDIO & MATCH ENDPOINT ====================

    public void Pause2DMusic() => audioController?.Pause2DMusic();
    public void Stop2DMusic() => audioController?.Stop2DMusic();
    public void Update2DBackgroundMusicPublic() => audioController?.Update2DBackgroundMusic(IsIn3DArena, gameplayActive, GetMenuManager());

    public void TriggerPlantsWin(bool notifyNetwork = true) => EndMatch(MatchResult.PlantsWin, notifyNetwork);
    public void TriggerZombiesWin(bool notifyNetwork = true) => EndMatch(MatchResult.ZombiesWin, notifyNetwork);

    private void EndMatch(MatchResult result, bool notifyNetwork)
    {
        if (matchResult != MatchResult.InProgress || result == MatchResult.InProgress) return;

        if (IsIn3DArena)
        {
            Exit3DArenaBackTo2DWall();
        }

        matchResult = result;
        is2DWorldPaused = true;
        isPlantSelected = false;
        isZombieSelected = false;
        HideAllPreviewGhosts();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (notifyNetwork) OnMatchEndedLocal?.Invoke(result);
    }

    public void ResetMatch(bool notifyNetwork = true)
    {
        EnsureSubsystems();
        matchResult = MatchResult.InProgress;
        matchTimeRemaining = matchDuration;
        is2DWorldPaused = false;
        isPlantSelected = false;
        isZombieSelected = false;

        if (IsIn3DArena)
        {
            Exit3DArenaBackTo2DWall();
        }

        DestroyAllOfType<WallZombie>();
        if (placedPlants != null)
        {
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    if (placedPlants[r, c] != null)
                    {
                        Destroy(placedPlants[r, c].gameObject);
                        placedPlants[r, c] = null;
                    }
                }
            }
        }
        DestroyAllOfType<CollectibleMoon>();
        DestroyAllOfType<RoseProjectile>();
        DestroyAllOfType<HazmatAcidProjectile>();

        economyController.ResetEconomy();
        arenaController.ResetSessionState();
        HideAllPreviewGhosts();

        if (notifyNetwork) OnMatchRestartedLocal?.Invoke();
    }

    private static void DestroyAllOfType<T>() where T : Component
    {
        T[] items = FindObjectsByType<T>(FindObjectsInactive.Include);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null) Destroy(items[i].gameObject);
        }
    }

    public PlayerController GetPrimaryPlayerController()
    {
        if (cachedPlayerController != null)
        {
            if (!cachedPlayerController.gameObject.activeInHierarchy) cachedPlayerController.gameObject.SetActive(true);
            cachedPlayerController.enabled = true;
            return cachedPlayerController;
        }

        GameObject rootPlayer = GameObject.Find("Player");
        if (rootPlayer != null)
        {
            if (!rootPlayer.activeInHierarchy) rootPlayer.SetActive(true);

            foreach (Transform child in rootPlayer.transform)
            {
                PlayerController childPc = child.GetComponent<PlayerController>();
                if (childPc != null) childPc.enabled = false;
            }

            PlayerController pc = rootPlayer.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.enabled = true;
                cachedPlayerController = pc;
                return pc;
            }
        }

        cachedPlayerController = FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (cachedPlayerController != null)
        {
            if (!cachedPlayerController.gameObject.activeInHierarchy) cachedPlayerController.gameObject.SetActive(true);
            cachedPlayerController.enabled = true;
            return cachedPlayerController;
        }

        GameObject autoPlayer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        autoPlayer.name = "Player";
        MaterialCache.DestroyCollider(autoPlayer);
        CharacterController cc = autoPlayer.AddComponent<CharacterController>();
        cc.height = 2.0f;
        cc.radius = 0.45f;
        cc.center = Vector3.zero;
        cachedPlayerController = autoPlayer.AddComponent<PlayerController>();
        return cachedPlayerController;
    }

    // ==================== HUD & NAVIGATION ====================

    private void OnGUI()
    {
        if (!Application.isPlaying || !gameplayActive) return;

        EnsureSubsystems();
        if (arenaController.IsZoomingIntoClash)
        {
            hudRenderer.DrawClashZoomBanner();
            return;
        }

        if (arenaController.IsIn3DArena)
        {
            arenaController.DrawArenaHUD();
            return;
        }

        hudRenderer.Draw2DGameplayHUD();
    }

    public void GoToMainMenuFromGameOver()
    {
        MultiplayerMenuManager menu = GetMenuManager();
        if (menu != null)
        {
            menu.ReturnToMainMenu();
        }
        else
        {
            ResetMatch(notifyNetwork: false);
            Stop2DMusic();
        }
    }

    public void ExitGameFromGameOver()
    {
        MultiplayerMenuManager menu = GetMenuManager();
        if (menu != null)
        {
            menu.QuitGame();
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnDrawGizmos()
    {
        float leftEdge = transform.position.x - (wallWidth * 0.5f);
        float bottomEdge = transform.position.y - (wallHeight * 0.5f);
        float z = transform.position.z - (wallThickness * 0.5f) - 0.02f;

        Gizmos.color = Color.green;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                Vector3 center = new Vector3(
                    leftEdge + (c + 0.5f) * TileWidth,
                    bottomEdge + (r + 0.5f) * TileHeight,
                    z
                );
                Gizmos.DrawWireCube(center, new Vector3(TileWidth - tilePadding, TileHeight - tilePadding, 0.05f));
            }
        }

        Gizmos.color = Color.red;
        Gizmos.DrawLine(
            new Vector3(DefenseLineX, bottomEdge, z),
            new Vector3(DefenseLineX, bottomEdge + wallHeight, z)
        );
    }
}
