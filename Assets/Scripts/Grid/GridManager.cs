using System;
using UnityEngine;

[ExecuteAlways]
public class GridManager : MonoBehaviour
{
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
    public float matchDuration = 300.0f; // 5 minutes
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
    private float skyMoonTimer = 0f;

    [Header("Plant Costs & Cooldowns")]
    public int moonflowerCost = 50;
    public float moonflowerCooldown = 5.0f;

    public int roseCost = 100;
    public float roseCooldown = 5.0f;
    public int rozeCost { get => roseCost; set => roseCost = value; }
    public float rozeCooldown { get => roseCooldown; set => roseCooldown = value; }

    public int durianCost = 50;
    public float durianCooldown = 30.0f;

    [Header("Zombie Player: Brains & Zombie Spawning")]
    public int currentBrains = 150;
    public int zombieCost = 50;
    public float zombieCooldown = 2.0f; // Snappy 2.0s spawn cooldown for active zombie offensive
    public float passiveBrainInterval = 10.0f;
    public int passiveBrainAmount = 25;
    private float zombieCooldownTimer = 0f;
    private float passiveBrainTimer = 0f;

    [Header("Zombie Placement Restriction")]
    [Tooltip("Number of columns on the right edge where zombies are allowed to spawn. Set to 2 for the last 2 columns/lanes.")]
    public int zombieAllowedColumnsFromRight = 2;

    [Header("Current Selection")]
    public bool isPlantSelected = false;
    public PlantType selectedPlantType = PlantType.Moonflower;
    public bool isZombieSelected = false;

    [Header("Clash Zoom & 3D 1v1 Arena")]
    public float clashZoomDuration = 1.35f;
    public float clashTargetOrthoSize = 1.15f;
    private bool isZoomingIntoClash = false;
    private bool isIn3DArena = false;
    private float clashZoomTimer = 0f;
    private Vector3 clashZoomStartCamPos;
    private Vector3 clashZoomTargetCamPos;
    private float clashZoomStartOrthoSize;
    private WallZombie clashingZombie;
    private PlaceholderPlant clashingPlant;
    private GameObject arena3DRoot;

    [Header("Visual Colors")]
    public Color tileColorLight = new Color(0.24f, 0.54f, 0.24f, 1.0f);
    public Color tileColorDark = new Color(0.18f, 0.44f, 0.18f, 1.0f);
    public Color tileHoverColor = new Color(0.45f, 0.92f, 0.45f, 1.0f);
    public Color tileZombieHoverColor = new Color(0.82f, 0.38f, 0.98f, 1.0f);
    public Color tileOccupiedColor = new Color(0.85f, 0.30f, 0.25f, 1.0f);

    public event Action<int, int, PlantType> OnPlantPlacedLocal;
    public event Action<int, int> OnZombiePlacedLocal;

    // ===== Network sync events (moons, clash, 3D arena) =====
    public event Action<int, Vector3, float, int> OnSkyMoonSpawnedLocal;      // id, startPos, landingY, value
    public event Action<int, Vector3, int> OnFlowerMoonSpawnedLocal;           // id, flowerPos, value
    public event Action<System.Collections.Generic.List<int>> OnMoonsCollectedLocal;
    public event Action<int, int, PlantType, MultiplayerMenuManager.PlayerRole> OnClashStartedLocal; // plant row, col, plantType, senderRole
    public event Action<Vector3, float> OnArenaPoseLocal;                      // position, yaw
    public event Action<bool> OnArenaResolvedLocal;                            // true = zombie won
    public event Action<float> OnArenaStunLocal;                               // duration in seconds (Durian immobilize)
    public event Action<string, Vector3, Vector3> OnArenaAbilityFxLocal;       // fxType, startPos, endPos

    private int nextMoonId = 1;
    public bool IsIn3DArena => isIn3DArena;
    /// <summary>Only the Plant player (or solo) spawns moons; the Zombie player receives them over the network.</summary>
    public bool IsMoonAuthority => GetEffectiveLocalRole() != MultiplayerMenuManager.PlayerRole.Zombie;

    // 3D arena remote player avatar
    private GameObject remoteArenaAvatar;
    private Vector3 remoteArenaTargetPos;
    private float remoteArenaTargetYaw;
    private bool hasRemoteArenaPose = false;
    private float arenaPoseSendTimer = 0f;

    // 3D Arena HP, Hitboxes, Abilities & Hit Indicator State
    private int localArenaMaxHP = 100;
    private int localArenaCurrentHP = 100;
    private int remoteArenaMaxHP = 150;
    private int remoteArenaCurrentHP = 150;
    private Transform remoteHpBarFill;
    private Renderer remoteHpBarFillRenderer;
    private Color remoteHpBarBaseColor = Color.green;

    private int arenaHitsLanded = 0;
    private float arenaHitIndicatorTimer = 0f;
    private string arenaLastHitLabel = "HIT!";
    private float remoteHitFlashTimer = 0f;
    private float remoteImmobilizedTimer = 0f;

    // Ability timers
    private float arenaAbilityCooldownTimer = 0f;
    private bool isChargingMoonBeam = false;
    private float moonBeamChargeTimer = 0f;
    public float moonBeamChargeDuration = 2.0f;
    private GameObject localMoonChargeOrb;

    private bool isChargingRoseThorn = false;
    private float roseThornChargeTimer = 0f;
    public float roseThornChargeDuration = 0.4f;

    private bool isDurianRollingLocal = false;
    private float durianRollTimer = 0f;
    public float durianRollDuration = 2.6f;
    private GameObject localDurianRollVisual;

    // Arena result (host presses N / M)
    private bool arenaResolving = false;
    private bool arenaZombieWon = false;
    private float arenaResolveTimer = 0f;
    public float arenaResultPauseDuration = 2.5f;

    private readonly float[] cooldownTimers = new float[3];
    private PlaceholderPlant[,] placedPlants;
    private Renderer[,] tileRenderers;
    private GameObject tilesContainer;
    private readonly GameObject[] previewGhosts = new GameObject[3];
    private GameObject zombiePreviewGhost;

    private int hoveredRow = -1;
    private int hoveredCol = -1;
    private Material sharedBaseMaterial;

    // Top-left UI layout rects (Moon Bank + 3 Plant Buttons + Brain Bank + Zombie Button)
    private readonly Rect moonBankRect = new Rect(12f, 12f, 110f, 66f);
    private readonly Rect[] plantButtonRects = new Rect[3]
    {
        new Rect(126f, 12f, 125f, 66f),
        new Rect(255f, 12f, 125f, 66f),
        new Rect(384f, 12f, 125f, 66f)
    };
    private readonly Rect brainBankRect = new Rect(12f, 82f, 110f, 66f);
    private readonly Rect zombieButtonRect = new Rect(126f, 82f, 150f, 66f);

    public float TileWidth => columns > 0 ? wallWidth / columns : 2.0f;
    public float TileHeight => rows > 0 ? wallHeight / rows : 1.8f;

    private void Awake()
    {
        Application.runInBackground = true;
        SyncWallScale();
        if (Application.isPlaying)
        {
            InitializeRuntimeGrid();
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

    public void SyncWallScale()
    {
        transform.rotation = Quaternion.identity;
        transform.localScale = new Vector3(wallWidth, wallHeight, wallThickness);

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null && enforce16By9Viewport && !isIn3DArena && !isZoomingIntoClash)
        {
            targetCamera.orthographic = true;
            targetCamera.orthographicSize = wallHeight * 0.5f;
            Apply16By9Viewport(targetCamera);
        }
    }

    [ContextMenu("Align Camera & Wall (16:9)")]
    public void SyncWallScaleAndCamera()
    {
        SyncWallScale();

        if (targetCamera != null && !isIn3DArena && !isZoomingIntoClash)
        {
            targetCamera.orthographic = true;
            targetCamera.orthographicSize = wallHeight * 0.5f;
            targetCamera.transform.position = GetIdealCameraPosition();
            targetCamera.transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);

            if (enforce16By9Viewport)
            {
                Apply16By9Viewport(targetCamera);
            }
        }
    }

    public Vector3 GetIdealCameraPosition()
    {
        return transform.position - transform.forward * cameraDistance;
    }

    public void Apply16By9Viewport(Camera cam)
    {
        float targetAspect = 16.0f / 9.0f;
        float windowAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float scaleHeight = windowAspect / targetAspect;

        if (Mathf.Abs(scaleHeight - 1.0f) < 0.01f)
        {
            cam.rect = new Rect(0f, 0f, 1f, 1f);
        }
        else if (scaleHeight < 1.0f)
        {
            Rect rect = cam.rect;
            rect.width = 1.0f;
            rect.height = scaleHeight;
            rect.x = 0f;
            rect.y = (1.0f - scaleHeight) * 0.5f;
            cam.rect = rect;
        }
        else
        {
            float scaleWidth = 1.0f / scaleHeight;
            Rect rect = cam.rect;
            rect.width = scaleWidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scaleWidth) * 0.5f;
            rect.y = 0f;
            cam.rect = rect;
        }
    }

    private void InitializeRuntimeGrid()
    {
        placedPlants = new PlaceholderPlant[rows, columns];
        tileRenderers = new Renderer[rows, columns];

        Transform existing = transform.Find("TileSpotsContainer");
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        tilesContainer = new GameObject("TileSpotsContainer");
        tilesContainer.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
        tilesContainer.transform.localScale = Vector3.one;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                GameObject tileQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tileQuad.name = $"Tile_{r}_{c}";
                tileQuad.transform.SetParent(tilesContainer.transform, true);

                DestroyCollider(tileQuad);

                Vector3 pos = GetTileWorldPosition(r, c, -0.01f);
                tileQuad.transform.position = pos;
                tileQuad.transform.rotation = Quaternion.identity;
                tileQuad.transform.localScale = new Vector3(
                    Mathf.Max(0.1f, TileWidth - tilePadding),
                    Mathf.Max(0.1f, TileHeight - tilePadding),
                    1.0f
                );

                Renderer rend = tileQuad.GetComponent<Renderer>();
                rend.material = CreateColoredMaterial(GetBaseTileColor(r, c));
                tileRenderers[r, c] = rend;
            }
        }

        // Visual Defense Line marker on the board's left boundary
        Transform existingLine = transform.Find("DefenseLineMarker");
        if (existingLine != null)
        {
            Destroy(existingLine.gameObject);
        }
        GameObject defenseLineObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        defenseLineObj.name = "DefenseLineMarker";
        defenseLineObj.transform.SetParent(transform, true);
        defenseLineObj.transform.position = new Vector3(
            DefenseLineX,
            transform.position.y,
            transform.position.z - (wallThickness * 0.5f) - 0.02f
        );
        defenseLineObj.transform.localScale = new Vector3(0.16f, wallHeight, 1.0f);
        DestroyCollider(defenseLineObj);
        defenseLineObj.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(1.0f, 0.20f, 0.25f, 0.98f));

        // Wooden Garden Border Frame
        Transform existingBorder = transform.Find("GardenFrame");
        if (existingBorder != null) Destroy(existingBorder.gameObject);
        GameObject frameRoot = new GameObject("GardenFrame");
        frameRoot.transform.SetParent(transform, false);
        Color woodCol = new Color(0.38f, 0.24f, 0.14f);
        float hw = wallWidth * 0.5f;
        float hh = wallHeight * 0.5f;
        float beamThick = 0.28f;
        float zOffset = -(wallThickness * 0.5f) - 0.03f;

        CreateFrameBeam(frameRoot, new Vector3(0f, hh + beamThick * 0.45f, zOffset), new Vector3(wallWidth + beamThick * 2f, beamThick, 0.15f), woodCol);
        CreateFrameBeam(frameRoot, new Vector3(0f, -hh - beamThick * 0.45f, zOffset), new Vector3(wallWidth + beamThick * 2f, beamThick, 0.15f), woodCol);
        CreateFrameBeam(frameRoot, new Vector3(-hw - beamThick * 0.45f, 0f, zOffset), new Vector3(beamThick, wallHeight, 0.15f), woodCol);
        CreateFrameBeam(frameRoot, new Vector3(hw + beamThick * 0.45f, 0f, zOffset), new Vector3(beamThick, wallHeight, 0.15f), woodCol);

        CreatePreviewGhosts();
    }

    private void CreateFrameBeam(GameObject parent, Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "FrameBeam";
        beam.transform.SetParent(parent.transform, false);
        beam.transform.localPosition = localPos;
        beam.transform.localScale = localScale;
        DestroyCollider(beam);
        beam.GetComponent<Renderer>().material = CreateColoredMaterial(color);
    }

    private void CreatePreviewGhosts()
    {
        for (int i = 0; i < 3; i++)
        {
            if (previewGhosts[i] != null)
            {
                Destroy(previewGhosts[i]);
            }
            previewGhosts[i] = CreatePlantVisual((PlantType)i, $"PreviewGhost_{(PlantType)i}", isGhost: true);
            if (tilesContainer != null)
            {
                previewGhosts[i].transform.SetParent(tilesContainer.transform, true);
            }
            previewGhosts[i].SetActive(false);
        }

        if (zombiePreviewGhost != null)
        {
            Destroy(zombiePreviewGhost);
        }
        zombiePreviewGhost = CreateZombieVisual("ZombiePreviewGhost", isGhost: true);
        if (tilesContainer != null)
        {
            zombiePreviewGhost.transform.SetParent(tilesContainer.transform, true);
        }
        zombiePreviewGhost.SetActive(false);
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            SyncWallScale();
            return;
        }

        if (placedPlants == null || placedPlants.GetLength(0) != rows || placedPlants.GetLength(1) != columns)
        {
            InitializeRuntimeGrid();
        }

        // Handle 3D 1v1 Arena active state (Backspace returns to the 2D Wall)
        if (isIn3DArena)
        {
            UpdateArenaNetworking();

            if (arenaResolving)
            {
                arenaResolveTimer -= Time.deltaTime;
                if (arenaResolveTimer <= 0f)
                {
                    FinishArenaResolution();
                }
            }
            else
            {
                UpdateArenaCombat();

                MultiplayerMenuManager menu = FindFirstObjectByType<MultiplayerMenuManager>();
                bool isHostOrSolo = menu == null || menu.isHost || menu.localRole == MultiplayerMenuManager.PlayerRole.None;
                if (isHostOrSolo)
                {
                    if (Input.GetKeyDown(KeyCode.N))
                    {
                        ResolveArena(zombieWon: true, notifyNetwork: true);
                    }
                    else if (Input.GetKeyDown(KeyCode.M))
                    {
                        ResolveArena(zombieWon: false, notifyNetwork: true);
                    }
                }

                if (Input.GetKeyDown(KeyCode.Backspace))
                {
                    Exit3DArenaBackTo2DWall();
                }
            }
            return;
        }

        // Handle Clash Camera Zoom-In before transitioning into the 3D 1v1 Arena
        if (isZoomingIntoClash)
        {
            UpdateClashZoomAnimation();
            return;
        }

        if (lockCameraToThisWall)
        {
            SyncWallScaleAndCamera();
        }
        else
        {
            SyncWallScale();
        }

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

        if (!gameplayActive || is2DWorldPaused || matchResult != MatchResult.InProgress)
        {
            HideAllPreviewGhosts();
            return;
        }

        // 5-Minute Match Timer: Plants win when the timer reaches 0:00!
        matchTimeRemaining -= Time.deltaTime;
        if (matchTimeRemaining <= 0f)
        {
            matchTimeRemaining = 0f;
            TriggerPlantsWin(notifyNetwork: true);
            return;
        }

        // Tick down plant & zombie cooldowns
        for (int i = 0; i < cooldownTimers.Length; i++)
        {
            if (cooldownTimers[i] > 0f)
            {
                cooldownTimers[i] = Mathf.Max(0f, cooldownTimers[i] - Time.deltaTime);
            }
        }
        if (zombieCooldownTimer > 0f)
        {
            zombieCooldownTimer = Mathf.Max(0f, zombieCooldownTimer - Time.deltaTime);
        }

        // Sky Moon Spawner: Every 15 seconds, a Moon falls from the top of the wall
        skyMoonTimer += Time.deltaTime;
        if (skyMoonTimer >= skyMoonInterval && IsMoonAuthority)
        {
            skyMoonTimer = 0f;
            SpawnSkyFallingMoon();
        }

        // Passive Brains income for Zombie Player
        passiveBrainTimer += Time.deltaTime;
        if (passiveBrainTimer >= passiveBrainInterval)
        {
            passiveBrainTimer = 0f;
            currentBrains += passiveBrainAmount;
        }

        // Hotkeys: 1, 2, 3 only available if player is allowed to place plants; 4 only if allowed to place zombies
        if (canPlacePlants)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                TrySelectPlant(PlantType.Moonflower);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                TrySelectPlant(PlantType.Rose);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                TrySelectPlant(PlantType.Durian);
            }
        }

        if (canPlaceZombies)
        {
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            {
                TrySelectZombie();
            }
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            isPlantSelected = false;
            isZombieSelected = false;
        }

        UpdateHoveredTile();
        UpdateTileHighlightsAndGhost();
        HandleMouseClick();
    }

    // ==================== CLASH TRIGGER -> PAUSE 2D -> ZOOM -> 3D 1V1 ARENA ====================

    public bool HasZombieInRow(int row, float minX)
    {
        for (int i = 0; i < WallZombie.ActiveZombies.Count; i++)
        {
            WallZombie z = WallZombie.ActiveZombies[i];
            if (z != null && z.row == row && z.transform.position.x >= minX)
            {
                return true;
            }
        }
        return false;
    }

    public PlaceholderPlant GetPlantTouchedByZombie(int row, float zombieX)
    {
        if (placedPlants == null || row < 0 || row >= rows)
        {
            return null;
        }

        for (int c = 0; c < columns; c++)
        {
            PlaceholderPlant plant = placedPlants[row, c];
            if (plant != null)
            {
                float dist = Mathf.Abs(zombieX - plant.transform.position.x);
                if (dist <= 0.72f)
                {
                    return plant;
                }
            }
        }
        return null;
    }

    private int clashingPlantRow = -1;
    private int clashingPlantCol = -1;
    private PlantType clashingPlantType = PlantType.Rose;

    public MultiplayerMenuManager.PlayerRole EffectiveRole => GetEffectiveLocalRole();

    public MultiplayerMenuManager.PlayerRole GetEffectiveLocalRole()
    {
        if (localRole != MultiplayerMenuManager.PlayerRole.None)
        {
            return localRole;
        }

        MultiplayerMenuManager menu = FindFirstObjectByType<MultiplayerMenuManager>();
        if (menu != null)
        {
            if (menu.localRole != MultiplayerMenuManager.PlayerRole.None)
            {
                localRole = menu.localRole;
                return localRole;
            }
            if (menu.isHost)
            {
                if (menu.hostRole != MultiplayerMenuManager.PlayerRole.None)
                {
                    localRole = menu.hostRole;
                    return localRole;
                }
                // Host default is Plant
                localRole = MultiplayerMenuManager.PlayerRole.Plant;
                return localRole;
            }
            else
            {
                if (menu.joinerRole != MultiplayerMenuManager.PlayerRole.None)
                {
                    localRole = menu.joinerRole;
                    return localRole;
                }
                // Joiner default is Zombie
                localRole = MultiplayerMenuManager.PlayerRole.Zombie;
                return localRole;
            }
        }

        return localRole;
    }

    public void TriggerZombiePlantClash(WallZombie zombie, PlaceholderPlant plant, bool notifyNetwork = true)
    {
        PlantType pType = (plant != null) ? plant.plantType : PlantType.Rose;
        TriggerZombiePlantClash(zombie, plant, pType, notifyNetwork);
    }

    public void TriggerZombiePlantClash(WallZombie zombie, PlaceholderPlant plant, PlantType fallbackType, bool notifyNetwork = true)
    {
        if (is2DWorldPaused || isZoomingIntoClash || isIn3DArena)
        {
            return;
        }

        PlantType pType = (plant != null) ? plant.plantType : fallbackType;
        int r = (plant != null) ? plant.row : (zombie != null ? zombie.row : 0);
        int c = (plant != null) ? plant.column : 0;

        clashingPlantRow = r;
        clashingPlantCol = c;
        clashingPlantType = pType;

        if (notifyNetwork)
        {
            OnClashStartedLocal?.Invoke(r, c, pType, GetEffectiveLocalRole());
        }

        // 1. Pause the entire 2D world
        is2DWorldPaused = true;
        isPlantSelected = false;
        isZombieSelected = false;
        HideAllPreviewGhosts();

        clashingZombie = zombie;
        clashingPlant = plant;

        // 2. Begin smooth camera zoom into the colliding Zombie & Plant
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null)
        {
            clashZoomStartCamPos = targetCamera.transform.position;
            clashZoomStartOrthoSize = targetCamera.orthographicSize;

            Vector3 zPos = zombie != null ? zombie.transform.position : (plant != null ? plant.transform.position : transform.position);
            Vector3 pPos = plant != null ? plant.transform.position : zPos;
            Vector3 midpoint = (zPos + pPos) * 0.5f;
            clashZoomTargetCamPos = new Vector3(midpoint.x, midpoint.y, clashZoomStartCamPos.z);
        }

        clashZoomTimer = 0f;
        isZoomingIntoClash = true;
    }

    /// <summary>Called when the other player reports a clash, so both players enter the 3D arena together.</summary>
    public void TriggerRemoteClash(int row, int col)
    {
        TriggerRemoteClash(row, col, PlantType.Rose, MultiplayerMenuManager.PlayerRole.None);
    }

    public void TriggerRemoteClash(int row, int col, PlantType plantType, MultiplayerMenuManager.PlayerRole remoteInitiatorRole = MultiplayerMenuManager.PlayerRole.None)
    {
        clashingPlantRow = row;
        clashingPlantCol = col;
        clashingPlantType = plantType;

        // Auto-reconcile local role if remote role was specified
        if (remoteInitiatorRole == MultiplayerMenuManager.PlayerRole.Zombie)
        {
            SetLocalRole(MultiplayerMenuManager.PlayerRole.Plant);
        }
        else if (remoteInitiatorRole == MultiplayerMenuManager.PlayerRole.Plant)
        {
            SetLocalRole(MultiplayerMenuManager.PlayerRole.Zombie);
        }

        PlaceholderPlant plant = null;
        if (placedPlants != null && row >= 0 && row < rows && col >= 0 && col < columns)
        {
            plant = placedPlants[row, col];
        }

        // Find the zombie in that row closest to the plant
        WallZombie best = null;
        float bestDist = float.MaxValue;
        Vector3 refPos = plant != null ? plant.transform.position : GetTileWorldPosition(row, col, 0f);
        for (int i = 0; i < WallZombie.ActiveZombies.Count; i++)
        {
            WallZombie z = WallZombie.ActiveZombies[i];
            if (z == null || z.row != row) continue;
            float d = Mathf.Abs(z.transform.position.x - refPos.x);
            if (d < bestDist) { bestDist = d; best = z; }
        }

        TriggerZombiePlantClash(best, plant, plantType, notifyNetwork: false);
    }

    private void UpdateClashZoomAnimation()
    {
        clashZoomTimer += Time.deltaTime;
        float t = Mathf.Clamp01(clashZoomTimer / Mathf.Max(0.1f, clashZoomDuration));
        float smoothT = Mathf.SmoothStep(0f, 1f, t);

        if (targetCamera != null)
        {
            targetCamera.transform.position = Vector3.Lerp(clashZoomStartCamPos, clashZoomTargetCamPos, smoothT);
            targetCamera.orthographicSize = Mathf.Lerp(clashZoomStartOrthoSize, clashTargetOrthoSize, smoothT);
        }

        if (t >= 1.0f)
        {
            isZoomingIntoClash = false;
            Enter3D1v1Arena();
        }
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

        // Cancel any illegal selection
        if (!canPlacePlants) isPlantSelected = false;
        if (!canPlaceZombies) isZombieSelected = false;
        HideAllPreviewGhosts();
    }

    private string currentArenaOpponentName = "OPPONENT";

    public void Enter3D1v1Arena()
    {
        isIn3DArena = true;
        arenaResolving = false;
        arenaPoseSendTimer = 0f;

        MultiplayerMenuManager.PlayerRole myRole = GetEffectiveLocalRole();
        bool isZombie = myRole == MultiplayerMenuManager.PlayerRole.Zombie;
        PlantType activePlant = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;
        clashingPlantType = activePlant;

        // Reset 3D Arena HP & combat states (attacks count as hits without reducing HP)
        localArenaMaxHP = isZombie ? 150 : GetPlantMaxArenaHP(activePlant);
        localArenaCurrentHP = localArenaMaxHP;
        remoteArenaMaxHP = isZombie ? GetPlantMaxArenaHP(activePlant) : 150;
        remoteArenaCurrentHP = remoteArenaMaxHP;

        arenaHitsLanded = 0;
        arenaHitIndicatorTimer = 0f;
        remoteHitFlashTimer = 0f;
        remoteImmobilizedTimer = 0f;
        arenaAbilityCooldownTimer = 0f;
        isChargingMoonBeam = false;
        moonBeamChargeTimer = 0f;
        isChargingRoseThorn = false;
        roseThornChargeTimer = 0f;
        isDurianRollingLocal = false;
        durianRollTimer = 0f;

        // Ensure 3D Arena environment exists with the remote opponent avatar (Plant or Zombie)
        BuildOrShow3DArenaEnvironment();

        Vector3 arenaSpawnPos = isZombie ? ArenaZombieSpawn : ArenaPlantSpawn;
        Quaternion arenaSpawnRot = Quaternion.Euler(0f, isZombie ? 180f : 0f, 0f);

        PlayerController chosenPlayer = GetPrimaryPlayerController();
        if (chosenPlayer != null && targetCamera != null)
        {
            chosenPlayer.inputFrozen = false;
            chosenPlayer.EnterFirstPersonArena(targetCamera, arenaSpawnPos, arenaSpawnRot, currentArenaOpponentName);
        }
    }

    private int GetPlantMaxArenaHP(PlantType type)
    {
        return (type == PlantType.Durian) ? 400 : 100;
    }

    private static readonly Vector3 ArenaPlantSpawn = new Vector3(0f, 1.5f, -38.0f);
    private static readonly Vector3 ArenaZombieSpawn = new Vector3(0f, 1.5f, -14.0f);

    /// <summary>Host decides the 1v1 result (N = zombie won, M = plant won). Shows a banner + pause, then returns to 2D.</summary>
    public void ResolveArena(bool zombieWon, bool notifyNetwork = true)
    {
        if (arenaResolving) return;
        if (!isIn3DArena && !isZoomingIntoClash) return;

        if (notifyNetwork)
        {
            OnArenaResolvedLocal?.Invoke(zombieWon);
        }

        if (isZoomingIntoClash)
        {
            // Result arrived before our zoom finished: jump straight into the arena so the banner shows
            isZoomingIntoClash = false;
            Enter3D1v1Arena();
        }

        arenaResolving = true;
        arenaZombieWon = zombieWon;
        arenaResolveTimer = arenaResultPauseDuration;

        PlayerController pc = GetPrimaryPlayerController();
        if (pc != null) pc.inputFrozen = true;
    }

    private void FinishArenaResolution()
    {
        arenaResolving = false;

        if (arenaZombieWon)
        {
            // Zombie won: the targeted plant is removed, the zombie keeps walking
            if (clashingPlantRow >= 0 && clashingPlantRow < rows && clashingPlantCol >= 0 && clashingPlantCol < columns)
            {
                if (placedPlants != null && placedPlants[clashingPlantRow, clashingPlantCol] != null)
                {
                    Destroy(placedPlants[clashingPlantRow, clashingPlantCol].gameObject);
                    placedPlants[clashingPlantRow, clashingPlantCol] = null;
                }
            }
            if (clashingPlant != null)
            {
                int r = clashingPlant.row, c = clashingPlant.column;
                if (placedPlants != null && r >= 0 && r < rows && c >= 0 && c < columns && placedPlants[r, c] == clashingPlant)
                {
                    placedPlants[r, c] = null;
                }
                Destroy(clashingPlant.gameObject);
                clashingPlant = null;
            }
            Exit3DArenaBackTo2DWall(destroyZombie: false);
        }
        else
        {
            // Plant won: the zombie is removed, the plant stays
            clashingPlant = null;
            Exit3DArenaBackTo2DWall(destroyZombie: true);
        }
    }

    private void UpdateArenaNetworking()
    {
        // Send our pose ~20x per second
        PlayerController pc = GetPrimaryPlayerController();
        arenaPoseSendTimer -= Time.deltaTime;
        if (pc != null && arenaPoseSendTimer <= 0f)
        {
            arenaPoseSendTimer = 0.05f;
            OnArenaPoseLocal?.Invoke(pc.transform.position, pc.transform.eulerAngles.y);
        }

        // Smoothly move the remote avatar towards the latest received pose (unless immobilized by Durian)
        if (remoteArenaAvatar != null && hasRemoteArenaPose && remoteImmobilizedTimer <= 0f)
        {
            Transform t = remoteArenaAvatar.transform;
            t.position = Vector3.Lerp(t.position, remoteArenaTargetPos, Time.deltaTime * 15f);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, remoteArenaTargetYaw, 0f), Time.deltaTime * 15f);
        }
    }

    public void SetRemoteArenaPose(Vector3 pos, float yaw)
    {
        remoteArenaTargetPos = pos;
        remoteArenaTargetYaw = yaw;
        if (!hasRemoteArenaPose && remoteArenaAvatar != null)
        {
            remoteArenaAvatar.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }
        hasRemoteArenaPose = true;
    }

    public void ApplyLocalArenaImmobilize(float duration)
    {
        PlayerController pc = GetPrimaryPlayerController();
        if (pc != null)
        {
            pc.Immobilize(duration);
            CancelAllActiveArenaAbilities(pc);
        }
    }

    private void CancelAllActiveArenaAbilities(PlayerController pc)
    {
        isChargingMoonBeam = false;
        moonBeamChargeTimer = 0f;
        if (localMoonChargeOrb != null)
        {
            Destroy(localMoonChargeOrb);
            localMoonChargeOrb = null;
        }

        isChargingRoseThorn = false;
        roseThornChargeTimer = 0f;

        isDurianRollingLocal = false;
        durianRollTimer = 0f;
        if (pc != null) pc.isDurianRolling = false;
        if (localDurianRollVisual != null)
        {
            Destroy(localDurianRollVisual);
            localDurianRollVisual = null;
        }
    }

    private void UpdateArenaCombat()
    {
        PlayerController pc = GetPrimaryPlayerController();
        if (pc == null) return;
        if (targetCamera == null) targetCamera = Camera.main;

        if (arenaHitIndicatorTimer > 0f)
        {
            arenaHitIndicatorTimer = Mathf.Max(0f, arenaHitIndicatorTimer - Time.deltaTime);
        }

        if (remoteImmobilizedTimer > 0f)
        {
            remoteImmobilizedTimer = Mathf.Max(0f, remoteImmobilizedTimer - Time.deltaTime);
        }

        if (remoteHitFlashTimer > 0f)
        {
            remoteHitFlashTimer = Mathf.Max(0f, remoteHitFlashTimer - Time.deltaTime);
            if (remoteHpBarFillRenderer != null)
            {
                remoteHpBarFillRenderer.material.color = (remoteHitFlashTimer > 0f)
                    ? new Color(1.0f, 0.95f, 0.35f)
                    : remoteHpBarBaseColor;
            }
        }

        if (arenaAbilityCooldownTimer > 0f)
        {
            arenaAbilityCooldownTimer = Mathf.Max(0f, arenaAbilityCooldownTimer - Time.deltaTime);
        }

        // If immobilized by Durian roll, cancel active charges and block all attacks & abilities!
        if (pc.IsImmobilized)
        {
            CancelAllActiveArenaAbilities(pc);
            return;
        }

        MultiplayerMenuManager.PlayerRole myRole = GetEffectiveLocalRole();
        bool isZombie = myRole == MultiplayerMenuManager.PlayerRole.Zombie;
        PlantType activePlant = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;

        // 1. Tick active Moonflower 2.0s Moon Beam Charge
        if (isChargingMoonBeam)
        {
            moonBeamChargeTimer -= Time.deltaTime;
            float progress = 1.0f - Mathf.Clamp01(moonBeamChargeTimer / Mathf.Max(0.01f, moonBeamChargeDuration));

            if (localMoonChargeOrb == null && arena3DRoot != null)
            {
                localMoonChargeOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                localMoonChargeOrb.name = "MoonBeamChargeOrb";
                localMoonChargeOrb.transform.SetParent(arena3DRoot.transform, true);
                DestroyCollider(localMoonChargeOrb);
                localMoonChargeOrb.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.45f, 0.95f, 1.0f));
            }

            if (localMoonChargeOrb != null && targetCamera != null)
            {
                // Hover over the Moonflower's head (slightly forward so player also sees the glow above their view)
                Vector3 orbPos = pc.transform.position + new Vector3(0f, 1.32f, 0f) + targetCamera.transform.forward * 0.42f;
                localMoonChargeOrb.transform.position = orbPos;
                float orbScale = Mathf.Lerp(0.18f, 0.68f, progress) + Mathf.Sin(Time.time * 18f) * 0.03f;
                localMoonChargeOrb.transform.localScale = Vector3.one * orbScale;
            }

            if (moonBeamChargeTimer <= 0f)
            {
                isChargingMoonBeam = false;
                if (localMoonChargeOrb != null)
                {
                    Destroy(localMoonChargeOrb);
                    localMoonChargeOrb = null;
                }
                FireMoonflowerLaserBeam(pc);
            }
            return;
        }

        // 2. Tick active Rose 0.4s Hitscan Thorn Charge
        if (isChargingRoseThorn)
        {
            roseThornChargeTimer -= Time.deltaTime;
            if (roseThornChargeTimer <= 0f)
            {
                isChargingRoseThorn = false;
                FireRoseHitscanThorn(pc);
            }
            return;
        }

        // 3. Tick active Durian Roll Attack
        if (isDurianRollingLocal)
        {
            durianRollTimer -= Time.deltaTime;
            pc.isDurianRolling = true;

            if (localDurianRollVisual == null && arena3DRoot != null)
            {
                localDurianRollVisual = Create3DPlantPlaceholder(PlantType.Durian, "DurianRollVisual");
                localDurianRollVisual.transform.SetParent(arena3DRoot.transform, true);
                foreach (Collider c in localDurianRollVisual.GetComponentsInChildren<Collider>())
                {
                    Destroy(c);
                }
            }

            if (localDurianRollVisual != null)
            {
                localDurianRollVisual.transform.position = pc.transform.position + new Vector3(0f, -0.35f, 0f) + pc.transform.forward * 0.55f;
                localDurianRollVisual.transform.rotation = Quaternion.Euler(Time.time * 720f, pc.transform.eulerAngles.y, 0f);
                localDurianRollVisual.transform.localScale = Vector3.one * 0.65f;
            }

            // Check if Durian connects with the Zombie opponent!
            if (remoteArenaAvatar != null)
            {
                Vector3 a = new Vector3(pc.transform.position.x, 0f, pc.transform.position.z);
                Vector3 b = new Vector3(remoteArenaAvatar.transform.position.x, 0f, remoteArenaAvatar.transform.position.z);
                float horizDist = Vector3.Distance(a, b);
                float vertDist = Mathf.Abs(pc.transform.position.y - remoteArenaAvatar.transform.position.y);

                if (horizDist <= 1.95f && vertDist <= 2.2f)
                {
                    // Connected! Immobilize and stop the Zombie player's attacks & abilities for 2 seconds!
                    StopDurianRoll(pc);
                    arenaAbilityCooldownTimer = 1.2f;
                    remoteImmobilizedTimer = 2.0f;
                    OnArenaStunLocal?.Invoke(2.0f);
                    RegisterArenaHit("DURIAN CONNECTED! ZOMBIE IMMOBILIZED (2.0s)!");
                    return;
                }
            }

            if (durianRollTimer <= 0f)
            {
                StopDurianRoll(pc);
                arenaAbilityCooldownTimer = 0.8f;
            }
            return;
        }

        // 4. Trigger Attack / Special Ability on Left Click (while cursor locked) or E key
        bool triggerPressed = (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked)
                              || Input.GetKeyDown(KeyCode.E);

        if (!triggerPressed || arenaAbilityCooldownTimer > 0f)
        {
            return;
        }

        if (isZombie)
        {
            PerformZombieMeleeSlash(pc);
        }
        else
        {
            if (activePlant == PlantType.Moonflower)
            {
                isChargingMoonBeam = true;
                moonBeamChargeTimer = moonBeamChargeDuration;
            }
            else if (activePlant == PlantType.Rose || activePlant == PlantType.Roze)
            {
                isChargingRoseThorn = true;
                roseThornChargeTimer = roseThornChargeDuration;
            }
            else if (activePlant == PlantType.Durian)
            {
                isDurianRollingLocal = true;
                durianRollTimer = durianRollDuration;
                pc.isDurianRolling = true;
                OnArenaAbilityFxLocal?.Invoke("ROLL", pc.transform.position, pc.transform.forward);
            }
        }
    }

    private void StopDurianRoll(PlayerController pc)
    {
        isDurianRollingLocal = false;
        durianRollTimer = 0f;
        if (pc != null) pc.isDurianRolling = false;
        if (localDurianRollVisual != null)
        {
            Destroy(localDurianRollVisual);
            localDurianRollVisual = null;
        }
    }

    private void FireMoonflowerLaserBeam(PlayerController pc)
    {
        arenaAbilityCooldownTimer = 1.0f;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Ray aimRay = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 beamStart = pc.transform.position + new Vector3(0f, 1.25f, 0f) + targetCamera.transform.forward * 0.35f;

        bool hitOpponent = CheckCursorRayHitOpponent(aimRay, 0.55f, 65f, out Vector3 hitPoint);
        Spawn3DBeamEffect(beamStart, hitPoint, new Color(0.25f, 0.95f, 1.0f), 0.28f, 0.45f);
        OnArenaAbilityFxLocal?.Invoke("BEAM", beamStart, hitPoint);

        if (hitOpponent)
        {
            RegisterArenaHit("MOON BEAM CONNECTED!");
        }
    }

    private void FireRoseHitscanThorn(PlayerController pc)
    {
        arenaAbilityCooldownTimer = 0.35f;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Ray aimRay = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 shotStart = pc.transform.position + new Vector3(0f, 0.52f, 0f) + targetCamera.transform.forward * 0.45f + targetCamera.transform.right * 0.18f;

        bool hitOpponent = CheckCursorRayHitOpponent(aimRay, 0.32f, 65f, out Vector3 hitPoint);
        Spawn3DBeamEffect(shotStart, hitPoint, new Color(1.0f, 0.15f, 0.38f), 0.10f, 0.22f);
        OnArenaAbilityFxLocal?.Invoke("THORN", shotStart, hitPoint);

        if (hitOpponent)
        {
            RegisterArenaHit("ROSE HITSCAN THORN CONNECTED!");
        }
    }

    private void PerformZombieMeleeSlash(PlayerController pc)
    {
        arenaAbilityCooldownTimer = 0.55f;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Vector3 slashCenter = targetCamera.transform.position + targetCamera.transform.forward * 1.15f;
        Spawn3DSlashEffect(slashCenter, targetCamera.transform.rotation);
        OnArenaAbilityFxLocal?.Invoke("SLASH", slashCenter, targetCamera.transform.forward);

        Ray aimRay = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        bool hitOpponent = CheckCursorRayHitOpponent(aimRay, 0.85f, 3.6f, out _);

        if (!hitOpponent && remoteArenaAvatar != null)
        {
            Vector3 toOpp = remoteArenaAvatar.transform.position - pc.transform.position;
            float dist = toOpp.magnitude;
            float angle = Vector3.Angle(targetCamera.transform.forward, toOpp);
            if (dist <= 3.6f && angle <= 58f)
            {
                hitOpponent = true;
            }
        }

        if (hitOpponent)
        {
            RegisterArenaHit("ZOMBIE MELEE SLASH CONNECTED!");
        }
    }

    private bool CheckCursorRayHitOpponent(Ray aimRay, float sphereRadius, float maxDist, out Vector3 endPoint)
    {
        endPoint = aimRay.GetPoint(maxDist);
        bool hitOpponent = false;
        float closestEnvDist = maxDist;

        // First find closest environment obstacle along ray
        RaycastHit[] directHits = Physics.RaycastAll(aimRay, maxDist);
        for (int i = 0; i < directHits.Length; i++)
        {
            Collider c = directHits[i].collider;
            if (c == null) continue;
            if (c.GetComponent<PlayerController>() != null) continue;

            if (IsOpponentHitbox(c))
            {
                if (directHits[i].distance <= closestEnvDist)
                {
                    hitOpponent = true;
                    endPoint = directHits[i].point;
                    closestEnvDist = directHits[i].distance;
                }
            }
            else if (directHits[i].distance < closestEnvDist)
            {
                closestEnvDist = directHits[i].distance;
                if (!hitOpponent) endPoint = directHits[i].point;
            }
        }

        if (hitOpponent)
        {
            return true;
        }

        // Also check generous spherecast along the cursor ray for the opponent's hitbox
        RaycastHit[] sphereHits = Physics.SphereCastAll(aimRay, sphereRadius, closestEnvDist);
        for (int i = 0; i < sphereHits.Length; i++)
        {
            if (IsOpponentHitbox(sphereHits[i].collider))
            {
                endPoint = sphereHits[i].point != Vector3.zero ? sphereHits[i].point : remoteArenaAvatar.transform.position;
                return true;
            }
        }

        return false;
    }

    private bool IsOpponentHitbox(Collider col)
    {
        if (col == null || remoteArenaAvatar == null) return false;
        return col.transform == remoteArenaAvatar.transform || col.transform.IsChildOf(remoteArenaAvatar.transform);
    }

    private void RegisterArenaHit(string label)
    {
        // Note: Attacks do not reduce HP for now; they count as a hit and trigger the Hit Indicator!
        arenaHitsLanded++;
        arenaHitIndicatorTimer = 0.95f;
        arenaLastHitLabel = label;
        remoteHitFlashTimer = 0.25f;
    }

    public void SpawnRemoteArenaAbilityFx(string fxType, Vector3 startPos, Vector3 endPos)
    {
        if (!isIn3DArena || arena3DRoot == null) return;

        if (fxType == "BEAM")
        {
            Spawn3DBeamEffect(startPos, endPos, new Color(0.25f, 0.95f, 1.0f), 0.28f, 0.45f);
        }
        else if (fxType == "THORN")
        {
            Spawn3DBeamEffect(startPos, endPos, new Color(1.0f, 0.15f, 0.38f), 0.10f, 0.22f);
        }
        else if (fxType == "SLASH")
        {
            Quaternion rot = endPos.sqrMagnitude > 0.001f ? Quaternion.LookRotation(endPos) : Quaternion.identity;
            Spawn3DSlashEffect(startPos, rot);
        }
    }

    private void Spawn3DBeamEffect(Vector3 start, Vector3 end, Color color, float thickness, float duration)
    {
        if (arena3DRoot == null) return;
        Vector3 diff = end - start;
        float len = diff.magnitude;
        if (len < 0.05f) return;

        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "ArenaBeamFX";
        beam.transform.SetParent(arena3DRoot.transform, true);
        beam.transform.position = start + diff * 0.5f;
        beam.transform.up = diff.normalized;
        beam.transform.localScale = new Vector3(thickness, len * 0.5f, thickness);
        DestroyCollider(beam);
        beam.GetComponent<Renderer>().material = CreateColoredMaterial(color);
        Destroy(beam, duration);
    }

    private void Spawn3DSlashEffect(Vector3 center, Quaternion viewRot)
    {
        if (arena3DRoot == null) return;
        GameObject slashRoot = new GameObject("ZombieSlashFX");
        slashRoot.transform.SetParent(arena3DRoot.transform, true);
        slashRoot.transform.SetPositionAndRotation(center, viewRot);

        for (int i = -1; i <= 1; i++)
        {
            GameObject claw = GameObject.CreatePrimitive(PrimitiveType.Cube);
            claw.transform.SetParent(slashRoot.transform, false);
            claw.transform.localPosition = new Vector3(i * 0.22f, 0f, 0f);
            claw.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
            claw.transform.localScale = new Vector3(0.08f, 0.95f, 0.08f);
            DestroyCollider(claw);
            claw.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.85f, 0.35f, 1.0f));
        }

        Destroy(slashRoot, 0.20f);
    }

    public void Exit3DArenaBackTo2DWall()
    {
        Exit3DArenaBackTo2DWall(destroyZombie: true);
    }

    public void Exit3DArenaBackTo2DWall(bool destroyZombie)
    {
        PlayerController chosenPlayer = GetPrimaryPlayerController();
        CancelAllActiveArenaAbilities(chosenPlayer);

        isIn3DArena = false;
        isZoomingIntoClash = false;
        arenaResolving = false;
        hasRemoteArenaPose = false;

        if (arena3DRoot != null)
        {
            Destroy(arena3DRoot);
            arena3DRoot = null;
            remoteArenaAvatar = null;
            remoteHpBarFill = null;
            remoteHpBarFillRenderer = null;
        }

        // Remove the clashing zombie so it doesn't immediately re-trigger on the exact same frame
        if (destroyZombie)
        {
            if (clashingZombie != null)
            {
                Destroy(clashingZombie.gameObject);
            }
            else if (clashingPlantRow >= 0)
            {
                Vector3 refPos = GetTileWorldPosition(clashingPlantRow, clashingPlantCol, 0f);
                WallZombie best = null;
                float bestDist = float.MaxValue;
                for (int i = 0; i < WallZombie.ActiveZombies.Count; i++)
                {
                    WallZombie z = WallZombie.ActiveZombies[i];
                    if (z == null || z.row != clashingPlantRow) continue;
                    float d = Mathf.Abs(z.transform.position.x - refPos.x);
                    if (d < bestDist) { bestDist = d; best = z; }
                }
                if (best != null) Destroy(best.gameObject);
            }
        }
        clashingZombie = null;

        if (chosenPlayer != null)
        {
            chosenPlayer.inputFrozen = false;
            chosenPlayer.ExitFirstPersonArena();
        }

        is2DWorldPaused = false;
        SyncWallScaleAndCamera();
    }

    public void TriggerPlantsWin(bool notifyNetwork = true)
    {
        if (matchResult != MatchResult.InProgress) return;
        matchResult = MatchResult.PlantsWin;
        is2DWorldPaused = true;
        isPlantSelected = false;
        isZombieSelected = false;
        HideAllPreviewGhosts();

        if (isIn3DArena)
        {
            Exit3DArenaBackTo2DWall();
        }

        if (notifyNetwork)
        {
            OnMatchEndedLocal?.Invoke(MatchResult.PlantsWin);
        }
    }

    public void TriggerZombiesWin(bool notifyNetwork = true)
    {
        if (matchResult != MatchResult.InProgress) return;
        matchResult = MatchResult.ZombiesWin;
        is2DWorldPaused = true;
        isPlantSelected = false;
        isZombieSelected = false;
        HideAllPreviewGhosts();

        if (isIn3DArena)
        {
            Exit3DArenaBackTo2DWall();
        }

        if (notifyNetwork)
        {
            OnMatchEndedLocal?.Invoke(MatchResult.ZombiesWin);
        }
    }

    public void ResetMatch(bool notifyNetwork = true)
    {
        matchResult = MatchResult.InProgress;
        matchTimeRemaining = matchDuration;
        is2DWorldPaused = false;
        isZoomingIntoClash = false;
        isPlantSelected = false;
        isZombieSelected = false;

        if (isIn3DArena)
        {
            Exit3DArenaBackTo2DWall();
        }

        // Destroy all active zombies
        WallZombie[] allZombies = FindObjectsByType<WallZombie>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allZombies.Length; i++)
        {
            if (allZombies[i] != null) Destroy(allZombies[i].gameObject);
        }

        // Destroy all placed plants
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

        // Destroy all moons and projectiles
        CollectibleMoon[] allMoons = FindObjectsByType<CollectibleMoon>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allMoons.Length; i++)
        {
            if (allMoons[i] != null) Destroy(allMoons[i].gameObject);
        }
        RoseProjectile[] allSpikes = FindObjectsByType<RoseProjectile>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allSpikes.Length; i++)
        {
            if (allSpikes[i] != null) Destroy(allSpikes[i].gameObject);
        }

        // Reset currencies & timers
        currentMoons = 100;
        currentBrains = 150;
        skyMoonTimer = 0f;
        passiveBrainTimer = 0f;
        for (int i = 0; i < cooldownTimers.Length; i++) cooldownTimers[i] = 0f;
        zombieCooldownTimer = 0f;
        clashingZombie = null;
        clashingPlant = null;

        HideAllPreviewGhosts();

        if (notifyNetwork)
        {
            OnMatchRestartedLocal?.Invoke();
        }
    }

    private PlayerController GetPrimaryPlayerController()
    {
        GameObject rootPlayer = GameObject.Find("Player");
        if (rootPlayer != null)
        {
            // Disable any duplicate child PlayerController so only the root Player moves
            foreach (Transform child in rootPlayer.transform)
            {
                PlayerController childPc = child.GetComponent<PlayerController>();
                if (childPc != null)
                {
                    childPc.enabled = false;
                }
            }

            PlayerController pc = rootPlayer.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.enabled = true;
                return pc;
            }
        }

        return FindFirstObjectByType<PlayerController>();
    }

    private void BuildOrShow3DArenaEnvironment()
    {
        if (arena3DRoot != null)
        {
            Destroy(arena3DRoot);
            arena3DRoot = null;
        }

        arena3DRoot = new GameObject("Arena3D_1v1");
        Vector3 arenaCenter = new Vector3(0f, 0f, -26.0f);
        arena3DRoot.transform.position = arenaCenter;

        // Expanded 3D Arena Floor (44 x 44)
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "ArenaFloor";
        floor.transform.SetParent(arena3DRoot.transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
        floor.transform.localScale = new Vector3(44f, 0.5f, 44f);
        floor.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.22f, 0.28f, 0.24f));

        // 4 Outer Perimeter Arena Walls
        CreateArenaBlock("Wall_North", new Vector3(0f, 1.75f, 22f), new Vector3(44f, 3.5f, 0.8f), new Color(0.32f, 0.36f, 0.42f));
        CreateArenaBlock("Wall_South", new Vector3(0f, 1.75f, -22f), new Vector3(44f, 3.5f, 0.8f), new Color(0.32f, 0.36f, 0.42f));
        CreateArenaBlock("Wall_East", new Vector3(22f, 1.75f, 0f), new Vector3(0.8f, 3.5f, 44f), new Color(0.32f, 0.36f, 0.42f));
        CreateArenaBlock("Wall_West", new Vector3(-22f, 1.75f, 0f), new Vector3(0.8f, 3.5f, 44f), new Color(0.32f, 0.36f, 0.42f));

        // Cover pillars & new tactical obstacles inside the expanded arena
        CreateArenaBlock("Pillar_Left", new Vector3(-5.5f, 1.4f, 0f), new Vector3(1.8f, 2.8f, 1.8f), new Color(0.45f, 0.52f, 0.48f));
        CreateArenaBlock("Pillar_Right", new Vector3(5.5f, 1.4f, 0f), new Vector3(1.8f, 2.8f, 1.8f), new Color(0.45f, 0.52f, 0.48f));

        CreateArenaBlock("Cover_NW", new Vector3(-9.5f, 0.85f, 7.0f), new Vector3(2.8f, 1.7f, 1.0f), new Color(0.38f, 0.44f, 0.40f));
        CreateArenaBlock("Cover_NE", new Vector3(9.5f, 0.85f, 7.0f), new Vector3(2.8f, 1.7f, 1.0f), new Color(0.38f, 0.44f, 0.40f));
        CreateArenaBlock("Cover_SW", new Vector3(-9.5f, 0.85f, -7.0f), new Vector3(2.8f, 1.7f, 1.0f), new Color(0.38f, 0.44f, 0.40f));
        CreateArenaBlock("Cover_SE", new Vector3(9.5f, 0.85f, -7.0f), new Vector3(2.8f, 1.7f, 1.0f), new Color(0.38f, 0.44f, 0.40f));

        CreateArenaBlock("Crate_West", new Vector3(-14.0f, 0.6f, 0f), new Vector3(2.4f, 1.2f, 2.8f), new Color(0.48f, 0.36f, 0.24f));
        CreateArenaBlock("Crate_East", new Vector3(14.0f, 0.6f, 0f), new Vector3(2.4f, 1.2f, 2.8f), new Color(0.48f, 0.36f, 0.24f));

        // Spawn the OTHER player's avatar (synced over the network) with accurate hitboxes.
        // Plant player sees the Zombie; Zombie player sees the specific plant that was touched.
        MultiplayerMenuManager.PlayerRole myRole = GetEffectiveLocalRole();
        bool localIsPlant = myRole == MultiplayerMenuManager.PlayerRole.Plant;
        PlantType touchedType = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;

        remoteArenaAvatar = new GameObject("Arena3D_RemotePlayer");
        remoteArenaAvatar.transform.SetParent(arena3DRoot.transform, false);
        Vector3 remoteSpawn = localIsPlant ? ArenaZombieSpawn : ArenaPlantSpawn;
        remoteArenaAvatar.transform.SetPositionAndRotation(remoteSpawn, Quaternion.Euler(0f, localIsPlant ? 180f : 0f, 0f));

        // Primary body hitbox + upper head/blossom hitbox for each plant and zombie in the 3D 1v1 zone
        CapsuleCollider bodyHitbox = remoteArenaAvatar.AddComponent<CapsuleCollider>();
        bodyHitbox.height = 2.0f;
        bodyHitbox.radius = (!localIsPlant && touchedType == PlantType.Durian) ? 0.78f : 0.58f;
        bodyHitbox.center = Vector3.zero;

        SphereCollider headHitbox = remoteArenaAvatar.AddComponent<SphereCollider>();
        headHitbox.radius = (!localIsPlant && touchedType == PlantType.Durian) ? 0.72f : 0.52f;
        headHitbox.center = new Vector3(0f, 0.35f, 0f);

        Rigidbody rb = remoteArenaAvatar.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        GameObject visual;
        if (localIsPlant)
        {
            visual = CreateZombieVisual("Visual_Zombie", isGhost: false);
            currentArenaOpponentName = "ZOMBIE PLAYER";

            // Strip any colliders on the visual so only the capsule/sphere hitboxes are used
            foreach (Collider c in visual.GetComponentsInChildren<Collider>())
            {
                Destroy(c);
            }
            visual.transform.SetParent(remoteArenaAvatar.transform, false);
            visual.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // flat 2D art faces the avatar's forward
            visual.transform.localScale = Vector3.one * 1.1f;

            // Mirror copy on the back so the texture is visible from every side
            GameObject back = Instantiate(visual, remoteArenaAvatar.transform, false);
            back.name = visual.name + "_Back";
            back.transform.localRotation = Quaternion.identity;
            foreach (MonoBehaviour mb in back.GetComponentsInChildren<MonoBehaviour>())
            {
                mb.enabled = false;
            }
        }
        else
        {
            visual = Create3DPlantPlaceholder(touchedType, $"Visual_3D_{touchedType}");
            currentArenaOpponentName = $"{touchedType.ToString().ToUpper()} PLAYER";

            visual.transform.SetParent(remoteArenaAvatar.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
        }

        // World-space 3D HP Bar above the opponent (no text sign over their head)
        CreateRemoteWorldHPBar(remoteArenaAvatar.transform, localIsPlant);
    }

    private void CreateRemoteWorldHPBar(Transform parent, bool opponentIsZombie)
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
        DestroyCollider(bgQuad);
        bgQuad.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.08f, 0.08f, 0.10f));

        GameObject fillQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fillQuad.name = "HPBar_Fill";
        fillQuad.transform.SetParent(hpRoot.transform, false);
        fillQuad.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        fillQuad.transform.localScale = new Vector3(1.24f, 0.12f, 1f);
        DestroyCollider(fillQuad);

        remoteHpBarBaseColor = opponentIsZombie ? new Color(0.82f, 0.32f, 0.98f) : new Color(0.28f, 0.92f, 0.38f);
        remoteHpBarFillRenderer = fillQuad.GetComponent<Renderer>();
        remoteHpBarFillRenderer.material = CreateColoredMaterial(remoteHpBarBaseColor);
        remoteHpBarFill = fillQuad.transform;
    }

    private void CreateArenaBlock(string name, Vector3 localPos, Vector3 scale, Color color)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(arena3DRoot.transform, false);
        block.transform.localPosition = localPos;
        block.transform.localScale = scale;
        block.GetComponent<Renderer>().material = CreateColoredMaterial(color);
    }

    // ==================== PLANT & ZOMBIE SELECTION / PLACEMENT ====================

    public int GetPlantCost(PlantType type)
    {
        switch (type)
        {
            case PlantType.Moonflower: return moonflowerCost;
            case PlantType.Rose: return roseCost;
            case PlantType.Durian: return durianCost;
            default: return 50;
        }
    }

    public float GetPlantMaxCooldown(PlantType type)
    {
        switch (type)
        {
            case PlantType.Moonflower: return moonflowerCooldown;
            case PlantType.Rose: return roseCooldown;
            case PlantType.Durian: return durianCooldown;
            default: return 5f;
        }
    }

    public float GetPlantCooldownRemaining(PlantType type)
    {
        return cooldownTimers[(int)type];
    }

    public bool CanAffordAndReady(PlantType type)
    {
        return currentMoons >= GetPlantCost(type) && GetPlantCooldownRemaining(type) <= 0.001f;
    }

    public bool CanAffordAndReadyZombie()
    {
        return currentBrains >= zombieCost && zombieCooldownTimer <= 0.001f;
    }

    public void TrySelectPlant(PlantType type)
    {
        if (!canPlacePlants)
        {
            return;
        }

        isZombieSelected = false;

        if (isPlantSelected && selectedPlantType == type)
        {
            isPlantSelected = false;
            return;
        }

        if (!CanAffordAndReady(type))
        {
            return;
        }

        selectedPlantType = type;
        isPlantSelected = true;
    }

    public void TrySelectZombie()
    {
        if (!canPlaceZombies)
        {
            return;
        }

        isPlantSelected = false;

        if (isZombieSelected)
        {
            isZombieSelected = false;
            return;
        }

        if (!CanAffordAndReadyZombie())
        {
            return;
        }

        isZombieSelected = true;
    }

    public bool IsColumnAllowedForZombie(int col)
    {
        // Must be in the rightmost columns (e.g. columns 6 and 7 in an 8-column grid)
        int minAllowedCol = Mathf.Max(0, columns - zombieAllowedColumnsFromRight);
        return col >= minAllowedCol;
    }

    public void AddMoons(int amount)
    {
        currentMoons += amount;
    }

    public Vector3 GetMoonBankWorldPosition()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            Vector2 bankCenterGui = moonBankRect.center;
            Vector3 screenPt = new Vector3(bankCenterGui.x, Screen.height - bankCenterGui.y, 0f);
            Ray ray = targetCamera.ScreenPointToRay(screenPt);
            Vector3 wallFrontCenter = transform.position - transform.forward * (wallThickness * 0.5f + 0.4f);
            Plane plane = new Plane(-transform.forward, wallFrontCenter);
            if (plane.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }
        }

        return new Vector3(
            transform.position.x - wallWidth * 0.42f,
            transform.position.y + wallHeight * 0.40f,
            transform.position.z - 0.5f
        );
    }

    public void SpawnSkyFallingMoon()
    {
        float marginX = 1.2f;
        float randomX = UnityEngine.Random.Range(
            transform.position.x - wallWidth * 0.5f + marginX,
            transform.position.x + wallWidth * 0.5f - marginX
        );
        float startY = transform.position.y + wallHeight * 0.5f + 0.6f;
        float landingY = UnityEngine.Random.Range(
            transform.position.y - wallHeight * 0.38f,
            transform.position.y + wallHeight * 0.25f
        );
        float z = transform.position.z - (wallThickness * 0.5f) - 0.45f;

        int id = nextMoonId++;
        Vector3 startPos = new Vector3(randomX, startY, z);
        SpawnSkyMoonWithId(id, startPos, landingY, skyMoonValue);
        OnSkyMoonSpawnedLocal?.Invoke(id, startPos, landingY, skyMoonValue);
    }

    public void SpawnSkyMoonWithId(int id, Vector3 startPos, float landingY, int value)
    {
        GameObject moonObj = CreateMoonVisual("SkyMoon");
        if (tilesContainer != null)
        {
            moonObj.transform.SetParent(tilesContainer.transform, true);
        }

        CollectibleMoon moon = moonObj.AddComponent<CollectibleMoon>();
        moon.networkId = id;
        moon.SetupSkyFall(this, startPos, new Vector3(startPos.x, landingY, startPos.z), value);
    }

    public void SpawnMoonFromFlower(Vector3 flowerWorldPos, int amount = 50)
    {
        // Only the moon authority (Plant player) creates moons; the other player gets them over the network
        if (!IsMoonAuthority) return;

        int id = nextMoonId++;
        SpawnFlowerMoonWithId(id, flowerWorldPos, amount);
        OnFlowerMoonSpawnedLocal?.Invoke(id, flowerWorldPos, amount);
    }

    public void SpawnFlowerMoonWithId(int id, Vector3 flowerWorldPos, int amount)
    {
        GameObject moonObj = CreateMoonVisual("MoonflowerMoon");
        if (tilesContainer != null)
        {
            moonObj.transform.SetParent(tilesContainer.transform, true);
        }

        CollectibleMoon moon = moonObj.AddComponent<CollectibleMoon>();
        moon.networkId = id;
        moon.SetupMoonflowerDrop(this, flowerWorldPos + new Vector3(0f, 0.2f, -0.2f), amount);
    }

    /// <summary>The Plant player picked these moons up: remove them from this (Zombie) view.</summary>
    public void RemoveMoonsRemote(System.Collections.Generic.List<int> ids)
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

    public void SpawnRoseProjectile(int row, Vector3 spawnPos)
    {
        GameObject proj = GameVisuals.CreateRoseProjectile("RoseThornProjectile");
        if (tilesContainer != null)
        {
            proj.transform.SetParent(tilesContainer.transform, true);
        }

        proj.transform.position = spawnPos;

        RoseProjectile rp = proj.AddComponent<RoseProjectile>();
        rp.Initialize(this, row, transform.position.x + wallWidth * 0.5f);
    }

    public void SpawnRoseProjectile(Vector3 spawnPos)
    {
        SpawnRoseProjectile(0, spawnPos);
    }

    private GameObject CreateMoonVisual(string name)
    {
        return GameVisuals.CreateMoon(name);
    }

    /// <summary>
    /// Picks up the clicked moon AND every moon gathered around it within moonClusterPickupRadius!
    /// Only the Plant player can interact with and collect moons.
    /// </summary>
    private bool TryClickCollectibleMoon()
    {
        // If the player is a Zombie player, they cannot interact with or collect moons!
        if (localRole == MultiplayerMenuManager.PlayerRole.Zombie)
        {
            return false;
        }

        if (targetCamera == null) return false;

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 wallFrontCenter = transform.position - transform.forward * (wallThickness * 0.5f + 0.4f);
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

        // Collect the clicked moon AND all moons around it so you never have to spam-click!
        Vector2 centerPos = new Vector2(primaryMoon.transform.position.x, primaryMoon.transform.position.y);
        var collectedIds = new System.Collections.Generic.List<int>();
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

            if (distToCluster <= moonClusterPickupRadius || distToClick <= moonClusterPickupRadius)
            {
                moon.Collect();
                collectedIds.Add(moon.networkId);
            }
        }

        if (collectedIds.Count > 0)
        {
            OnMoonsCollectedLocal?.Invoke(collectedIds);
        }

        return true;
    }

    private void UpdateHoveredTile()
    {
        hoveredRow = -1;
        hoveredCol = -1;

        if (targetCamera == null)
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 wallFrontCenter = transform.position - transform.forward * (wallThickness * 0.5f);
        Plane wallPlane = new Plane(-transform.forward, wallFrontCenter);

        if (wallPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            float leftEdge = transform.position.x - (wallWidth * 0.5f);
            float bottomEdge = transform.position.y - (wallHeight * 0.5f);

            float localX = hitPoint.x - leftEdge;
            float localY = hitPoint.y - bottomEdge;

            if (localX >= 0f && localX < wallWidth && localY >= 0f && localY < wallHeight)
            {
                hoveredCol = Mathf.Clamp(Mathf.FloorToInt(localX / TileWidth), 0, columns - 1);
                hoveredRow = Mathf.Clamp(Mathf.FloorToInt(localY / TileHeight), 0, rows - 1);
            }
        }
    }

    private void HideAllPreviewGhosts()
    {
        for (int i = 0; i < previewGhosts.Length; i++)
        {
            if (previewGhosts[i] != null && previewGhosts[i].activeSelf)
            {
                previewGhosts[i].SetActive(false);
            }
        }
        if (zombiePreviewGhost != null && zombiePreviewGhost.activeSelf)
        {
            zombiePreviewGhost.SetActive(false);
        }
    }

    private void UpdateTileHighlightsAndGhost()
    {
        if (tileRenderers == null)
        {
            return;
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                Renderer rend = tileRenderers[r, c];
                if (rend == null) continue;

                if (r == hoveredRow && c == hoveredCol)
                {
                    if (isZombieSelected)
                    {
                        // Check if this column is valid for zombie placement (last 2 columns on the right)
                        bool allowed = IsColumnAllowedForZombie(c);
                        rend.material.color = allowed ? tileZombieHoverColor : new Color(0.85f, 0.2f, 0.2f, 1.0f);
                    }
                    else
                    {
                        bool occupied = placedPlants[r, c] != null;
                        rend.material.color = occupied ? tileOccupiedColor : tileHoverColor;
                    }
                }
                else
                {
                    rend.material.color = GetBaseTileColor(r, c);
                }
            }
        }

        bool showPlantGhost = isPlantSelected
                              && CanAffordAndReady(selectedPlantType)
                              && hoveredRow >= 0
                              && hoveredCol >= 0
                              && placedPlants[hoveredRow, hoveredCol] == null;

        for (int i = 0; i < previewGhosts.Length; i++)
        {
            GameObject ghost = previewGhosts[i];
            if (ghost == null) continue;

            bool active = showPlantGhost && ((int)selectedPlantType == i);
            ghost.SetActive(active);
            if (active)
            {
                ghost.transform.position = GetTileWorldPosition(hoveredRow, hoveredCol, -0.25f);
            }
        }

        if (zombiePreviewGhost != null)
        {
            bool showZombieGhost = isZombieSelected
                                   && CanAffordAndReadyZombie()
                                   && hoveredRow >= 0
                                   && hoveredCol >= 0
                                   && IsColumnAllowedForZombie(hoveredCol);

            zombiePreviewGhost.SetActive(showZombieGhost);
            if (showZombieGhost)
            {
                zombiePreviewGhost.transform.position = GetTileWorldPosition(hoveredRow, hoveredCol, -0.3f);
            }
        }
    }

    private void HandleMouseClick()
    {
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        // 1. Check if the player clicked on a Moon (collects all moons gathered around it!)
        if (TryClickCollectibleMoon())
        {
            return;
        }

        // 2. Ignore clicks inside the top UI bar
        Vector2 guiMousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (canPlacePlants)
        {
            if (moonBankRect.Contains(guiMousePos)) return;
            for (int i = 0; i < plantButtonRects.Length; i++)
            {
                if (plantButtonRects[i].Contains(guiMousePos)) return;
            }
        }
        if (canPlaceZombies)
        {
            if (brainBankRect.Contains(guiMousePos) || zombieButtonRect.Contains(guiMousePos)) return;
        }

        if (hoveredRow < 0 || hoveredCol < 0)
        {
            return;
        }

        // 3. Place Zombie if Zombie is selected (Only allowed on the last 2 columns on the right)
        if (isZombieSelected && CanAffordAndReadyZombie())
        {
            if (!IsColumnAllowedForZombie(hoveredCol))
            {
                // Disallowed column - feedback: do not spend brains
                return;
            }

            currentBrains -= zombieCost;
            zombieCooldownTimer = zombieCooldown;
            SpawnZombieOnTile(hoveredRow, hoveredCol, notifyNetwork: true);
            isZombieSelected = false;
            return;
        }

        // 4. Place Plant if Plant is selected
        if (isPlantSelected && CanAffordAndReady(selectedPlantType))
        {
            if (placedPlants[hoveredRow, hoveredCol] == null)
            {
                int cost = GetPlantCost(selectedPlantType);
                currentMoons -= cost;
                cooldownTimers[(int)selectedPlantType] = GetPlantMaxCooldown(selectedPlantType);

                PlacePlantOnTile(hoveredRow, hoveredCol, selectedPlantType, notifyNetwork: true);
                isPlantSelected = false;
            }
        }
    }

    public WallZombie SpawnZombieOnTile(int row, int col, bool notifyNetwork = false)
    {
        if (row < 0 || row >= rows)
        {
            return null;
        }

        int clampedCol = Mathf.Clamp(col, 0, columns - 1);
        Vector3 spawnPos = GetTileWorldPosition(row, clampedCol, -0.3f);

        GameObject zombieObj = CreateZombieVisual($"Zombie_{row}_{clampedCol}", isGhost: false);
        if (tilesContainer != null)
        {
            zombieObj.transform.SetParent(tilesContainer.transform, true);
        }

        WallZombie zombieComp = zombieObj.AddComponent<WallZombie>();
        zombieComp.Initialize(this, row, spawnPos);

        if (notifyNetwork)
        {
            OnZombiePlacedLocal?.Invoke(row, clampedCol);
        }

        return zombieComp;
    }

    public PlaceholderPlant PlacePlantOnTile(int row, int col, PlantType type = PlantType.Moonflower, bool notifyNetwork = false)
    {
        if (row < 0 || row >= rows || col < 0 || col >= columns)
        {
            return null;
        }

        if (placedPlants == null)
        {
            InitializeRuntimeGrid();
        }

        if (placedPlants[row, col] != null)
        {
            return placedPlants[row, col];
        }

        Vector3 spawnPos = GetTileWorldPosition(row, col, -0.25f);
        GameObject plantObj = CreatePlantVisual(type, $"{type}_{row}_{col}", isGhost: false);
        plantObj.transform.position = spawnPos;

        if (tilesContainer != null)
        {
            plantObj.transform.SetParent(tilesContainer.transform, true);
        }

        PlaceholderPlant plantComp = plantObj.GetComponent<PlaceholderPlant>();
        if (plantComp == null)
        {
            plantComp = plantObj.AddComponent<PlaceholderPlant>();
        }
        plantComp.Initialize(this, type, row, col);

        placedPlants[row, col] = plantComp;

        if (notifyNetwork)
        {
            OnPlantPlacedLocal?.Invoke(row, col, type);
        }

        return plantComp;
    }

    private GameObject CreateZombieVisual(string objectName, bool isGhost)
    {
        return GameVisuals.CreateZombie(objectName, isGhost, TileWidth, TileHeight);
    }

    private GameObject CreatePlantVisual(PlantType type, string objectName, bool isGhost)
    {
        return GameVisuals.CreatePlant(type, objectName, isGhost, TileWidth, TileHeight);
    }

    private GameObject Create3DPlantPlaceholder(PlantType type, string objectName)
    {
        GameObject root = new GameObject(objectName);

        if (type == PlantType.Moonflower)
        {
            GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            stem.transform.SetParent(root.transform, false);
            stem.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            stem.transform.localScale = new Vector3(0.36f, 0.50f, 0.36f);
            DestroyCollider(stem);
            stem.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.2f, 0.7f, 0.45f));

            GameObject petals = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            petals.name = "Petals";
            petals.transform.SetParent(root.transform, false);
            petals.transform.localPosition = new Vector3(0f, 0.25f, -0.04f);
            petals.transform.localScale = new Vector3(1.15f, 1.15f, 0.40f);
            DestroyCollider(petals);
            petals.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.45f, 0.85f, 1.0f));

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.25f, 0.08f);
            head.transform.localScale = new Vector3(0.78f, 0.78f, 0.50f);
            DestroyCollider(head);
            head.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(1.0f, 0.95f, 0.4f));
        }
        else if (type == PlantType.Rose || type == PlantType.Roze)
        {
            GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            stem.transform.SetParent(root.transform, false);
            stem.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            stem.transform.localScale = new Vector3(0.38f, 0.50f, 0.38f);
            DestroyCollider(stem);
            stem.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.15f, 0.65f, 0.25f));

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            head.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);
            DestroyCollider(head);
            head.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.95f, 0.18f, 0.32f));

            GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel";
            barrel.transform.SetParent(root.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 0.28f, 0.52f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.42f, 0.30f, 0.42f);
            DestroyCollider(barrel);
            barrel.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.8f, 0.12f, 0.25f));
        }
        else
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            body.transform.localScale = new Vector3(1.25f, 1.40f, 1.20f);
            DestroyCollider(body);
            body.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.68f, 0.58f, 0.2f));

            GameObject spikes1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spikes1.name = "Spikes";
            spikes1.transform.SetParent(root.transform, false);
            spikes1.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            spikes1.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            spikes1.transform.localScale = new Vector3(1.05f, 1.05f, 1.05f);
            DestroyCollider(spikes1);
            spikes1.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.82f, 0.72f, 0.25f));
        }

        return root;
    }

    private void DestroyCollider(GameObject obj)
    {
        Collider col = obj.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
            {
                Destroy(col);
            }
            else
            {
                DestroyImmediate(col);
            }
        }
    }

    public Vector3 GetTileWorldPosition(int row, int column, float surfaceZOffset = -0.05f)
    {
        float leftEdge = transform.position.x - (wallWidth * 0.5f);
        float bottomEdge = transform.position.y - (wallHeight * 0.5f);

        float x = leftEdge + (column + 0.5f) * TileWidth;
        float y = bottomEdge + (row + 0.5f) * TileHeight;
        float z = transform.position.z - (wallThickness * 0.5f) + surfaceZOffset;

        return new Vector3(x, y, z);
    }

    private Color GetBaseTileColor(int row, int col)
    {
        if (col >= columns - zombieAllowedColumnsFromRight)
        {
            return ((row + col) % 2 == 0)
                ? new Color(0.28f, 0.22f, 0.35f, 1.0f)
                : new Color(0.20f, 0.16f, 0.28f, 1.0f);
        }
        return ((row + col) % 2 == 0) ? tileColorLight : tileColorDark;
    }

    private Material CreateColoredMaterial(Color color)
    {
        if (sharedBaseMaterial == null)
        {
            Renderer ownRenderer = GetComponent<Renderer>();
            if (ownRenderer != null && ownRenderer.sharedMaterial != null)
            {
                sharedBaseMaterial = ownRenderer.sharedMaterial;
            }
        }

        Material mat;
        if (sharedBaseMaterial != null)
        {
            mat = new Material(sharedBaseMaterial);
        }
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Standard");
            mat = new Material(shader);
        }

        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
        return mat;
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !gameplayActive)
        {
            return;
        }

        Color prevBg = GUI.backgroundColor;

        // If zooming into clash, show clash alert banner
        if (isZoomingIntoClash)
        {
            GUIStyle clashStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            clashStyle.normal.textColor = new Color(1f, 0.9f, 0.3f);
            GUI.backgroundColor = new Color(0.15f, 0.05f, 0.05f, 0.92f);
            GUI.Box(new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.5f - 45f, 520f, 90f),
                "ZOMBIE TOUCHED PLANT!\n2D World Paused - Entering 3D 1v1 Arena...", clashStyle);
            GUI.backgroundColor = prevBg;
            return;
        }

        // If inside the 3D 1v1 Arena, show arena HUD, HP bars, Center Cursor, Ability Charge & Hit Indicator
        if (isIn3DArena)
        {
            Draw3DArenaHUD(prevBg);
            return;
        }

        // Check if Match has ended (Plants win by survival or Zombies win by breach)
        if (matchResult != MatchResult.InProgress)
        {
            DrawGameOverModal();
            return;
        }

        // TOP-CENTER HUD: 5-MINUTE COUNTDOWN TIMER
        float sw = Screen.width;
        // Dynamically position the Defense Timer so it NEVER overlaps the Durian button on the left or the Role banner on the right
        float minTimerX = canPlacePlants ? (plantButtonRects[2].x + plantButtonRects[2].width + 16f) : 285f;
        float maxTimerX = sw - 245f - 210f; // 10px buffer before the compact 220px top-right role banner
        float timerX = (maxTimerX > minTimerX) ? (minTimerX + (maxTimerX - minTimerX) * 0.5f) : minTimerX;
        Rect timerRect = new Rect(timerX, 12f, 210f, 66f);

        int mins = Mathf.FloorToInt(matchTimeRemaining / 60f);
        int secs = Mathf.FloorToInt(matchTimeRemaining % 60f);
        string timeStr = $"{mins:D2}:{secs:D2}";

        GUIStyle timerStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        if (matchTimeRemaining <= 30f)
        {
            timerStyle.normal.textColor = new Color(1.0f, 0.35f, 0.35f);
            GUI.backgroundColor = new Color(0.35f, 0.08f, 0.08f, 0.95f);
        }
        else if (matchTimeRemaining <= 60f)
        {
            timerStyle.normal.textColor = new Color(1.0f, 0.95f, 0.35f);
            GUI.backgroundColor = new Color(0.28f, 0.22f, 0.08f, 0.95f);
        }
        else
        {
            timerStyle.normal.textColor = new Color(0.9f, 0.95f, 1.0f);
            GUI.backgroundColor = new Color(0.10f, 0.16f, 0.24f, 0.92f);
        }

        GUI.Box(timerRect, $"DEFENSE TIMER\n{timeStr}\nPlants Win at 00:00", timerStyle);
        GUI.backgroundColor = prevBg;

        GUIStyle bankStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        // 1. MOON BANK DISPLAY + 3 PLANT SEED BUTTONS (Only shown for Plant Player or None)
        if (canPlacePlants)
        {
            bankStyle.normal.textColor = new Color(1.0f, 0.95f, 0.4f);
            GUI.backgroundColor = new Color(0.12f, 0.18f, 0.32f, 0.95f);
            GUI.Box(moonBankRect, $"MOONS\n{currentMoons}", bankStyle);

            DrawPlantSeedButton(0, PlantType.Moonflower, "Moonflower [1]", moonflowerCost, new Color(0.4f, 0.85f, 1.0f));
            DrawPlantSeedButton(1, PlantType.Rose, "Rose [2]", roseCost, new Color(1.0f, 0.4f, 0.5f));
            DrawPlantSeedButton(2, PlantType.Durian, "Durian [3]", durianCost, new Color(0.9f, 0.78f, 0.3f));
        }

        // 2. ZOMBIE PLAYER: BRAINS BANK + ZOMBIE SPAWN BUTTON (Only shown for Zombie Player or None)
        if (canPlaceZombies)
        {
            // Position on the far left if Plant UI is hidden; if both are active, place on second tier
            Rect actualBrainRect = canPlacePlants ? brainBankRect : new Rect(12f, 12f, 110f, 66f);
            Rect actualZombieRect = canPlacePlants ? zombieButtonRect : new Rect(126f, 12f, 145f, 66f);

            bankStyle.normal.textColor = new Color(0.95f, 0.55f, 1.0f);
            GUI.backgroundColor = new Color(0.24f, 0.10f, 0.28f, 0.95f);
            GUI.Box(actualBrainRect, $"BRAINS\n{currentBrains}", bankStyle);

            DrawZombieSpawnButton(actualZombieRect);
        }

        GUI.backgroundColor = prevBg;
    }

    private void Draw3DArenaHUD(Color prevBg)
    {
        float sw = Screen.width;
        float sh = Screen.height;
        float cx = sw * 0.5f;
        float cy = sh * 0.5f;

        MultiplayerMenuManager.PlayerRole myRole = GetEffectiveLocalRole();
        bool isZombie = myRole == MultiplayerMenuManager.PlayerRole.Zombie;
        PlantType activePlant = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;

        string myTitle = isZombie ? "YOU (ZOMBIE)" : $"YOU ({activePlant.ToString().ToUpper()})";
        string oppTitle = isZombie ? $"OPPONENT ({activePlant.ToString().ToUpper()})" : "OPPONENT (ZOMBIE)";

        string abilityHint;
        if (isZombie)
        {
            abilityHint = "[Left Click / E] Melee Slash Attack";
        }
        else if (activePlant == PlantType.Moonflower)
        {
            abilityHint = "[Left Click / E] Moon Beam (2.0s Charge Over Head -> Laser at Cursor)";
        }
        else if (activePlant == PlantType.Rose || activePlant == PlantType.Roze)
        {
            abilityHint = "[Left Click / E] Hitscan Thorn Shot (0.4s Charge)";
        }
        else
        {
            abilityHint = "[Left Click / E] Durian Roll (Lowers FOV, Harder Turn, 2.0s Zombie Immobilize on Hit)";
        }

        // 1. Top-Center Instructions Banner
        GUIStyle arenaHud = new GUIStyle(GUI.skin.box)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        arenaHud.normal.textColor = Color.white;
        GUI.backgroundColor = new Color(0.10f, 0.14f, 0.22f, 0.88f);
        GUI.Box(new Rect(cx - 310f, 12f, 620f, 54f),
            $"3D 1v1 ARENA • {abilityHint}\nWASD: Move | Space: Jump | Hits Connected: {arenaHitsLanded} | Host: N = Zombie Won, M = Plant Won",
            arenaHud);

        // 2. Top-Left & Top-Right HP Bars for Local Player and Opponent
        Color myBarCol = isZombie ? new Color(0.78f, 0.32f, 0.96f) : new Color(0.25f, 0.90f, 0.38f);
        Color oppBarCol = isZombie ? new Color(0.25f, 0.90f, 0.38f) : new Color(0.78f, 0.32f, 0.96f);
        if (remoteHitFlashTimer > 0f)
        {
            oppBarCol = new Color(1.0f, 0.95f, 0.35f);
        }

        DrawArenaHPBar(new Rect(18f, 14f, 240f, 48f), myTitle, localArenaCurrentHP, localArenaMaxHP, myBarCol);
        DrawArenaHPBar(new Rect(sw - 258f, 14f, 240f, 48f), oppTitle, remoteArenaCurrentHP, remoteArenaMaxHP, oppBarCol);

        // 3. Center Cursor (Crosshair) in the exact middle of the screen
        Color prevColor = GUI.color;
        bool hitActive = arenaHitIndicatorTimer > 0f;
        GUI.color = hitActive ? new Color(1.0f, 0.30f, 0.30f, 0.98f) : new Color(0.95f, 1.0f, 0.95f, 0.92f);

        // Center dot
        GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
        // 4 Crosshair ticks (left, right, top, bottom)
        GUI.DrawTexture(new Rect(cx - 14f, cy - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx + 6f, cy - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy - 14f, 2f, 8f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy + 6f, 2f, 8f), Texture2D.whiteTexture);

        // 4. Hit Marker & Hit Connected Indicator around the center cursor when an attack connects
        if (hitActive)
        {
            GUI.color = new Color(1.0f, 0.88f, 0.25f, 0.98f);
            // 4 corner hit-marker brackets around center cursor
            GUI.DrawTexture(new Rect(cx - 11f, cy - 11f, 5f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 11f, cy - 11f, 2f, 5f), Texture2D.whiteTexture);

            GUI.DrawTexture(new Rect(cx + 6f, cy - 11f, 5f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 9f, cy - 11f, 2f, 5f), Texture2D.whiteTexture);

            GUI.DrawTexture(new Rect(cx - 11f, cy + 9f, 5f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 11f, cy + 6f, 2f, 5f), Texture2D.whiteTexture);

            GUI.DrawTexture(new Rect(cx + 6f, cy + 9f, 5f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 9f, cy + 6f, 2f, 5f), Texture2D.whiteTexture);

            GUI.color = prevColor;
            GUIStyle hitStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            hitStyle.normal.textColor = new Color(1.0f, 0.95f, 0.35f);
            GUI.backgroundColor = new Color(0.35f, 0.06f, 0.06f, 0.92f);
            GUI.Box(new Rect(cx - 190f, cy + 26f, 380f, 32f),
                $"★ {arenaLastHitLabel} (Hits: {arenaHitsLanded}) ★", hitStyle);
        }
        GUI.color = prevColor;

        // 5. Ability Charge / Roll Progress Bar below the Center Cursor
        if (isChargingMoonBeam)
        {
            float pct = 1.0f - Mathf.Clamp01(moonBeamChargeTimer / Mathf.Max(0.01f, moonBeamChargeDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, $"CHARGING MOON BEAM... {(pct * 100f):F0}%", new Color(0.30f, 0.95f, 1.0f));
        }
        else if (isChargingRoseThorn)
        {
            float pct = 1.0f - Mathf.Clamp01(roseThornChargeTimer / Mathf.Max(0.01f, roseThornChargeDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, "CHARGING THORN...", new Color(1.0f, 0.25f, 0.45f));
        }
        else if (isDurianRollingLocal)
        {
            float pct = Mathf.Clamp01(durianRollTimer / Mathf.Max(0.01f, durianRollDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, "DURIAN ROLLING! (RAM ZOMBIE TO IMMOBILIZE)", new Color(0.95f, 0.80f, 0.25f));
        }

        // 6. Immobilized Banner if Zombie Player was hit by Durian Roll
        PlayerController pc = GetPrimaryPlayerController();
        if (pc != null && pc.IsImmobilized)
        {
            GUIStyle stunStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            stunStyle.normal.textColor = new Color(1.0f, 0.85f, 0.25f);
            GUI.backgroundColor = new Color(0.28f, 0.08f, 0.05f, 0.94f);
            GUI.Box(new Rect(cx - 250f, cy - 95f, 500f, 46f),
                $"⚡ IMMOBILIZED BY DURIAN! ({pc.immobilizedTimer:F1}s) — Movement & Attacks Locked ⚡", stunStyle);
        }
        else if (remoteImmobilizedTimer > 0f)
        {
            GUIStyle oppStunStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            oppStunStyle.normal.textColor = new Color(0.45f, 1.0f, 0.55f);
            GUI.backgroundColor = new Color(0.08f, 0.24f, 0.12f, 0.90f);
            GUI.Box(new Rect(cx - 210f, cy + 64f, 420f, 30f),
                $"ZOMBIE IMMOBILIZED & UNABLE TO ATTACK ({remoteImmobilizedTimer:F1}s)", oppStunStyle);
        }

        // 7. Arena Resolution Banner (N / M)
        if (arenaResolving)
        {
            GUIStyle winStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 46,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            winStyle.normal.textColor = arenaZombieWon ? new Color(0.8f, 0.45f, 1f) : new Color(0.5f, 1f, 0.45f);
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.85f);
            float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.04f;
            float w = 560f * pulse, h = 130f * pulse;
            GUI.Box(new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h),
                arenaZombieWon ? "ZOMBIE WON!" : "PLANT WON!", winStyle);
        }

        GUI.backgroundColor = prevBg;
    }

    private void DrawArenaHPBar(Rect rect, string label, int currentHP, int maxHP, Color fillColor)
    {
        Color prevColor = GUI.color;
        GUI.color = new Color(0.06f, 0.08f, 0.12f, 0.94f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        float pct = Mathf.Clamp01((float)currentHP / Mathf.Max(1, maxHP));
        Rect fillRect = new Rect(rect.x + 4f, rect.y + 24f, (rect.width - 8f) * pct, 18f);
        GUI.color = fillColor;
        GUI.DrawTexture(fillRect, Texture2D.whiteTexture);

        GUI.color = prevColor;
        GUIStyle textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        textStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(rect.x, rect.y + 3f, rect.width, 20f), $"{label}  •  HP: {currentHP} / {maxHP}", textStyle);
    }

    private void DrawCenterChargeBar(float cx, float y, float pct, string text, Color fillCol)
    {
        Color prevColor = GUI.color;
        Rect bgRect = new Rect(cx - 110f, y, 220f, 20f);
        GUI.color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
        GUI.DrawTexture(bgRect, Texture2D.whiteTexture);

        Rect fillRect = new Rect(bgRect.x + 2f, bgRect.y + 2f, (bgRect.width - 4f) * Mathf.Clamp01(pct), bgRect.height - 4f);
        GUI.color = fillCol;
        GUI.DrawTexture(fillRect, Texture2D.whiteTexture);

        GUI.color = prevColor;
        GUIStyle st = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        st.normal.textColor = Color.white;
        GUI.Label(bgRect, text, st);
    }

    private void DrawPlantSeedButton(int index, PlantType type, string title, int cost, Color readyTint)
    {
        Rect rect = plantButtonRects[index];
        float cd = GetPlantCooldownRemaining(type);
        bool enoughMoons = currentMoons >= cost;
        bool isReady = cd <= 0.001f && enoughMoons;
        bool isSelected = isPlantSelected && selectedPlantType == type;

        GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        if (isSelected)
        {
            GUI.backgroundColor = new Color(0.25f, 1.0f, 0.35f);
        }
        else if (isReady)
        {
            GUI.backgroundColor = readyTint;
        }
        else
        {
            GUI.backgroundColor = new Color(0.45f, 0.45f, 0.45f);
        }

        string statusLine;
        if (cd > 0.001f)
        {
            statusLine = $"CD: {cd:F1}s";
        }
        else if (!enoughMoons)
        {
            statusLine = $"Need {cost} 🌙";
        }
        else if (isSelected)
        {
            statusLine = "SELECTED ✓";
        }
        else
        {
            statusLine = "READY";
        }

        string label = $"{title}\n{cost} 🌙 • {statusLine}";
        if (GUI.Button(rect, label, btnStyle))
        {
            TrySelectPlant(type);
        }
    }

    private void DrawZombieSpawnButton(Rect targetRect)
    {
        bool enoughBrains = currentBrains >= zombieCost;
        bool isReady = zombieCooldownTimer <= 0.001f && enoughBrains;

        GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        if (isZombieSelected)
        {
            GUI.backgroundColor = new Color(0.85f, 0.3f, 1.0f);
        }
        else if (isReady)
        {
            GUI.backgroundColor = new Color(0.68f, 0.42f, 0.88f);
        }
        else
        {
            GUI.backgroundColor = new Color(0.45f, 0.45f, 0.45f);
        }

        string statusLine;
        if (zombieCooldownTimer > 0.001f)
        {
            statusLine = $"CD: {zombieCooldownTimer:F1}s";
        }
        else if (!enoughBrains)
        {
            statusLine = $"Need {zombieCost} 🧠";
        }
        else if (isZombieSelected)
        {
            statusLine = "SELECTED ✓";
        }
        else
        {
            statusLine = "READY";
        }

        string label = $"Zombie [4]\n{zombieCost} 🧠 • {statusLine}";
        if (GUI.Button(targetRect, label, btnStyle))
        {
            TrySelectZombie();
        }
    }

    private void DrawGameOverModal()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        Color prevBg = GUI.backgroundColor;

        // Dark dimming overlay across screen
        GUI.backgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.78f);
        GUI.Box(new Rect(0f, 0f, sw, sh), GUIContent.none);

        Rect boxRect = new Rect((sw - 580f) * 0.5f, (sh - 280f) * 0.5f, 580f, 280f);

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        GUIStyle descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        if (matchResult == MatchResult.PlantsWin)
        {
            GUI.backgroundColor = new Color(0.06f, 0.22f, 0.10f, 0.96f);
            GUI.Box(boxRect, GUIContent.none);

            string title = (localRole == MultiplayerMenuManager.PlayerRole.Plant)
                ? "VICTORY! (PLANTS WIN)"
                : (localRole == MultiplayerMenuManager.PlayerRole.Zombie)
                    ? "DEFEAT! (PLANTS WIN)"
                    : "PLANTS WIN!";

            titleStyle.normal.textColor = new Color(0.35f, 1.0f, 0.45f);
            GUI.Label(new Rect(boxRect.x, boxRect.y + 22f, boxRect.width, 42f), title, titleStyle);

            descStyle.normal.textColor = new Color(0.9f, 0.98f, 0.9f);
            GUI.Label(new Rect(boxRect.x + 30f, boxRect.y + 75f, boxRect.width - 60f, 70f),
                "The 5-minute defense timer reached 00:00!\nThe Plants stood strong and successfully defended the garden wall against the zombie assault!",
                descStyle);

            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.35f);
            if (GUI.Button(new Rect(boxRect.x + (boxRect.width - 240f) * 0.5f, boxRect.y + 185f, 240f, 55f), "RESTART MATCH", btnStyle))
            {
                ResetMatch(notifyNetwork: true);
            }
        }
        else if (matchResult == MatchResult.ZombiesWin)
        {
            GUI.backgroundColor = new Color(0.26f, 0.06f, 0.12f, 0.96f);
            GUI.Box(boxRect, GUIContent.none);

            string title = (localRole == MultiplayerMenuManager.PlayerRole.Zombie)
                ? "VICTORY! (ZOMBIES WIN)"
                : (localRole == MultiplayerMenuManager.PlayerRole.Plant)
                    ? "DEFEAT! (ZOMBIES WIN)"
                    : "ZOMBIES WIN!";

            titleStyle.normal.textColor = new Color(1.0f, 0.35f, 0.35f);
            GUI.Label(new Rect(boxRect.x, boxRect.y + 22f, boxRect.width, 42f), title, titleStyle);

            descStyle.normal.textColor = new Color(1.0f, 0.9f, 0.9f);
            GUI.Label(new Rect(boxRect.x + 30f, boxRect.y + 75f, boxRect.width - 60f, 70f),
                "A Zombie crossed the defense line of the board!\nThe zombies breached your perimeter and ate your brains!",
                descStyle);

            GUI.backgroundColor = new Color(0.85f, 0.25f, 0.35f);
            if (GUI.Button(new Rect(boxRect.x + (boxRect.width - 240f) * 0.5f, boxRect.y + 185f, 240f, 55f), "RESTART MATCH", btnStyle))
            {
                ResetMatch(notifyNetwork: true);
            }
        }

        GUI.backgroundColor = prevBg;
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

        // Draw red Defense Line in gizmos
        Gizmos.color = Color.red;
        Gizmos.DrawLine(
            new Vector3(DefenseLineX, bottomEdge, z),
            new Vector3(DefenseLineX, bottomEdge + wallHeight, z)
        );
    }
}
