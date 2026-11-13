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
    public float zombieCooldown = 5.0f; // Normal Zombie [4]

    public int bucketheadCost = 100;
    public float bucketheadCooldown = 12.0f; // Buckethead [5] (5x Health)

    public int bunnyCost = 75;
    public float bunnyCooldown = 7.5f; // Bunny Speed Zombie [6]

    public float passiveBrainInterval = 10.0f;
    public int passiveBrainAmount = 25;
    private float zombieCooldownTimer = 0f;
    private readonly float[] zombieCooldownTimers = new float[3];
    private float passiveBrainTimer = 0f;

    [Header("Zombie Placement Restriction")]
    [Tooltip("Number of columns on the right edge where zombies are allowed to spawn. Set to 2 for the last 2 columns/lanes.")]
    public int zombieAllowedColumnsFromRight = 2;

    [Header("Current Selection")]
    public bool isPlantSelected = false;
    public PlantType selectedPlantType = PlantType.Moonflower;
    public bool isZombieSelected = false;
    public ZombieType selectedZombieType = ZombieType.Normal;

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
    public event Action<int, int, ZombieType> OnZombiePlacedLocal;

    // ===== Network sync events (moons, clash, 3D arena) =====
    public event Action<int, Vector3, float, int> OnSkyMoonSpawnedLocal;      // id, startPos, landingY, value
    public event Action<int, Vector3, int> OnFlowerMoonSpawnedLocal;           // id, flowerPos, value
    public event Action<System.Collections.Generic.List<int>> OnMoonsCollectedLocal;
    public event Action<int, int, PlantType, MultiplayerMenuManager.PlayerRole, ZombieType> OnClashStartedLocal; // plant row, col, plantType, senderRole, zombieType
    public event Action<Vector3, float, int> OnArenaPoseLocal;                 // position, yaw, stateFlags (bit1=BunnySlide, bit2=BucketParry, bit3=ParrySpeed)
    public event Action<bool> OnArenaResolvedLocal;                            // true = zombie won
    public event Action<float> OnArenaStunLocal;                               // duration in seconds (Durian immobilize)
    public event Action<string, Vector3, Vector3> OnArenaAbilityFxLocal;       // fxType, startPos, endPos
    public event Action<int> OnArenaSeedLocal;                                 // synced random seed for 3D arena layout
    public event Action<int, float> OnArenaAttackHitLocal;                     // damage, stunDuration
    public event Action<int, int> OnArenaParriedLocal;                         // blockedDamage, restoredHP

    private int nextMoonId = 1;
    public bool IsIn3DArena => isIn3DArena;
    /// <summary>Only the Plant player (or solo) spawns moons; the Zombie player receives them over the network.</summary>
    public bool IsMoonAuthority => GetEffectiveLocalRole() != MultiplayerMenuManager.PlayerRole.Zombie;

    // 3D arena remote player avatar & random layout seed
    private GameObject remoteArenaAvatar;
    private CapsuleCollider remoteBodyHitbox;
    private SphereCollider remoteHeadHitbox;
    private Transform remoteZombieVisualRoot;
    private GameObject remoteParryShieldObj;
    private bool remoteIsBunnySliding = false;
    private bool remoteIsBucketParrying = false;
    private bool remoteHasParrySpeedBuff = false;
    private bool remoteIsFeralLunging = false;

    private GameObject arenaObstaclesRoot;
    private Renderer arenaFloorRenderer;
    private int currentArenaLayoutSeed = 0;
    private string currentArenaPresetName = "Random";
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

    // Ability cooldown & active timers (Primary & Secondary abilities)
    private float arenaAbilityCooldownTimer = 0f; // Primary attack cooldown
    private float arenaPrimaryMaxCooldown = 1.0f;
    private float arenaSecondaryCooldownTimer = 0f; // Special ability cooldown
    private float arenaSecondaryMaxCooldown = 5.0f;

    private bool isChargingMoonBeam = false;
    private float moonBeamChargeTimer = 0f;
    public float moonBeamChargeDuration = 1.6f;
    private GameObject localMoonChargeOrb;

    private bool isChargingRoseThorn = false;
    private float roseThornChargeTimer = 0f;
    public float roseThornChargeDuration = 0.25f;

    private bool isDurianRollingLocal = false;
    private float durianRollTimer = 0f;
    public float durianRollDuration = 2.6f;
    private GameObject localDurianRollVisual;

    // Buckethead Parry Ability (1.0s active parry window, 5.0s cooldown, 3.0s speed boost proportional to damage)
    public float bucketParryDuration = 1.0f;
    public float bucketParryCooldown = 5.0f;
    public float bucketParrySpeedDuration = 3.0f;
    private bool isBucketParryingLocal = false;
    private float bucketParryTimer = 0f;
    private GameObject localBucketParryVisual;

    // Bunny Zombie Slide Ability (lowers hitbox, faster speed while sliding, 3D sliding model pose)
    public float bunnySlideDuration = 0.90f;
    public float bunnySlideCooldown = 3.0f;
    private bool isBunnySlidingLocal = false;
    private float bunnySlideTimer = 0f;
    private GameObject localBunnySlideVisual;

    // Feral Lunge Dive Ability (fast forward dive pounce with diving POV arms & diving 3D pose toward the Plant player)
    public float feralLungeDuration = 0.65f;
    public float feralLungeCooldown = 3.5f;
    private bool isFeralLungingLocal = false;
    private float feralLungeTimer = 0f;
    private bool feralLungeHitDealt = false;
    private GameObject localFeralLungeVisual;

    // Solo sparring bot timer (only active when testing solo without a second networked player)
    private float soloBotActionTimer = 2.2f;
    private float soloBotStrafePhase = 0f;

    // Arena result (host presses N / M or automatic when HP reaches 0)
    private bool arenaResolving = false;
    private bool arenaZombieWon = false;
    private float arenaResolveTimer = 0f;
    public float arenaResultPauseDuration = 2.5f;

    private readonly float[] cooldownTimers = new float[3];
    private PlaceholderPlant[,] placedPlants;
    private Renderer[,] tileRenderers;
    private GameObject tilesContainer;
    private readonly GameObject[] previewGhosts = new GameObject[3];
    private readonly GameObject[] zombiePreviewGhosts = new GameObject[3];
    private GameObject zombiePreviewGhost;

    private int hoveredRow = -1;
    private int hoveredCol = -1;
    private int prevHoveredRow = -1;
    private int prevHoveredCol = -1;
    private bool prevIsZombieSelected = false;
    private bool prevHoveredOccupied = false;
    private Material sharedBaseMaterial;
    private readonly System.Collections.Generic.Dictionary<Color32, Material> cachedMaterials = new System.Collections.Generic.Dictionary<Color32, Material>();
    private MultiplayerMenuManager cachedMenuManager;
    private PlayerController cachedPlayerController;
    private float clashRetrySendTimer = 0f;
    private bool localInitiatedCurrentClash = false;

    // Top-left UI layout rects (Moon Bank + 3 Plant Buttons + Brain Bank + 3 Zombie Buttons)
    private readonly Rect moonBankRect = new Rect(12f, 12f, 106f, 66f);
    private readonly Rect[] plantButtonRects = new Rect[3]
    {
        new Rect(122f, 12f, 124f, 66f),
        new Rect(250f, 12f, 124f, 66f),
        new Rect(378f, 12f, 124f, 66f)
    };
    private readonly Rect brainBankRect = new Rect(12f, 82f, 106f, 66f);
    private readonly Rect[] zombieButtonRects = new Rect[3]
    {
        new Rect(122f, 82f, 124f, 66f),
        new Rect(250f, 82f, 128f, 66f),
        new Rect(382f, 82f, 124f, 66f)
    };
    private readonly Rect zombieButtonRect = new Rect(122f, 82f, 124f, 66f);

    public float TileWidth => columns > 0 ? wallWidth / columns : 2.0f;
    public float TileHeight => rows > 0 ? wallHeight / rows : 1.8f;

    private void Awake()
    {
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 1;
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
        Vector3 targetScale = new Vector3(wallWidth, wallHeight, wallThickness);
        if (transform.localScale != targetScale)
        {
            transform.localScale = targetScale;
        }
        if (transform.rotation != Quaternion.identity)
        {
            transform.rotation = Quaternion.identity;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null && enforce16By9Viewport && !isIn3DArena && !isZoomingIntoClash)
        {
            if (!targetCamera.orthographic || Mathf.Abs(targetCamera.orthographicSize - wallHeight * 0.5f) > 0.001f)
            {
                targetCamera.orthographic = true;
                targetCamera.orthographicSize = wallHeight * 0.5f;
            }
            Apply16By9Viewport(targetCamera);
        }
    }

    [ContextMenu("Align Camera & Wall (16:9)")]
    public void SyncWallScaleAndCamera()
    {
        SyncWallScale();

        if (targetCamera != null && !isIn3DArena && !isZoomingIntoClash)
        {
            if (!targetCamera.orthographic || Mathf.Abs(targetCamera.orthographicSize - wallHeight * 0.5f) > 0.001f)
            {
                targetCamera.orthographic = true;
                targetCamera.orthographicSize = wallHeight * 0.5f;
            }
            Vector3 idealPos = GetIdealCameraPosition();
            if (Vector3.Distance(targetCamera.transform.position, idealPos) > 0.001f)
            {
                targetCamera.transform.position = idealPos;
            }
            Quaternion idealRot = Quaternion.LookRotation(transform.forward, Vector3.up);
            if (targetCamera.transform.rotation != idealRot)
            {
                targetCamera.transform.rotation = idealRot;
            }

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

        if (cam.rect != targetRect)
        {
            cam.rect = targetRect;
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
                rend.sharedMaterial = CreateColoredMaterial(GetBaseTileColor(r, c));
                tileRenderers[r, c] = rend;
            }
        }

        prevHoveredRow = -1;
        prevHoveredCol = -1;

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
        defenseLineObj.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(1.0f, 0.20f, 0.25f, 0.98f));

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
        beam.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(color);
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

        for (int i = 0; i < 3; i++)
        {
            if (zombiePreviewGhosts[i] != null)
            {
                Destroy(zombiePreviewGhosts[i]);
            }
            zombiePreviewGhosts[i] = CreateZombieVisual((ZombieType)i, $"ZombiePreviewGhost_{(ZombieType)i}", isGhost: true);
            if (tilesContainer != null)
            {
                zombiePreviewGhosts[i].transform.SetParent(tilesContainer.transform, true);
            }
            zombiePreviewGhosts[i].SetActive(false);
        }

        zombiePreviewGhost = zombiePreviewGhosts[0];
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

                MultiplayerMenuManager menu = GetMenuManager();
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
                    else if (Input.GetKeyDown(KeyCode.R))
                    {
                        currentArenaLayoutSeed = UnityEngine.Random.Range(1000, 9999999);
                        RebuildRandomArenaLayout(currentArenaLayoutSeed);
                        OnArenaSeedLocal?.Invoke(currentArenaLayoutSeed);
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

        // Tick down PvZ-style plant & zombie seed packet cooldowns
        for (int i = 0; i < cooldownTimers.Length; i++)
        {
            if (cooldownTimers[i] > 0f)
            {
                cooldownTimers[i] = Mathf.Max(0f, cooldownTimers[i] - Time.deltaTime);
            }
        }
        for (int i = 0; i < zombieCooldownTimers.Length; i++)
        {
            if (zombieCooldownTimers[i] > 0f)
            {
                zombieCooldownTimers[i] = Mathf.Max(0f, zombieCooldownTimers[i] - Time.deltaTime);
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

        // Hotkeys: 1, 2, 3 for Plants; 4, 5, 6 for Zombies (Normal, Buckethead, Bunny)
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
                TrySelectZombie(ZombieType.Normal);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
            {
                TrySelectZombie(ZombieType.Buckethead);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
            {
                TrySelectZombie(ZombieType.Bunny);
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
    private ZombieType clashingZombieType = ZombieType.Normal;

    private MultiplayerMenuManager GetMenuManager()
    {
        if (cachedMenuManager == null)
        {
            cachedMenuManager = FindFirstObjectByType<MultiplayerMenuManager>();
        }
        return cachedMenuManager;
    }

    public MultiplayerMenuManager.PlayerRole EffectiveRole => GetEffectiveLocalRole();

    public MultiplayerMenuManager.PlayerRole GetEffectiveLocalRole()
    {
        if (localRole != MultiplayerMenuManager.PlayerRole.None)
        {
            return localRole;
        }

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
        ZombieType zType = (zombie != null) ? zombie.zombieType : clashingZombieType;
        int r = (plant != null) ? plant.row : (zombie != null ? zombie.row : 0);
        int c = (plant != null) ? plant.column : 0;

        clashingPlantRow = r;
        clashingPlantCol = c;
        clashingPlantType = pType;
        clashingZombieType = zType;

        if (notifyNetwork)
        {
            localInitiatedCurrentClash = true;
            clashRetrySendTimer = 0.22f;
            currentArenaLayoutSeed = UnityEngine.Random.Range(1000, 9999999);
            OnClashStartedLocal?.Invoke(r, c, pType, GetEffectiveLocalRole(), zType);
            OnArenaSeedLocal?.Invoke(currentArenaLayoutSeed);
        }
        else
        {
            localInitiatedCurrentClash = false;
            if (currentArenaLayoutSeed == 0)
            {
                currentArenaLayoutSeed = UnityEngine.Random.Range(1000, 9999999);
            }
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
        TriggerRemoteClash(row, col, PlantType.Rose, MultiplayerMenuManager.PlayerRole.None, ZombieType.Normal);
    }

    public void TriggerRemoteClash(int row, int col, PlantType plantType, MultiplayerMenuManager.PlayerRole remoteInitiatorRole = MultiplayerMenuManager.PlayerRole.None, ZombieType zombieType = ZombieType.Normal)
    {
        gameplayActive = true;
        clashingPlantRow = row;
        clashingPlantCol = col;
        clashingPlantType = plantType;
        clashingZombieType = zombieType;

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

        if (best != null)
        {
            clashingZombieType = best.zombieType;
        }
        else
        {
            clashingZombieType = zombieType;
        }

        TriggerZombiePlantClash(best, plant, plantType, notifyNetwork: false);
    }

    private void UpdateClashZoomAnimation()
    {
        clashZoomTimer += Time.deltaTime;
        float t = Mathf.Clamp01(clashZoomTimer / Mathf.Max(0.1f, clashZoomDuration));
        float smoothT = Mathf.SmoothStep(0f, 1f, t);

        if (localInitiatedCurrentClash && !hasRemoteArenaPose)
        {
            clashRetrySendTimer -= Time.deltaTime;
            if (clashRetrySendTimer <= 0f)
            {
                clashRetrySendTimer = 0.25f;
                OnClashStartedLocal?.Invoke(clashingPlantRow, clashingPlantCol, clashingPlantType, GetEffectiveLocalRole(), clashingZombieType);
                OnArenaSeedLocal?.Invoke(currentArenaLayoutSeed);
            }
        }

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
        ZombieType activeZombie = (clashingZombie != null) ? clashingZombie.zombieType : clashingZombieType;
        clashingPlantType = activePlant;
        clashingZombieType = activeZombie;

        // Initialize 3D Arena HP based on specific PlantType & ZombieType (Buckethead = 400 HP, Bunny = 90 HP, Normal = 150 HP)
        localArenaMaxHP = isZombie ? GetZombieMaxArenaHP(activeZombie) : GetPlantMaxArenaHP(activePlant);
        localArenaCurrentHP = localArenaMaxHP;
        remoteArenaMaxHP = isZombie ? GetPlantMaxArenaHP(activePlant) : GetZombieMaxArenaHP(activeZombie);
        remoteArenaCurrentHP = remoteArenaMaxHP;

        arenaHitsLanded = 0;
        arenaHitIndicatorTimer = 0f;
        remoteHitFlashTimer = 0f;
        remoteImmobilizedTimer = 0f;
        arenaAbilityCooldownTimer = 0f;
        arenaSecondaryCooldownTimer = 0f;
        isChargingMoonBeam = false;
        moonBeamChargeTimer = 0f;
        isChargingRoseThorn = false;
        roseThornChargeTimer = 0f;
        isDurianRollingLocal = false;
        durianRollTimer = 0f;
        isBucketParryingLocal = false;
        bucketParryTimer = 0f;
        isBunnySlidingLocal = false;
        bunnySlideTimer = 0f;
        isFeralLungingLocal = false;
        feralLungeTimer = 0f;
        feralLungeHitDealt = false;
        remoteIsBunnySliding = false;
        remoteIsBucketParrying = false;
        remoteHasParrySpeedBuff = false;
        remoteIsFeralLunging = false;
        soloBotActionTimer = 2.2f;

        // Ensure 3D Arena environment exists with the remote opponent avatar (Plant or Zombie)
        BuildOrShow3DArenaEnvironment();

        Vector3 arenaSpawnPos = isZombie ? ArenaZombieSpawn : ArenaPlantSpawn;
        Quaternion arenaSpawnRot = Quaternion.Euler(0f, isZombie ? 180f : 0f, 0f);

        PlayerController chosenPlayer = GetPrimaryPlayerController();
        if (chosenPlayer != null && targetCamera != null)
        {
            chosenPlayer.inputFrozen = false;
            chosenPlayer.characterSpeedMultiplier = GetArenaCharacterSpeedMultiplier(isZombie, activePlant, activeZombie);
            chosenPlayer.characterBaseFovBonus = GetArenaCharacterFovBonus(isZombie, activeZombie);
            chosenPlayer.EnterFirstPersonArena(targetCamera, arenaSpawnPos, arenaSpawnRot, currentArenaOpponentName);
        }
    }

    private int GetPlantMaxArenaHP(PlantType type)
    {
        return (type == PlantType.Durian) ? 400 : 125;
    }

    private int GetZombieMaxArenaHP(ZombieType type)
    {
        switch (type)
        {
            case ZombieType.Buckethead: return 400; // Buckethead has 400 combat HP
            case ZombieType.Bunny:      return 90;  // Bunny speed zombie has lower combat HP
            case ZombieType.Normal:
            default:                    return 150;
        }
    }

    private float GetArenaCharacterSpeedMultiplier(bool isZombie, PlantType pType, ZombieType zType)
    {
        if (isZombie)
        {
            switch (zType)
            {
                case ZombieType.Buckethead: return 0.86f; // A tiny bit slower
                case ZombieType.Bunny:      return 1.44f; // Noticeably faster base movement speed
                case ZombieType.Normal:
                default:                    return 1.15f; // Faster zombie base speed for chasing plants
            }
        }
        else
        {
            return (pType == PlantType.Durian) ? 0.92f : 1.0f;
        }
    }

    private float GetArenaCharacterFovBonus(bool isZombie, ZombieType zType)
    {
        if (!isZombie) return 0f;
        switch (zType)
        {
            case ZombieType.Bunny:      return 14f; // Higher FOV (89°) to give the illusion of lunging for the Plant player
            case ZombieType.Normal:     return 9f;  // Higher FOV (84°) for aggressive zombie chase feel
            case ZombieType.Buckethead: return 4f;
            default:                    return 8f;
        }
    }

    private static readonly Vector3 ArenaPlantSpawn = new Vector3(0f, 1.5f, -38.0f);
    private static readonly Vector3 ArenaZombieSpawn = new Vector3(0f, 1.5f, -14.0f);

    /// <summary>Host decides the 1v1 result (N = zombie won, M = plant won) or triggered automatically when HP hits 0.</summary>
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
        // If we initiated the clash and the remote player hasn't sent a pose yet, resend CLASH & ASEED periodically so they are guaranteed to enter the 3D scene
        if (localInitiatedCurrentClash && !hasRemoteArenaPose)
        {
            clashRetrySendTimer -= Time.deltaTime;
            if (clashRetrySendTimer <= 0f)
            {
                clashRetrySendTimer = 0.35f;
                OnClashStartedLocal?.Invoke(clashingPlantRow, clashingPlantCol, clashingPlantType, GetEffectiveLocalRole(), clashingZombieType);
                OnArenaSeedLocal?.Invoke(currentArenaLayoutSeed);
            }
        }

        // Send our pose + active state flags ~20x per second
        PlayerController pc = GetPrimaryPlayerController();
        arenaPoseSendTimer -= Time.deltaTime;
        if (pc != null && arenaPoseSendTimer <= 0f)
        {
            arenaPoseSendTimer = 0.05f;
            int stateFlags = 0;
            if (isBunnySlidingLocal) stateFlags |= 1;
            if (isBucketParryingLocal) stateFlags |= 2;
            if (pc.parrySpeedBonusTimer > 0f) stateFlags |= 4;
            if (isFeralLungingLocal) stateFlags |= 8;
            OnArenaPoseLocal?.Invoke(pc.transform.position, pc.transform.eulerAngles.y, stateFlags);
        }

        // Smoothly move the remote avatar towards the latest received pose (unless immobilized by Durian)
        if (remoteArenaAvatar != null && hasRemoteArenaPose && remoteImmobilizedTimer <= 0f)
        {
            Transform t = remoteArenaAvatar.transform;
            t.position = Vector3.Lerp(t.position, remoteArenaTargetPos, Time.deltaTime * 15f);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, remoteArenaTargetYaw, 0f), Time.deltaTime * 15f);
        }

        UpdateRemoteAvatarVisualsAndHitbox();
    }

    private void UpdateRemoteAvatarVisualsAndHitbox()
    {
        if (remoteArenaAvatar == null) return;

        // 1. Update Remote Bunny Zombie Sliding Model & Lowered Hitbox (or Feral Lunge Diving Pose)!
        if (remoteBodyHitbox != null && remoteHeadHitbox != null)
        {
            if (remoteIsBunnySliding)
            {
                remoteBodyHitbox.height = 0.95f;
                remoteBodyHitbox.center = new Vector3(0f, -0.52f, 0f);
                remoteHeadHitbox.center = new Vector3(0f, -0.22f, 0.22f);
            }
            else if (remoteIsFeralLunging)
            {
                remoteBodyHitbox.height = 1.45f;
                remoteBodyHitbox.center = new Vector3(0f, -0.10f, 0.22f);
                remoteHeadHitbox.center = new Vector3(0f, 0.20f, 0.45f);
            }
            else
            {
                remoteBodyHitbox.height = 2.0f;
                remoteBodyHitbox.center = Vector3.zero;
                remoteHeadHitbox.center = new Vector3(0f, 0.35f, 0f);
            }
        }

        if (remoteZombieVisualRoot != null)
        {
            Vector3 targetLocalPos = remoteIsBunnySliding
                ? new Vector3(0f, -0.52f, 0.18f)
                : (remoteIsFeralLunging ? new Vector3(0f, 0.16f, 0.42f) : Vector3.zero);

            Quaternion targetLocalRot = remoteIsBunnySliding
                ? Quaternion.Euler(-58f, 0f, 0f)
                : (remoteIsFeralLunging ? Quaternion.Euler(64f, 0f, 0f) : Quaternion.identity);

            remoteZombieVisualRoot.localPosition = Vector3.Lerp(remoteZombieVisualRoot.localPosition, targetLocalPos, Time.deltaTime * 18f);
            remoteZombieVisualRoot.localRotation = Quaternion.Slerp(remoteZombieVisualRoot.localRotation, targetLocalRot, Time.deltaTime * 18f);

            // Pose legs & arms during slide or feral dive
            Transform leftLeg = remoteZombieVisualRoot.Find("LeftLeg");
            Transform rightLeg = remoteZombieVisualRoot.Find("RightLeg");
            if (leftLeg != null && rightLeg != null)
            {
                Quaternion legRot = remoteIsBunnySliding
                    ? Quaternion.Euler(-32f, 0f, 0f)
                    : (remoteIsFeralLunging ? Quaternion.Euler(28f, 0f, 0f) : Quaternion.identity);
                leftLeg.localRotation = Quaternion.Slerp(leftLeg.localRotation, legRot, Time.deltaTime * 18f);
                rightLeg.localRotation = Quaternion.Slerp(rightLeg.localRotation, legRot, Time.deltaTime * 18f);
            }

            Transform leftArm = remoteZombieVisualRoot.Find("LeftArm");
            Transform rightArm = remoteZombieVisualRoot.Find("RightArm");
            if (leftArm != null && rightArm != null)
            {
                Quaternion leftArmRot = remoteIsFeralLunging ? Quaternion.Euler(35f, 18f, -15f) : Quaternion.Euler(82f, 0f, 0f);
                Quaternion rightArmRot = remoteIsFeralLunging ? Quaternion.Euler(35f, -18f, 15f) : Quaternion.Euler(85f, 0f, 0f);
                leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation, leftArmRot, Time.deltaTime * 18f);
                rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation, rightArmRot, Time.deltaTime * 18f);
            }
        }

        // 2. Update Remote Buckethead Parry Shield Visual!
        if (remoteParryShieldObj != null)
        {
            if (remoteParryShieldObj.activeSelf != remoteIsBucketParrying)
            {
                remoteParryShieldObj.SetActive(remoteIsBucketParrying);
            }
            if (remoteIsBucketParrying)
            {
                float s = 1.0f + Mathf.Sin(Time.time * 22f) * 0.06f;
                remoteParryShieldObj.transform.localScale = new Vector3(1.35f * s, 1.85f * s, 0.35f);
            }
        }
    }

    public void SetRemoteArenaPose(Vector3 pos, float yaw, int stateFlags = 0)
    {
        // If we started receiving 3D arena poses from the remote player before CLASH arrived, immediately enter the 3D arena!
        if (!isIn3DArena && !isZoomingIntoClash)
        {
            gameplayActive = true;
            is2DWorldPaused = true;
            Enter3D1v1Arena();
        }

        remoteArenaTargetPos = pos;
        remoteArenaTargetYaw = yaw;
        remoteIsBunnySliding = (stateFlags & 1) != 0;
        remoteIsBucketParrying = (stateFlags & 2) != 0;
        remoteHasParrySpeedBuff = (stateFlags & 4) != 0;
        remoteIsFeralLunging = (stateFlags & 8) != 0;

        if (!hasRemoteArenaPose && remoteArenaAvatar != null)
        {
            remoteArenaAvatar.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }
        hasRemoteArenaPose = true;
    }

    public void ApplyLocalArenaImmobilize(float duration)
    {
        // If Buckethead is actively parrying, block the stun and trigger Parry Speed Boost!
        if (isBucketParryingLocal)
        {
            TriggerLocalBucketParrySuccess(50);
            return;
        }

        PlayerController pc = GetPrimaryPlayerController();
        if (pc != null)
        {
            pc.Immobilize(duration);
            CancelAllActiveArenaAbilities(pc);
        }
    }

    public void ApplyRemoteArenaDamage(int damage, float stunDuration)
    {
        if (!isIn3DArena || arenaResolving) return;

        // If Buckethead is inside his 1.0-second Parry window, parry the attack & convert its damage into movement speed!
        if (isBucketParryingLocal)
        {
            TriggerLocalBucketParrySuccess(damage);
            OnArenaParriedLocal?.Invoke(damage, localArenaCurrentHP);
            return;
        }

        if (stunDuration > 0f)
        {
            ApplyLocalArenaImmobilize(stunDuration);
        }

        localArenaCurrentHP = Mathf.Max(0, localArenaCurrentHP - damage);
        arenaHitIndicatorTimer = 0.75f;
        arenaLastHitLabel = $"TOOK {damage} DMG!";

        if (localArenaCurrentHP <= 0)
        {
            bool localIsZombie = GetEffectiveLocalRole() == MultiplayerMenuManager.PlayerRole.Zombie;
            ResolveArena(zombieWon: !localIsZombie, notifyNetwork: true);
        }
    }

    public void ApplyRemoteParryNotification(int blockedDamage, int restoredRemoteHP)
    {
        if (!isIn3DArena) return;
        remoteArenaCurrentHP = Mathf.Clamp(restoredRemoteHP, 1, remoteArenaMaxHP);
        UpdateRemoteWorldHPBarScale();

        float bonusPct = Mathf.Clamp(blockedDamage * 1.8f, 25f, 140f);
        arenaHitIndicatorTimer = 1.25f;
        arenaLastHitLabel = $"🛡 PARRIED BY BUCKETHEAD! (+{bonusPct:F0}% ZOMBIE SPEED)";
    }

    private void TriggerLocalBucketParrySuccess(int incomingDamage)
    {
        isBucketParryingLocal = false;
        bucketParryTimer = 0f;
        if (localBucketParryVisual != null)
        {
            Destroy(localBucketParryVisual);
            localBucketParryVisual = null;
        }

        // Stronger attack = more movement speed for 3 seconds!
        float bonusPct = Mathf.Clamp(incomingDamage * 1.8f, 25f, 140f);
        float speedMul = 1.0f + (bonusPct / 100f);

        PlayerController pc = GetPrimaryPlayerController();
        if (pc != null)
        {
            pc.ApplyParrySpeedBoost(speedMul, bucketParrySpeedDuration);
            OnArenaAbilityFxLocal?.Invoke("PARRY", pc.transform.position, pc.transform.forward);
        }

        arenaHitIndicatorTimer = 1.5f;
        arenaLastHitLabel = $"⚡ PARRIED {incomingDamage} DMG! +{bonusPct:F0}% SPEED FOR 3.0s! ⚡";
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

        isBucketParryingLocal = false;
        bucketParryTimer = 0f;
        if (localBucketParryVisual != null)
        {
            Destroy(localBucketParryVisual);
            localBucketParryVisual = null;
        }

        StopBunnySlide(pc);
        StopFeralLunge(pc);
    }

    private void StopBunnySlide(PlayerController pc)
    {
        isBunnySlidingLocal = false;
        bunnySlideTimer = 0f;
        if (pc != null) pc.isBunnySliding = false;
        if (localBunnySlideVisual != null)
        {
            Destroy(localBunnySlideVisual);
            localBunnySlideVisual = null;
        }
    }

    private void StopFeralLunge(PlayerController pc)
    {
        isFeralLungingLocal = false;
        feralLungeTimer = 0f;
        if (pc != null) pc.isFeralLunging = false;
        if (localFeralLungeVisual != null)
        {
            Destroy(localFeralLungeVisual);
            localFeralLungeVisual = null;
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
                Color col = (remoteHitFlashTimer > 0f)
                    ? new Color(1.0f, 0.95f, 0.35f)
                    : remoteHpBarBaseColor;
                remoteHpBarFillRenderer.sharedMaterial = CreateColoredMaterial(col);
            }
        }

        if (arenaAbilityCooldownTimer > 0f)
        {
            arenaAbilityCooldownTimer = Mathf.Max(0f, arenaAbilityCooldownTimer - Time.deltaTime);
        }
        if (arenaSecondaryCooldownTimer > 0f)
        {
            arenaSecondaryCooldownTimer = Mathf.Max(0f, arenaSecondaryCooldownTimer - Time.deltaTime);
        }

        MultiplayerMenuManager.PlayerRole myRole = GetEffectiveLocalRole();
        bool isZombie = myRole == MultiplayerMenuManager.PlayerRole.Zombie;
        PlantType activePlant = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;
        ZombieType activeZombie = (clashingZombie != null) ? clashingZombie.zombieType : clashingZombieType;

        // Solo sparring bot when testing without a remote player connected
        if (!hasRemoteArenaPose && remoteArenaAvatar != null)
        {
            UpdateSoloArenaSparringBot(pc, isZombie, activePlant, activeZombie);
        }

        // If immobilized by Durian roll, cancel active charges and block all attacks & abilities!
        if (pc.IsImmobilized)
        {
            CancelAllActiveArenaAbilities(pc);
            return;
        }

        // 1. Tick active Buckethead 1.0s Parry Window
        if (isBucketParryingLocal)
        {
            bucketParryTimer -= Time.deltaTime;
            if (localBucketParryVisual == null && arena3DRoot != null)
            {
                localBucketParryVisual = CreateLocalParryShieldVisual();
            }
            if (localBucketParryVisual != null && targetCamera != null)
            {
                localBucketParryVisual.transform.position = targetCamera.transform.position + targetCamera.transform.forward * 0.72f + new Vector3(0f, -0.08f, 0f);
                localBucketParryVisual.transform.rotation = targetCamera.transform.rotation;
            }

            if (bucketParryTimer <= 0f)
            {
                isBucketParryingLocal = false;
                if (localBucketParryVisual != null)
                {
                    Destroy(localBucketParryVisual);
                    localBucketParryVisual = null;
                }
            }
        }

        // 2. Tick active Bunny Zombie Slide (lowers hitbox, faster speed, sliding model visual)
        if (isBunnySlidingLocal)
        {
            bunnySlideTimer -= Time.deltaTime;
            pc.isBunnySliding = true;

            if (localBunnySlideVisual == null && arena3DRoot != null)
            {
                localBunnySlideVisual = CreateLocalBunnySlideLegsVisual();
            }
            if (localBunnySlideVisual != null)
            {
                localBunnySlideVisual.transform.position = pc.transform.position + new Vector3(0f, -0.78f, 0f) + pc.transform.forward * 0.65f;
                localBunnySlideVisual.transform.rotation = Quaternion.Euler(0f, pc.transform.eulerAngles.y, 0f);
            }

            if (bunnySlideTimer <= 0f)
            {
                StopBunnySlide(pc);
            }
        }

        // 2b. Tick active Feral Charge / Lunge Dive (fast forward dive pounce with diving POV hands & 3D diving pose)
        if (isFeralLungingLocal)
        {
            feralLungeTimer -= Time.deltaTime;
            pc.isFeralLunging = true;

            if (localFeralLungeVisual == null && arena3DRoot != null)
            {
                localFeralLungeVisual = CreateLocalFeralLungePovVisual(activeZombie);
            }
            if (localFeralLungeVisual != null && targetCamera != null)
            {
                float diveBob = Mathf.Sin((1f - (feralLungeTimer / Mathf.Max(0.01f, feralLungeDuration))) * Mathf.PI) * 0.14f;
                localFeralLungeVisual.transform.position = targetCamera.transform.position
                    + targetCamera.transform.forward * (0.64f + diveBob)
                    - targetCamera.transform.up * 0.24f;
                localFeralLungeVisual.transform.rotation = targetCamera.transform.rotation * Quaternion.Euler(14f, 0f, 0f);
            }

            // Check if our Feral Lunge dive connects with the Plant opponent!
            if (!feralLungeHitDealt && remoteArenaAvatar != null)
            {
                Vector3 a = new Vector3(pc.transform.position.x, 0f, pc.transform.position.z);
                Vector3 b = new Vector3(remoteArenaAvatar.transform.position.x, 0f, remoteArenaAvatar.transform.position.z);
                float horizDist = Vector3.Distance(a, b);
                float vertDist = Mathf.Abs(pc.transform.position.y - remoteArenaAvatar.transform.position.y);

                if (horizDist <= 2.35f && vertDist <= 2.2f)
                {
                    feralLungeHitDealt = true;
                    Vector3 slashCenter = targetCamera != null
                        ? targetCamera.transform.position + targetCamera.transform.forward * 1.15f
                        : pc.transform.position + pc.transform.forward * 1.15f;
                    Quaternion slashRot = targetCamera != null ? targetCamera.transform.rotation : pc.transform.rotation;
                    Spawn3DSlashEffect(slashCenter, slashRot);
                    OnArenaAbilityFxLocal?.Invoke("SLASH", slashCenter, pc.transform.forward);
                    DealDamageToOpponent(38, "FERAL DIVE LUNGE HIT (38 DMG)!", 0f);
                    StopFeralLunge(pc);
                    return;
                }
            }

            if (feralLungeTimer <= 0f)
            {
                StopFeralLunge(pc);
            }
        }

        // 3. Tick active Moonflower Charged Moon Beam
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
                localMoonChargeOrb.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.45f, 0.95f, 1.0f));
            }

            if (localMoonChargeOrb != null && targetCamera != null)
            {
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

        // 4. Tick active Rose Hitscan Thorn Charge
        if (isChargingRoseThorn)
        {
            roseThornChargeTimer -= Time.deltaTime;
            if (roseThornChargeTimer <= 0f)
            {
                isChargingRoseThorn = false;
                FireRoseHitscanThorn(pc, isHeavyVolley: false);
            }
            return;
        }

        // 5. Tick active Durian Roll Attack
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
                    StopDurianRoll(pc);
                    arenaSecondaryCooldownTimer = 6.0f;
                    arenaSecondaryMaxCooldown = 6.0f;

                    if (remoteIsBucketParrying)
                    {
                        RegisterArenaParriedByOpponent(50);
                    }
                    else
                    {
                        remoteImmobilizedTimer = 2.0f;
                        OnArenaStunLocal?.Invoke(2.0f);
                        DealDamageToOpponent(50, "DURIAN ROLL SLAM! (50 DMG + 2s STUN)", 2.0f);
                    }
                    return;
                }
            }

            if (durianRollTimer <= 0f)
            {
                StopDurianRoll(pc);
                arenaSecondaryCooldownTimer = 6.0f;
                arenaSecondaryMaxCooldown = 6.0f;
            }
            return;
        }

        // ==================== INPUT TRIGGERS FOR PRIMARY & SECONDARY ABILITIES ====================
        bool leftClick = Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked;
        bool rightClick = Input.GetMouseButtonDown(1) && Cursor.lockState == CursorLockMode.Locked;
        bool keyE = Input.GetKeyDown(KeyCode.E);
        bool keyQ = Input.GetKeyDown(KeyCode.Q);
        bool keyCtrl = Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C);

        if (isZombie)
        {
            if (activeZombie == ZombieType.Buckethead)
            {
                // Buckethead:
                // - Right Click / E / Q: Bucket Parry (1.0s active parry window, 5.0s cooldown)
                // - Left Click: Heavy Bucket Melee Slash (30 dmg, 1.0s cooldown)
                if ((rightClick || keyE || keyQ) && arenaSecondaryCooldownTimer <= 0f && !isBucketParryingLocal)
                {
                    StartBucketheadParry(pc);
                }
                else if (leftClick && arenaAbilityCooldownTimer <= 0f)
                {
                    PerformZombieMeleeSlash(pc, damage: 30, cooldown: 1.0f, "BUCKETHEAD SLAM (30 DMG)!");
                }
            }
            else if (activeZombie == ZombieType.Bunny)
            {
                // Bunny Zombie:
                // - Left Click (or Q / LeftCtrl): Bunny Slide (lowers hitbox + speed boost + sliding model!)
                // - Right Click / E (or Left Click if already sliding or slide on cooldown): Bunny Claw Slash (22 dmg, 0.65s cooldown)
                // - F / Middle Click (or Right Click while sliding): Feral Dive Lunge!
                bool keyF = Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(2);
                if (keyF && arenaAbilityCooldownTimer <= 0f && !isFeralLungingLocal)
                {
                    StartFeralLunge(pc);
                }
                else if ((keyQ || keyCtrl) && arenaSecondaryCooldownTimer <= 0f && !isBunnySlidingLocal)
                {
                    StartBunnySlide(pc);
                }
                else if (rightClick || keyE)
                {
                    if (isBunnySlidingLocal && !isFeralLungingLocal && arenaAbilityCooldownTimer <= 0f)
                    {
                        StopBunnySlide(pc);
                        StartFeralLunge(pc);
                    }
                    else if (arenaAbilityCooldownTimer <= 0f)
                    {
                        PerformZombieMeleeSlash(pc, damage: 22, cooldown: 0.65f, "BUNNY CLAW SLASH (22 DMG)!");
                    }
                }
                else if (leftClick)
                {
                    if (!isBunnySlidingLocal && arenaSecondaryCooldownTimer <= 0f)
                    {
                        StartBunnySlide(pc);
                    }
                    else if (arenaAbilityCooldownTimer <= 0f)
                    {
                        PerformZombieMeleeSlash(pc, damage: 22, cooldown: 0.65f, "BUNNY CLAW SLASH (22 DMG)!");
                    }
                }
            }
            else
            {
                // Normal Zombie:
                // - Left Click / E: Claw Slash (25 dmg, 0.85s cooldown)
                // - Right Click / Q: Feral Lunge Dive (38 dmg + fast forward dive with diving POV & diving model, 3.5s cooldown)
                if ((rightClick || keyQ) && arenaSecondaryCooldownTimer <= 0f && !isFeralLungingLocal)
                {
                    StartFeralLunge(pc);
                }
                else if ((leftClick || keyE) && arenaAbilityCooldownTimer <= 0f)
                {
                    PerformZombieMeleeSlash(pc, damage: 25, cooldown: 0.85f, "ZOMBIE CLAW SLASH (25 DMG)!");
                }
            }
        }
        else
        {
            // Plant Player Abilities & Cooldowns
            if (activePlant == PlantType.Moonflower)
            {
                // Right Click / E: Charged Moon Laser Beam (65 Dmg, 6.0s Cooldown)
                // Left Click: Quick Solar Bolt (18 Dmg, 0.8s Cooldown)
                if ((rightClick || keyE) && arenaSecondaryCooldownTimer <= 0f)
                {
                    isChargingMoonBeam = true;
                    moonBeamChargeTimer = moonBeamChargeDuration;
                }
                else if (leftClick && arenaAbilityCooldownTimer <= 0f)
                {
                    FireMoonflowerSolarBolt(pc);
                }
            }
            else if (activePlant == PlantType.Rose || activePlant == PlantType.Roze)
            {
                // Right Click / E: Heavy Piercing Thorn Volley (45 Dmg, 4.5s Cooldown)
                // Left Click: Hitscan Thorn Shot (25 Dmg, 0.9s Cooldown)
                if ((rightClick || keyE) && arenaSecondaryCooldownTimer <= 0f)
                {
                    FireRoseHitscanThorn(pc, isHeavyVolley: true);
                }
                else if (leftClick && arenaAbilityCooldownTimer <= 0f)
                {
                    isChargingRoseThorn = true;
                    roseThornChargeTimer = roseThornChargeDuration;
                }
            }
            else if (activePlant == PlantType.Durian)
            {
                // Right Click / E: Durian Roll (50 Dmg + 2s Stun, 6.0s Cooldown)
                // Left Click: Close-Range Spike Bash (28 Dmg, 1.0s Cooldown)
                if ((rightClick || keyE) && arenaSecondaryCooldownTimer <= 0f)
                {
                    isDurianRollingLocal = true;
                    durianRollTimer = durianRollDuration;
                    pc.isDurianRolling = true;
                    OnArenaAbilityFxLocal?.Invoke("ROLL", pc.transform.position, pc.transform.forward);
                }
                else if (leftClick && arenaAbilityCooldownTimer <= 0f)
                {
                    PerformDurianSpikeBash(pc);
                }
            }
        }
    }

    private void StartBucketheadParry(PlayerController pc)
    {
        isBucketParryingLocal = true;
        bucketParryTimer = bucketParryDuration; // 1.0 second active parry window
        arenaSecondaryCooldownTimer = bucketParryCooldown; // 5.0 seconds cooldown
        arenaSecondaryMaxCooldown = bucketParryCooldown;
        OnArenaAbilityFxLocal?.Invoke("PARRY_START", pc.transform.position, pc.transform.forward);
    }

    private void StartBunnySlide(PlayerController pc)
    {
        isBunnySlidingLocal = true;
        bunnySlideTimer = bunnySlideDuration; // 0.90s slide
        arenaSecondaryCooldownTimer = bunnySlideCooldown; // 3.0s cooldown
        arenaSecondaryMaxCooldown = bunnySlideCooldown;
        pc.isBunnySliding = true;
        OnArenaAbilityFxLocal?.Invoke("SLIDE", pc.transform.position, pc.transform.forward);
    }

    private void StartFeralLunge(PlayerController pc)
    {
        isFeralLungingLocal = true;
        feralLungeTimer = feralLungeDuration; // 0.65s high-speed forward dive
        feralLungeHitDealt = false;
        arenaSecondaryCooldownTimer = feralLungeCooldown; // 3.5s cooldown
        arenaSecondaryMaxCooldown = feralLungeCooldown;
        arenaAbilityCooldownTimer = Mathf.Max(arenaAbilityCooldownTimer, 0.55f);
        pc.isFeralLunging = true;
        OnArenaAbilityFxLocal?.Invoke("LUNGE", pc.transform.position, pc.transform.forward);
    }

    private GameObject CreateLocalFeralLungePovVisual(ZombieType zType)
    {
        GameObject root = new GameObject("LocalFeralLungePOV");
        root.transform.SetParent(arena3DRoot.transform, true);

        bool isBunny = (zType == ZombieType.Bunny);
        Material sleeveMat = CreateColoredMaterial(isBunny ? new Color(0.14f, 0.74f, 0.78f) : new Color(0.42f, 0.16f, 0.58f));
        Material stripeMat = CreateColoredMaterial(new Color(0.98f, 0.38f, 0.56f));
        Material skinMat = CreateColoredMaterial(isBunny ? new Color(0.56f, 0.80f, 0.36f) : new Color(0.50f, 0.74f, 0.30f));
        Material clawMat = CreateColoredMaterial(new Color(0.95f, 0.92f, 0.78f));

        // Left & right diving zombie arms & claws lunging forward in POV toward the Plant player!
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject armPivot = new GameObject(side < 0 ? "LeftDivingArm" : "RightDivingArm");
            armPivot.transform.SetParent(root.transform, false);
            armPivot.transform.localPosition = new Vector3(side * 0.34f, -0.05f, 0.05f);
            armPivot.transform.localRotation = Quaternion.Euler(0f, -side * 14f, -side * 12f);

            GameObject sleeve = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sleeve.transform.SetParent(armPivot.transform, false);
            sleeve.transform.localPosition = new Vector3(0f, 0f, -0.10f);
            sleeve.transform.localRotation = Quaternion.Euler(84f, 0f, 0f);
            sleeve.transform.localScale = new Vector3(0.18f, 0.24f, 0.18f);
            DestroyCollider(sleeve);
            sleeve.GetComponent<Renderer>().sharedMaterial = sleeveMat;

            if (isBunny)
            {
                GameObject cuff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cuff.transform.SetParent(armPivot.transform, false);
                cuff.transform.localPosition = new Vector3(0f, 0f, 0.08f);
                cuff.transform.localRotation = Quaternion.Euler(84f, 0f, 0f);
                cuff.transform.localScale = new Vector3(0.19f, 0.05f, 0.19f);
                DestroyCollider(cuff);
                cuff.GetComponent<Renderer>().sharedMaterial = stripeMat;
            }

            GameObject hand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hand.transform.SetParent(armPivot.transform, false);
            hand.transform.localPosition = new Vector3(0f, 0.02f, 0.24f);
            hand.transform.localScale = new Vector3(0.20f, 0.15f, 0.24f);
            DestroyCollider(hand);
            hand.GetComponent<Renderer>().sharedMaterial = skinMat;

            // 3 sharp lunging claws per hand
            for (int c = -1; c <= 1; c++)
            {
                GameObject claw = GameObject.CreatePrimitive(PrimitiveType.Cube);
                claw.transform.SetParent(armPivot.transform, false);
                claw.transform.localPosition = new Vector3(c * 0.055f, 0.01f, 0.40f);
                claw.transform.localRotation = Quaternion.Euler(18f, c * 8f, 0f);
                claw.transform.localScale = new Vector3(0.032f, 0.032f, 0.18f);
                DestroyCollider(claw);
                claw.GetComponent<Renderer>().sharedMaterial = clawMat;
            }
        }

        return root;
    }

    private GameObject CreateLocalParryShieldVisual()
    {
        GameObject root = new GameObject("LocalBucketParryShield");
        root.transform.SetParent(arena3DRoot.transform, true);

        Material shieldMat = CreateColoredMaterial(new Color(0.35f, 0.92f, 1.0f));
        Material rimMat = CreateColoredMaterial(new Color(0.85f, 0.92f, 0.98f));

        // Left & right curved energy bars so player can still see crosshair clearly
        GameObject leftArc = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftArc.transform.SetParent(root.transform, false);
        leftArc.transform.localPosition = new Vector3(-0.42f, 0f, 0f);
        leftArc.transform.localRotation = Quaternion.Euler(0f, -18f, 8f);
        leftArc.transform.localScale = new Vector3(0.08f, 0.56f, 0.04f);
        DestroyCollider(leftArc);
        leftArc.GetComponent<Renderer>().sharedMaterial = shieldMat;

        GameObject rightArc = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightArc.transform.SetParent(root.transform, false);
        rightArc.transform.localPosition = new Vector3(0.42f, 0f, 0f);
        rightArc.transform.localRotation = Quaternion.Euler(0f, 18f, -8f);
        rightArc.transform.localScale = new Vector3(0.08f, 0.56f, 0.04f);
        DestroyCollider(rightArc);
        rightArc.GetComponent<Renderer>().sharedMaterial = shieldMat;

        GameObject botRim = GameObject.CreatePrimitive(PrimitiveType.Cube);
        botRim.transform.SetParent(root.transform, false);
        botRim.transform.localPosition = new Vector3(0f, -0.28f, 0f);
        botRim.transform.localScale = new Vector3(0.76f, 0.06f, 0.04f);
        DestroyCollider(botRim);
        botRim.GetComponent<Renderer>().sharedMaterial = rimMat;

        return root;
    }

    private GameObject CreateLocalBunnySlideLegsVisual()
    {
        GameObject root = new GameObject("LocalBunnySlideLegs");
        root.transform.SetParent(arena3DRoot.transform, true);

        Material pantsMat = CreateColoredMaterial(new Color(0.12f, 0.16f, 0.24f));
        Material shoeMat = CreateColoredMaterial(new Color(1.0f, 0.42f, 0.0f));
        Material stripeMat = CreateColoredMaterial(Color.white);

        for (int side = -1; side <= 1; side += 2)
        {
            GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leg.transform.SetParent(root.transform, false);
            leg.transform.localPosition = new Vector3(side * 0.18f, 0f, 0.15f);
            leg.transform.localRotation = Quaternion.Euler(82f, 0f, 0f);
            leg.transform.localScale = new Vector3(0.18f, 0.36f, 0.18f);
            DestroyCollider(leg);
            leg.GetComponent<Renderer>().sharedMaterial = pantsMat;

            GameObject shoe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shoe.transform.SetParent(root.transform, false);
            shoe.transform.localPosition = new Vector3(side * 0.18f, 0.08f, 0.52f);
            shoe.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
            shoe.transform.localScale = new Vector3(0.20f, 0.28f, 0.14f);
            DestroyCollider(shoe);
            shoe.GetComponent<Renderer>().sharedMaterial = shoeMat;

            GameObject sole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sole.transform.SetParent(shoe.transform, false);
            sole.transform.localPosition = new Vector3(0f, 0f, 0.45f);
            sole.transform.localScale = new Vector3(1.02f, 1.02f, 0.3f);
            DestroyCollider(sole);
            sole.GetComponent<Renderer>().sharedMaterial = stripeMat;
        }

        return root;
    }

    private void UpdateSoloArenaSparringBot(PlayerController pc, bool localIsZombie, PlantType activePlant, ZombieType activeZombie)
    {
        if (remoteImmobilizedTimer > 0f) return;

        // Gentle strafing & facing player so solo testing feels alive
        soloBotStrafePhase += Time.deltaTime * 1.6f;
        Vector3 toPlayer = pc.transform.position - remoteArenaAvatar.transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.05f)
        {
            Quaternion lookRot = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            remoteArenaAvatar.transform.rotation = Quaternion.Slerp(remoteArenaAvatar.transform.rotation, lookRot, Time.deltaTime * 8f);
        }

        soloBotActionTimer -= Time.deltaTime;
        if (soloBotActionTimer <= 0f)
        {
            if (localIsZombie)
            {
                // Remote AI is a Plant shooting at the Zombie player (great for testing Buckethead Parry & Bunny Slide!)
                int dmg = (activePlant == PlantType.Moonflower) ? 45 : ((activePlant == PlantType.Durian) ? 35 : 25);
                soloBotActionTimer = (activePlant == PlantType.Moonflower) ? 3.2f : 2.3f;

                Vector3 start = remoteArenaAvatar.transform.position + new Vector3(0f, 0.6f, 0f);
                Vector3 target = pc.transform.position + new Vector3(0f, isBunnySlidingLocal ? 0.45f : 0.1f, 0f);
                Color beamCol = (activePlant == PlantType.Moonflower) ? new Color(0.25f, 0.95f, 1.0f) : new Color(1.0f, 0.20f, 0.40f);

                // Check if an arena obstacle blocks the AI's laser!
                Physics.SyncTransforms();
                Vector3 dir = target - start;
                float dist = dir.magnitude;
                bool blockedByObstacle = false;
                Vector3 beamEnd = target;
                if (dist > 0.1f)
                {
                    RaycastHit[] hits = Physics.RaycastAll(start, dir.normalized, dist);
                    float closestWall = dist;
                    for (int i = 0; i < hits.Length; i++)
                    {
                        Collider c = hits[i].collider;
                        if (c == null) continue;
                        if (IsOpponentHitbox(c)) continue;
                        if (c.GetComponent<PlayerController>() != null) continue;
                        if (hits[i].distance < closestWall)
                        {
                            closestWall = hits[i].distance;
                            beamEnd = hits[i].point;
                            blockedByObstacle = true;
                        }
                    }
                }

                Spawn3DBeamEffect(start, beamEnd, beamCol, 0.16f, 0.30f);

                if (blockedByObstacle)
                {
                    // Laser stopped by cover obstacle!
                }
                else if (isBunnySlidingLocal)
                {
                    arenaHitIndicatorTimer = 0.85f;
                    arenaLastHitLabel = "💨 DODGED SHOT WITH BUNNY SLIDE! 💨";
                }
                else
                {
                    ApplyRemoteArenaDamage(dmg, 0f);
                }
            }
            else
            {
                // Remote AI is a Zombie (if Buckethead, periodically raises Parry shield; if Bunny, periodically slides!)
                if (activeZombie == ZombieType.Buckethead)
                {
                    remoteIsBucketParrying = !remoteIsBucketParrying;
                    soloBotActionTimer = remoteIsBucketParrying ? 1.0f : 3.5f;
                }
                else if (activeZombie == ZombieType.Bunny)
                {
                    remoteIsBunnySliding = !remoteIsBunnySliding;
                    soloBotActionTimer = remoteIsBunnySliding ? 0.9f : 2.5f;
                }
                else
                {
                    soloBotActionTimer = 2.5f;
                }
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

    private void FireMoonflowerSolarBolt(PlayerController pc)
    {
        arenaAbilityCooldownTimer = 0.80f;
        arenaPrimaryMaxCooldown = 0.80f;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Ray aimRay = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 start = pc.transform.position + new Vector3(0f, 0.55f, 0f) + targetCamera.transform.forward * 0.45f;

        bool hitOpponent = CheckCursorRayHitOpponent(aimRay, 0.35f, 60f, out Vector3 hitPoint);
        Spawn3DBeamEffect(start, hitPoint, new Color(1.0f, 0.92f, 0.32f), 0.12f, 0.22f);
        OnArenaAbilityFxLocal?.Invoke("BEAM", start, hitPoint);

        if (hitOpponent)
        {
            if (remoteIsBucketParrying)
            {
                RegisterArenaParriedByOpponent(18);
            }
            else
            {
                DealDamageToOpponent(18, "SOLAR BOLT HIT (18 DMG)!", 0f);
            }
        }
    }

    private void FireMoonflowerLaserBeam(PlayerController pc)
    {
        arenaSecondaryCooldownTimer = 6.0f;
        arenaSecondaryMaxCooldown = 6.0f;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Ray aimRay = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 beamStart = pc.transform.position + new Vector3(0f, 1.25f, 0f) + targetCamera.transform.forward * 0.35f;

        bool hitOpponent = CheckCursorRayHitOpponent(aimRay, 0.55f, 65f, out Vector3 hitPoint);
        Spawn3DBeamEffect(beamStart, hitPoint, new Color(0.25f, 0.95f, 1.0f), 0.28f, 0.45f);
        OnArenaAbilityFxLocal?.Invoke("BEAM", beamStart, hitPoint);

        if (hitOpponent)
        {
            if (remoteIsBucketParrying)
            {
                RegisterArenaParriedByOpponent(65);
            }
            else
            {
                DealDamageToOpponent(65, "MOON LASER BEAM HIT (65 DMG)!", 0f);
            }
        }
    }

    private void FireRoseHitscanThorn(PlayerController pc, bool isHeavyVolley)
    {
        if (isHeavyVolley)
        {
            arenaSecondaryCooldownTimer = 4.5f;
            arenaSecondaryMaxCooldown = 4.5f;
        }
        else
        {
            arenaAbilityCooldownTimer = 0.90f;
            arenaPrimaryMaxCooldown = 0.90f;
        }

        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Ray aimRay = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 shotStart = pc.transform.position + new Vector3(0f, 0.52f, 0f) + targetCamera.transform.forward * 0.45f + targetCamera.transform.right * 0.18f;

        float radius = isHeavyVolley ? 0.48f : 0.32f;
        bool hitOpponent = CheckCursorRayHitOpponent(aimRay, radius, 65f, out Vector3 hitPoint);
        Color col = isHeavyVolley ? new Color(1.0f, 0.08f, 0.55f) : new Color(1.0f, 0.15f, 0.38f);
        float thick = isHeavyVolley ? 0.22f : 0.10f;
        Spawn3DBeamEffect(shotStart, hitPoint, col, thick, 0.26f);
        OnArenaAbilityFxLocal?.Invoke("THORN", shotStart, hitPoint);

        if (hitOpponent)
        {
            int dmg = isHeavyVolley ? 45 : 25;
            if (remoteIsBucketParrying)
            {
                RegisterArenaParriedByOpponent(dmg);
            }
            else
            {
                DealDamageToOpponent(dmg, isHeavyVolley ? "THORN VOLLEY HIT (45 DMG)!" : "ROSE THORN HIT (25 DMG)!", 0f);
            }
        }
    }

    private void PerformDurianSpikeBash(PlayerController pc)
    {
        arenaAbilityCooldownTimer = 1.0f;
        arenaPrimaryMaxCooldown = 1.0f;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Vector3 bashCenter = targetCamera.transform.position + targetCamera.transform.forward * 1.25f;
        Spawn3DDurianSpikeBurstEffect(bashCenter, targetCamera.transform.rotation);
        OnArenaAbilityFxLocal?.Invoke("DURIAN_BASH", bashCenter, targetCamera.transform.forward);

        Ray aimRay = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        bool hitOpponent = CheckCursorRayHitOpponent(aimRay, 0.95f, 3.8f, out _);

        if (!hitOpponent && remoteArenaAvatar != null)
        {
            Vector3 toOpp = remoteArenaAvatar.transform.position - pc.transform.position;
            if (toOpp.magnitude <= 3.8f && Vector3.Angle(targetCamera.transform.forward, toOpp) <= 62f)
            {
                if (!Physics.Linecast(targetCamera.transform.position, remoteArenaAvatar.transform.position + Vector3.up * 0.5f, out RaycastHit blockHit) ||
                    IsOpponentHitbox(blockHit.collider) ||
                    blockHit.collider.GetComponent<PlayerController>() != null)
                {
                    hitOpponent = true;
                }
            }
        }

        if (hitOpponent)
        {
            if (remoteIsBucketParrying)
            {
                RegisterArenaParriedByOpponent(28);
            }
            else
            {
                DealDamageToOpponent(28, "DURIAN SPIKE BASH (28 DMG)!", 0f);
            }
        }
    }

    private void PerformNormalZombieLungeSlash(PlayerController pc)
    {
        StartFeralLunge(pc);
    }

    private void PerformZombieMeleeSlash(PlayerController pc, int damage = 25, float cooldown = 0.8f, string hitLabel = "ZOMBIE MELEE SLASH!")
    {
        arenaAbilityCooldownTimer = cooldown;
        arenaPrimaryMaxCooldown = cooldown;
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
                if (!Physics.Linecast(targetCamera.transform.position, remoteArenaAvatar.transform.position + Vector3.up * 0.5f, out RaycastHit blockHit) ||
                    IsOpponentHitbox(blockHit.collider) ||
                    blockHit.collider.GetComponent<PlayerController>() != null)
                {
                    hitOpponent = true;
                }
            }
        }

        if (hitOpponent)
        {
            DealDamageToOpponent(damage, hitLabel, 0f);
        }
    }

    private void DealDamageToOpponent(int damage, string label, float stunDuration)
    {
        arenaHitsLanded++;
        arenaHitIndicatorTimer = 0.95f;
        arenaLastHitLabel = label;
        remoteHitFlashTimer = 0.25f;

        remoteArenaCurrentHP = Mathf.Max(0, remoteArenaCurrentHP - damage);
        UpdateRemoteWorldHPBarScale();

        OnArenaAttackHitLocal?.Invoke(damage, stunDuration);

        if (remoteArenaCurrentHP <= 0)
        {
            bool localIsZombie = GetEffectiveLocalRole() == MultiplayerMenuManager.PlayerRole.Zombie;
            ResolveArena(zombieWon: localIsZombie, notifyNetwork: true);
        }
    }

    private void RegisterArenaParriedByOpponent(int blockedDamage)
    {
        float bonusPct = Mathf.Clamp(blockedDamage * 1.8f, 25f, 140f);
        arenaHitIndicatorTimer = 1.2f;
        arenaLastHitLabel = $"🛡 BUCKETHEAD PARRIED YOUR {blockedDamage} DMG! (+{bonusPct:F0}% ZOMBIE SPEED) 🛡";
        OnArenaAttackHitLocal?.Invoke(blockedDamage, 0f);
    }

    private void UpdateRemoteWorldHPBarScale()
    {
        if (remoteHpBarFill == null) return;
        float pct = Mathf.Clamp01((float)remoteArenaCurrentHP / Mathf.Max(1, remoteArenaMaxHP));
        remoteHpBarFill.localScale = new Vector3(1.24f * pct, 0.12f, 1f);
        remoteHpBarFill.localPosition = new Vector3(-0.62f * (1f - pct), 0f, -0.01f);
    }

    private bool CheckCursorRayHitOpponent(Ray aimRay, float sphereRadius, float maxDist, out Vector3 endPoint)
    {
        // Ensure all newly spawned or rerolled 3D obstacle colliders are up to date in the Physics world!
        Physics.SyncTransforms();

        endPoint = aimRay.GetPoint(maxDist);
        bool hitOpponent = false;
        float closestEnvDist = maxDist;
        float closestOpponentDist = maxDist;

        // 1. First find the closest solid environment obstacle (walls, pillars, crates) along the ray
        RaycastHit[] directHits = Physics.RaycastAll(aimRay, maxDist);
        for (int i = 0; i < directHits.Length; i++)
        {
            Collider c = directHits[i].collider;
            if (c == null) continue;
            if (c.GetComponent<PlayerController>() != null) continue;

            if (!IsOpponentHitbox(c))
            {
                if (directHits[i].distance < closestEnvDist)
                {
                    closestEnvDist = directHits[i].distance;
                    endPoint = directHits[i].point;
                }
            }
        }

        // 2. Check if the direct ray hit the opponent BEFORE hitting the closest obstacle wall
        for (int i = 0; i < directHits.Length; i++)
        {
            Collider c = directHits[i].collider;
            if (c == null) continue;
            if (IsOpponentHitbox(c) && directHits[i].distance < closestEnvDist && directHits[i].distance <= closestOpponentDist)
            {
                hitOpponent = true;
                closestOpponentDist = directHits[i].distance;
                endPoint = directHits[i].point;
            }
        }

        if (hitOpponent)
        {
            return true;
        }

        // 3. Check generous spherecast up to the closest obstacle distance, and verify clear line-of-sight so lasers NEVER pierce around or through walls!
        RaycastHit[] sphereHits = Physics.SphereCastAll(aimRay, sphereRadius, closestEnvDist);
        for (int i = 0; i < sphereHits.Length; i++)
        {
            Collider c = sphereHits[i].collider;
            if (IsOpponentHitbox(c) && sphereHits[i].distance < closestEnvDist)
            {
                Vector3 targetCheckPt = c.bounds.center;
                Vector3 toTarget = targetCheckPt - aimRay.origin;
                float distToTarget = toTarget.magnitude;

                bool blockedByWall = false;
                if (distToTarget > 0.05f)
                {
                    RaycastHit[] losHits = Physics.RaycastAll(aimRay.origin, toTarget.normalized, distToTarget);
                    for (int j = 0; j < losHits.Length; j++)
                    {
                        Collider lc = losHits[j].collider;
                        if (lc == null) continue;
                        if (lc.GetComponent<PlayerController>() != null) continue;
                        if (!IsOpponentHitbox(lc))
                        {
                            blockedByWall = true;
                            break;
                        }
                    }
                }

                if (!blockedByWall)
                {
                    endPoint = sphereHits[i].point != Vector3.zero ? sphereHits[i].point : targetCheckPt;
                    return true;
                }
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
        DealDamageToOpponent(25, label, 0f);
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
        else if (fxType == "DURIAN_BASH")
        {
            Quaternion rot = endPos.sqrMagnitude > 0.001f ? Quaternion.LookRotation(endPos) : Quaternion.identity;
            Spawn3DDurianSpikeBurstEffect(startPos, rot);
        }
        else if (fxType == "PARRY")
        {
            Spawn3DBeamEffect(startPos, startPos + Vector3.up * 2.2f, new Color(0.35f, 0.95f, 1.0f), 0.55f, 0.35f);
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
        beam.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(color);
        Destroy(beam, duration);
    }

    private void Spawn3DDurianSpikeBurstEffect(Vector3 center, Quaternion viewRot)
    {
        if (arena3DRoot == null) return;
        GameObject burstRoot = new GameObject("DurianSpikeBashFX");
        burstRoot.transform.SetParent(arena3DRoot.transform, true);
        burstRoot.transform.SetPositionAndRotation(center, viewRot);

        Material shellMat = CreateColoredMaterial(new Color(0.68f, 0.56f, 0.16f));
        Material spikeGoldMat = CreateColoredMaterial(new Color(0.95f, 0.84f, 0.22f));
        Material spikeLimeMat = CreateColoredMaterial(new Color(0.55f, 0.85f, 0.24f));

        // Central spiky Durian mace core
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.transform.SetParent(burstRoot.transform, false);
        core.transform.localPosition = Vector3.zero;
        core.transform.localScale = new Vector3(0.52f, 0.52f, 0.52f);
        DestroyCollider(core);
        core.GetComponent<Renderer>().sharedMaterial = shellMat;

        // Interlocking diamond spike cages + 8 forward-thrusting golden durian thorns (distinct from Zombie claw slash!)
        GameObject cageA = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cageA.transform.SetParent(burstRoot.transform, false);
        cageA.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
        cageA.transform.localScale = new Vector3(0.46f, 0.46f, 0.46f);
        DestroyCollider(cageA);
        cageA.GetComponent<Renderer>().sharedMaterial = spikeGoldMat;

        GameObject cageB = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cageB.transform.SetParent(burstRoot.transform, false);
        cageB.transform.localRotation = Quaternion.Euler(0f, 45f, 45f);
        cageB.transform.localScale = new Vector3(0.46f, 0.46f, 0.46f);
        DestroyCollider(cageB);
        cageB.GetComponent<Renderer>().sharedMaterial = spikeLimeMat;

        int spikeCount = 8;
        for (int i = 0; i < spikeCount; i++)
        {
            float angle = i * (360f / spikeCount);
            float rad = angle * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad) * 0.65f, Mathf.Sin(rad) * 0.65f, 0.85f).normalized;

            GameObject thorn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            thorn.transform.SetParent(burstRoot.transform, false);
            thorn.transform.localPosition = dir * 0.36f;
            thorn.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, 45f);
            thorn.transform.localScale = new Vector3(0.11f, 0.11f, 0.52f);
            DestroyCollider(thorn);
            thorn.GetComponent<Renderer>().sharedMaterial = (i % 2 == 0) ? spikeGoldMat : spikeLimeMat;
        }

        Destroy(burstRoot, 0.24f);
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
            claw.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.85f, 0.35f, 1.0f));
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
        localInitiatedCurrentClash = false;

        if (arena3DRoot != null)
        {
            Destroy(arena3DRoot);
            arena3DRoot = null;
            remoteArenaAvatar = null;
            remoteBodyHitbox = null;
            remoteHeadHitbox = null;
            remoteZombieVisualRoot = null;
            remoteParryShieldObj = null;
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
        for (int i = 0; i < zombieCooldownTimers.Length; i++) zombieCooldownTimers[i] = 0f;
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
        if (cachedPlayerController != null)
        {
            if (!cachedPlayerController.gameObject.activeInHierarchy)
            {
                cachedPlayerController.gameObject.SetActive(true);
            }
            cachedPlayerController.enabled = true;
            return cachedPlayerController;
        }

        GameObject rootPlayer = GameObject.Find("Player");
        if (rootPlayer != null)
        {
            if (!rootPlayer.activeInHierarchy)
            {
                rootPlayer.SetActive(true);
            }

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
                cachedPlayerController = pc;
                return pc;
            }
        }

        cachedPlayerController = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (cachedPlayerController != null)
        {
            if (!cachedPlayerController.gameObject.activeInHierarchy)
            {
                cachedPlayerController.gameObject.SetActive(true);
            }
            cachedPlayerController.enabled = true;
            return cachedPlayerController;
        }

        // Auto-create a Player controller if none exists in the scene so 3D Arena transport always works!
        GameObject autoPlayer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        autoPlayer.name = "Player";
        Collider defaultCol = autoPlayer.GetComponent<Collider>();
        if (defaultCol != null) Destroy(defaultCol);
        CharacterController cc = autoPlayer.AddComponent<CharacterController>();
        cc.height = 2.0f;
        cc.radius = 0.45f;
        cc.center = Vector3.zero;
        cachedPlayerController = autoPlayer.AddComponent<PlayerController>();
        return cachedPlayerController;
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
        arenaFloorRenderer = floor.GetComponent<Renderer>();
        arenaFloorRenderer.sharedMaterial = CreateColoredMaterial(new Color(0.22f, 0.28f, 0.24f));

        // 4 Outer Perimeter Arena Walls
        CreateArenaBlock("Wall_North", new Vector3(0f, 1.75f, 22f), new Vector3(44f, 3.5f, 0.8f), new Color(0.32f, 0.36f, 0.42f));
        CreateArenaBlock("Wall_South", new Vector3(0f, 1.75f, -22f), new Vector3(44f, 3.5f, 0.8f), new Color(0.32f, 0.36f, 0.42f));
        CreateArenaBlock("Wall_East", new Vector3(22f, 1.75f, 0f), new Vector3(0.8f, 3.5f, 44f), new Color(0.32f, 0.36f, 0.42f));
        CreateArenaBlock("Wall_West", new Vector3(-22f, 1.75f, 0f), new Vector3(0.8f, 3.5f, 44f), new Color(0.32f, 0.36f, 0.42f));

        // Build randomized preset + procedural layout inside the arena
        if (currentArenaLayoutSeed == 0)
        {
            currentArenaLayoutSeed = UnityEngine.Random.Range(1000, 9999999);
        }
        RebuildRandomArenaLayout(currentArenaLayoutSeed);

        // Spawn the OTHER player's avatar (synced over the network) with accurate hitboxes.
        // Plant player sees the specific Zombie (Normal, Buckethead, or Bunny); Zombie player sees the specific Plant.
        MultiplayerMenuManager.PlayerRole myRole = GetEffectiveLocalRole();
        bool localIsPlant = myRole == MultiplayerMenuManager.PlayerRole.Plant;
        PlantType touchedType = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;
        ZombieType touchedZombieType = (clashingZombie != null) ? clashingZombie.zombieType : clashingZombieType;

        remoteArenaAvatar = new GameObject("Arena3D_RemotePlayer");
        remoteArenaAvatar.transform.SetParent(arena3DRoot.transform, false);
        Vector3 remoteSpawn = localIsPlant ? ArenaZombieSpawn : ArenaPlantSpawn;
        remoteArenaAvatar.transform.SetPositionAndRotation(remoteSpawn, Quaternion.Euler(0f, localIsPlant ? 180f : 0f, 0f));

        // Primary body hitbox + upper head/blossom hitbox for each plant and zombie in the 3D 1v1 zone
        remoteBodyHitbox = remoteArenaAvatar.AddComponent<CapsuleCollider>();
        remoteBodyHitbox.height = 2.0f;
        remoteBodyHitbox.radius = (!localIsPlant && touchedType == PlantType.Durian) ? 0.78f : 0.58f;
        remoteBodyHitbox.center = Vector3.zero;

        remoteHeadHitbox = remoteArenaAvatar.AddComponent<SphereCollider>();
        remoteHeadHitbox.radius = (!localIsPlant && touchedType == PlantType.Durian) ? 0.72f : 0.52f;
        remoteHeadHitbox.center = new Vector3(0f, 0.35f, 0f);

        Rigidbody rb = remoteArenaAvatar.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        GameObject visual;
        if (localIsPlant)
        {
            visual = Create3DZombiePlaceholder(touchedZombieType, $"Visual_3D_{touchedZombieType}");
            currentArenaOpponentName = $"{touchedZombieType.ToString().ToUpper()} ZOMBIE";
            remoteZombieVisualRoot = visual.transform;

            visual.transform.SetParent(remoteArenaAvatar.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
        }
        else
        {
            visual = Create3DPlantPlaceholder(touchedType, $"Visual_3D_{touchedType}");
            currentArenaOpponentName = $"{touchedType.ToString().ToUpper()} PLAYER";
            remoteZombieVisualRoot = null;

            visual.transform.SetParent(remoteArenaAvatar.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
        }

        // World-space 3D HP Bar above the opponent
        CreateRemoteWorldHPBar(remoteArenaAvatar.transform, localIsPlant);
    }

    public void ApplyRemoteArenaSeed(int seed)
    {
        currentArenaLayoutSeed = seed;
        if (isIn3DArena && arena3DRoot != null)
        {
            RebuildRandomArenaLayout(seed);
        }
    }

    public void RebuildRandomArenaLayout(int seed)
    {
        currentArenaLayoutSeed = seed;
        if (arena3DRoot == null) return;

        if (arenaObstaclesRoot != null)
        {
            Destroy(arenaObstaclesRoot);
            arenaObstaclesRoot = null;
        }

        arenaObstaclesRoot = new GameObject("ArenaObstacles_Random");
        arenaObstaclesRoot.transform.SetParent(arena3DRoot.transform, false);
        arenaObstaclesRoot.transform.localPosition = Vector3.zero;

        System.Random rng = new System.Random(seed);

        // 12 distinct arena presets + procedural scatter and mirroring
        int presetIndex = rng.Next(0, 12);
        bool mirrorX = rng.NextDouble() > 0.5;
        bool mirrorZ = rng.NextDouble() > 0.5;

        // Random color palettes so each preset & seed feels fresh
        Color[] floorTints = new Color[]
        {
            new Color(0.22f, 0.28f, 0.24f), // Mossy Slate
            new Color(0.26f, 0.24f, 0.30f), // Twilight Dusk
            new Color(0.28f, 0.26f, 0.21f), // Sand Courtyard
            new Color(0.19f, 0.25f, 0.30f), // Moonlit Stone
            new Color(0.25f, 0.22f, 0.22f), // Obsidian Rust
            new Color(0.20f, 0.29f, 0.27f)  // Jade Garden
        };
        if (arenaFloorRenderer != null)
        {
            arenaFloorRenderer.sharedMaterial = CreateColoredMaterial(floorTints[rng.Next(0, floorTints.Length)]);
        }

        Color pillarColor = new Color(
            0.40f + (float)rng.NextDouble() * 0.16f,
            0.44f + (float)rng.NextDouble() * 0.16f,
            0.42f + (float)rng.NextDouble() * 0.16f
        );
        Color wallColor = new Color(
            0.34f + (float)rng.NextDouble() * 0.14f,
            0.38f + (float)rng.NextDouble() * 0.14f,
            0.42f + (float)rng.NextDouble() * 0.14f
        );
        Color crateColor = new Color(
            0.46f + (float)rng.NextDouble() * 0.14f,
            0.33f + (float)rng.NextDouble() * 0.10f,
            0.20f + (float)rng.NextDouble() * 0.08f
        );
        Color accentColor = new Color(
            0.30f + (float)rng.NextDouble() * 0.22f,
            0.46f + (float)rng.NextDouble() * 0.20f,
            0.38f + (float)rng.NextDouble() * 0.22f
        );

        float mx = mirrorX ? -1f : 1f;
        float mz = mirrorZ ? -1f : 1f;

        switch (presetIndex)
        {
            case 0:
                currentArenaPresetName = "Crossfire Bastion";
                // Central split hub with angled wing shields & corner bunkers
                SpawnRandomObstacle("Center_Bastion_W", new Vector3(-2.6f * mx, 1.3f, 0f), new Vector3(1.6f, 2.6f, 3.6f), 15f * mx, pillarColor, rng);
                SpawnRandomObstacle("Center_Bastion_E", new Vector3(2.6f * mx, 1.3f, 0f), new Vector3(1.6f, 2.6f, 3.6f), -15f * mx, pillarColor, rng);
                SpawnRandomObstacle("Wing_NW", new Vector3(-8.5f, 1.0f, 6.2f), new Vector3(4.2f, 2.0f, 1.1f), 28f, wallColor, rng);
                SpawnRandomObstacle("Wing_NE", new Vector3(8.5f, 1.0f, 6.2f), new Vector3(4.2f, 2.0f, 1.1f), -28f, wallColor, rng);
                SpawnRandomObstacle("Wing_SW", new Vector3(-8.5f, 1.0f, -6.2f), new Vector3(4.2f, 2.0f, 1.1f), -28f, wallColor, rng);
                SpawnRandomObstacle("Wing_SE", new Vector3(8.5f, 1.0f, -6.2f), new Vector3(4.2f, 2.0f, 1.1f), 28f, wallColor, rng);
                SpawnRandomObstacle("Flank_Crate_W", new Vector3(-15.0f, 0.8f, 0f), new Vector3(2.6f, 1.6f, 2.6f), 12f, crateColor, rng);
                SpawnRandomObstacle("Flank_Crate_E", new Vector3(15.0f, 0.8f, 0f), new Vector3(2.6f, 1.6f, 2.6f), -12f, crateColor, rng);
                break;

            case 1:
                currentArenaPresetName = "Twin Fortresses";
                // Two wide defensive L-bunkers on each half + side pillars
                SpawnRandomObstacle("Fort_North_Front", new Vector3(0f, 0.95f, 6.8f), new Vector3(6.4f, 1.9f, 1.1f), 0f, wallColor, rng);
                SpawnRandomObstacle("Fort_South_Front", new Vector3(0f, 0.95f, -6.8f), new Vector3(6.4f, 1.9f, 1.1f), 0f, wallColor, rng);
                SpawnRandomObstacle("Fort_NW_Side", new Vector3(-5.2f, 1.2f, 8.2f), new Vector3(1.2f, 2.4f, 3.8f), 18f, pillarColor, rng);
                SpawnRandomObstacle("Fort_NE_Side", new Vector3(5.2f, 1.2f, 8.2f), new Vector3(1.2f, 2.4f, 3.8f), -18f, pillarColor, rng);
                SpawnRandomObstacle("Fort_SW_Side", new Vector3(-5.2f, 1.2f, -8.2f), new Vector3(1.2f, 2.4f, 3.8f), -18f, pillarColor, rng);
                SpawnRandomObstacle("Fort_SE_Side", new Vector3(5.2f, 1.2f, -8.2f), new Vector3(1.2f, 2.4f, 3.8f), 18f, pillarColor, rng);
                SpawnRandomObstacle("Mid_Obelisk_W", new Vector3(-11.5f, 1.6f, 0f), new Vector3(2.2f, 3.2f, 2.2f), 45f, accentColor, rng);
                SpawnRandomObstacle("Mid_Obelisk_E", new Vector3(11.5f, 1.6f, 0f), new Vector3(2.2f, 3.2f, 2.2f), 45f, accentColor, rng);
                break;

            case 2:
                currentArenaPresetName = "Labyrinth Maze";
                // Interlocking staggered corridor walls for close-quarters juking
                SpawnRandomObstacle("Maze_C1", new Vector3(-4.5f * mx, 1.35f, 2.5f * mz), new Vector3(6.2f, 2.7f, 1.0f), 0f, wallColor, rng);
                SpawnRandomObstacle("Maze_C2", new Vector3(4.5f * mx, 1.35f, -2.5f * mz), new Vector3(6.2f, 2.7f, 1.0f), 0f, wallColor, rng);
                SpawnRandomObstacle("Maze_V1", new Vector3(-8.5f * mx, 1.35f, -4.0f * mz), new Vector3(1.0f, 2.7f, 6.5f), 0f, pillarColor, rng);
                SpawnRandomObstacle("Maze_V2", new Vector3(8.5f * mx, 1.35f, 4.0f * mz), new Vector3(1.0f, 2.7f, 6.5f), 0f, pillarColor, rng);
                SpawnRandomObstacle("Maze_Outer_NW", new Vector3(-13.5f, 1.2f, 9.5f), new Vector3(4.8f, 2.4f, 1.2f), 20f, accentColor, rng);
                SpawnRandomObstacle("Maze_Outer_SE", new Vector3(13.5f, 1.2f, -9.5f), new Vector3(4.8f, 2.4f, 1.2f), 20f, accentColor, rng);
                SpawnRandomObstacle("Maze_Outer_NE", new Vector3(13.5f, 1.2f, 9.5f), new Vector3(1.2f, 2.4f, 4.8f), -15f, crateColor, rng);
                SpawnRandomObstacle("Maze_Outer_SW", new Vector3(-13.5f, 1.2f, -9.5f), new Vector3(1.2f, 2.4f, 4.8f), -15f, crateColor, rng);
                break;

            case 3:
                currentArenaPresetName = "Colosseum Pillars";
                // Circular ring of 8 tall pillars + central altar cover
                SpawnRandomObstacle("Colosseum_Altar", new Vector3(0f, 0.75f, 0f), new Vector3(3.4f, 1.5f, 3.4f), 45f, accentColor, rng);
                int ringCount = 8;
                float ringRadius = 9.8f;
                for (int i = 0; i < ringCount; i++)
                {
                    float ang = (i * Mathf.PI * 2f / ringCount) + 0.35f; // offset so north/south spawn sightlines aren't blocked at spawn
                    Vector3 pos = new Vector3(Mathf.Cos(ang) * ringRadius, 1.65f, Mathf.Sin(ang) * ringRadius);
                    SpawnRandomObstacle($"Colosseum_Pillar_{i}", pos, new Vector3(1.9f, 3.3f, 1.9f), ang * Mathf.Rad2Deg, pillarColor, rng);
                }
                break;

            case 4:
                currentArenaPresetName = "Three-Lane Trench";
                // Two long broken lane dividers creating West, Center, and East lanes
                SpawnRandomObstacle("LaneDiv_W_North", new Vector3(-6.8f, 1.3f, 6.2f), new Vector3(1.2f, 2.6f, 6.8f), 0f, wallColor, rng);
                SpawnRandomObstacle("LaneDiv_W_South", new Vector3(-6.8f, 1.3f, -6.2f), new Vector3(1.2f, 2.6f, 6.8f), 0f, wallColor, rng);
                SpawnRandomObstacle("LaneDiv_E_North", new Vector3(6.8f, 1.3f, 6.2f), new Vector3(1.2f, 2.6f, 6.8f), 0f, wallColor, rng);
                SpawnRandomObstacle("LaneDiv_E_South", new Vector3(6.8f, 1.3f, -6.2f), new Vector3(1.2f, 2.6f, 6.8f), 0f, wallColor, rng);
                SpawnRandomObstacle("Mid_Bridge_Cover", new Vector3(0f, 0.8f, 0f), new Vector3(4.2f, 1.6f, 1.5f), 25f * mx, crateColor, rng);
                SpawnRandomObstacle("Side_Bunker_W", new Vector3(-14.5f, 0.95f, 0f), new Vector3(3.2f, 1.9f, 2.0f), 0f, accentColor, rng);
                SpawnRandomObstacle("Side_Bunker_E", new Vector3(14.5f, 0.95f, 0f), new Vector3(3.2f, 1.9f, 2.0f), 0f, accentColor, rng);
                break;

            case 5:
                currentArenaPresetName = "Ancient Ruins";
                // Asymmetrical fallen monoliths, stepped blocks, and broken arches
                SpawnRandomObstacle("Ruin_Monolith_1", new Vector3(-3.8f * mx, 1.7f, 3.2f * mz), new Vector3(1.8f, 3.4f, 1.8f), 18f, pillarColor, rng);
                SpawnRandomObstacle("Ruin_FallenColumn_1", new Vector3(4.5f * mx, 0.65f, 2.0f * mz), new Vector3(5.6f, 1.3f, 1.4f), -32f, wallColor, rng);
                SpawnRandomObstacle("Ruin_Monolith_2", new Vector3(3.8f * mx, 1.7f, -3.2f * mz), new Vector3(1.8f, 3.4f, 1.8f), -18f, pillarColor, rng);
                SpawnRandomObstacle("Ruin_FallenColumn_2", new Vector3(-4.5f * mx, 0.65f, -2.0f * mz), new Vector3(5.6f, 1.3f, 1.4f), 32f, wallColor, rng);
                SpawnRandomObstacle("Ruin_Step_NW", new Vector3(-11.0f, 0.85f, 8.5f), new Vector3(3.5f, 1.7f, 2.4f), 40f, accentColor, rng);
                SpawnRandomObstacle("Ruin_Step_SE", new Vector3(11.0f, 0.85f, -8.5f), new Vector3(3.5f, 1.7f, 2.4f), 40f, accentColor, rng);
                SpawnRandomObstacle("Ruin_Step_NE", new Vector3(12.0f, 1.25f, 7.5f), new Vector3(2.4f, 2.5f, 2.4f), 15f, crateColor, rng);
                SpawnRandomObstacle("Ruin_Step_SW", new Vector3(-12.0f, 1.25f, -7.5f), new Vector3(2.4f, 2.5f, 2.4f), 15f, crateColor, rng);
                break;

            case 6:
                currentArenaPresetName = "Diamond Citadel";
                // 4 angled walls forming a central diamond arena-within-an-arena with 4 entry gaps
                SpawnRandomObstacle("Diamond_NW", new Vector3(-4.6f, 1.25f, 4.6f), new Vector3(5.5f, 2.5f, 1.0f), -45f, wallColor, rng);
                SpawnRandomObstacle("Diamond_NE", new Vector3(4.6f, 1.25f, 4.6f), new Vector3(5.5f, 2.5f, 1.0f), 45f, wallColor, rng);
                SpawnRandomObstacle("Diamond_SW", new Vector3(-4.6f, 1.25f, -4.6f), new Vector3(5.5f, 2.5f, 1.0f), 45f, wallColor, rng);
                SpawnRandomObstacle("Diamond_SE", new Vector3(4.6f, 1.25f, -4.6f), new Vector3(5.5f, 2.5f, 1.0f), -45f, wallColor, rng);
                SpawnRandomObstacle("Diamond_Core", new Vector3(0f, 1.5f, 0f), new Vector3(1.8f, 3.0f, 1.8f), 45f, accentColor, rng);
                SpawnRandomObstacle("Outer_Pillar_W", new Vector3(-14.0f, 1.5f, 0f), new Vector3(2.2f, 3.0f, 2.2f), 0f, pillarColor, rng);
                SpawnRandomObstacle("Outer_Pillar_E", new Vector3(14.0f, 1.5f, 0f), new Vector3(2.2f, 3.0f, 2.2f), 0f, pillarColor, rng);
                break;

            case 7:
                currentArenaPresetName = "Cargo Crate Yard";
                // Clusters of industrial shipping containers & stacked crates
                SpawnCrateCluster("Cluster_Center_W", new Vector3(-4.8f * mx, 0f, 1.5f * mz), crateColor, pillarColor, rng);
                SpawnCrateCluster("Cluster_Center_E", new Vector3(4.8f * mx, 0f, -1.5f * mz), crateColor, pillarColor, rng);
                SpawnCrateCluster("Cluster_NW", new Vector3(-11.5f, 0f, 7.5f), crateColor, accentColor, rng);
                SpawnCrateCluster("Cluster_NE", new Vector3(11.5f, 0f, 7.5f), crateColor, accentColor, rng);
                SpawnCrateCluster("Cluster_SW", new Vector3(-11.5f, 0f, -7.5f), crateColor, accentColor, rng);
                SpawnCrateCluster("Cluster_SE", new Vector3(11.5f, 0f, -7.5f), crateColor, accentColor, rng);
                break;

            case 8:
                currentArenaPresetName = "Chevron Gauntlet";
                // V-shaped chevron shields pointing toward the center
                SpawnRandomObstacle("Chev_N_Left", new Vector3(-3.2f, 1.15f, 5.8f), new Vector3(4.8f, 2.3f, 1.0f), 28f, wallColor, rng);
                SpawnRandomObstacle("Chev_N_Right", new Vector3(3.2f, 1.15f, 5.8f), new Vector3(4.8f, 2.3f, 1.0f), -28f, wallColor, rng);
                SpawnRandomObstacle("Chev_S_Left", new Vector3(-3.2f, 1.15f, -5.8f), new Vector3(4.8f, 2.3f, 1.0f), -28f, wallColor, rng);
                SpawnRandomObstacle("Chev_S_Right", new Vector3(3.2f, 1.15f, -5.8f), new Vector3(4.8f, 2.3f, 1.0f), 28f, wallColor, rng);
                SpawnRandomObstacle("Gauntlet_Mid_W", new Vector3(-9.5f, 1.4f, 0f), new Vector3(2.2f, 2.8f, 4.4f), 0f, pillarColor, rng);
                SpawnRandomObstacle("Gauntlet_Mid_E", new Vector3(9.5f, 1.4f, 0f), new Vector3(2.2f, 2.8f, 4.4f), 0f, pillarColor, rng);
                break;

            case 9:
                currentArenaPresetName = "Spiral Sanctuary";
                // Pinwheel / spiral blades radiating from the center
                for (int b = 0; b < 4; b++)
                {
                    float baseAng = b * 90f + (mirrorX ? 32f : -32f);
                    float rad = baseAng * Mathf.Deg2Rad;
                    Vector3 pos = new Vector3(Mathf.Cos(rad) * 6.5f, 1.25f, Mathf.Sin(rad) * 6.5f);
                    SpawnRandomObstacle($"Spiral_Blade_{b}", pos, new Vector3(6.0f, 2.5f, 1.1f), baseAng + 35f, wallColor, rng);
                }
                SpawnRandomObstacle("Spiral_Outer_NW", new Vector3(-13.0f, 1.1f, 9.0f), new Vector3(2.6f, 2.2f, 2.6f), 20f, accentColor, rng);
                SpawnRandomObstacle("Spiral_Outer_NE", new Vector3(13.0f, 1.1f, 9.0f), new Vector3(2.6f, 2.2f, 2.6f), -20f, accentColor, rng);
                SpawnRandomObstacle("Spiral_Outer_SW", new Vector3(-13.0f, 1.1f, -9.0f), new Vector3(2.6f, 2.2f, 2.6f), -20f, accentColor, rng);
                SpawnRandomObstacle("Spiral_Outer_SE", new Vector3(13.0f, 1.1f, -9.0f), new Vector3(2.6f, 2.2f, 2.6f), 20f, accentColor, rng);
                break;

            case 10:
                currentArenaPresetName = "Checkered Bunkers";
                // Grid of alternating low cover cubes and tall pillars
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
                            SpawnRandomObstacle($"Checker_Pillar_{idx}", new Vector3(xs[ix], 1.55f, zs[iz]), new Vector3(1.8f, 3.1f, 1.8f), 15f * idx, pillarColor, rng);
                        }
                        else
                        {
                            SpawnRandomObstacle($"Checker_Low_{idx}", new Vector3(xs[ix], 0.75f, zs[iz]), new Vector3(3.0f, 1.5f, 1.4f), -20f * idx, crateColor, rng);
                        }
                    }
                }
                break;

            default:
                currentArenaPresetName = "Wildcard Chaos";
                // Purely procedural high-density mixed layout
                break;
        }

        // Additional procedural random obstacles scattered in every preset (more in Wildcard Chaos)
        // Guarantees every single match has a genuinely random layout even within the same preset!
        int extraCount = (presetIndex == 11) ? rng.Next(18, 25) : rng.Next(8, 14);
        for (int i = 0; i < extraCount; i++)
        {
            float rx = -17.5f + (float)rng.NextDouble() * 35.0f;
            float rz = -16.5f + (float)rng.NextDouble() * 33.0f;

            if (!IsArenaPositionSafeFromSpawns(rx, rz, 4.6f))
            {
                continue;
            }

            int shapeKind = rng.Next(0, 5);
            float yaw = (float)rng.NextDouble() * 360f;
            Color col = (shapeKind == 0) ? pillarColor
                      : (shapeKind == 1) ? wallColor
                      : (shapeKind == 2) ? crateColor
                      : accentColor;

            if (shapeKind == 0)
            {
                // Tall Pillar
                float w = 1.4f + (float)rng.NextDouble() * 1.1f;
                float h = 2.4f + (float)rng.NextDouble() * 1.3f;
                CreateRandomObstacleBlock($"Proc_Pillar_{i}", new Vector3(rx, h * 0.5f, rz), new Vector3(w, h, w), yaw, col);
            }
            else if (shapeKind == 1)
            {
                // Angled Tactical Wall
                float len = 3.0f + (float)rng.NextDouble() * 2.8f;
                float h = 1.6f + (float)rng.NextDouble() * 1.0f;
                CreateRandomObstacleBlock($"Proc_Wall_{i}", new Vector3(rx, h * 0.5f, rz), new Vector3(len, h, 0.95f), yaw, col);
            }
            else if (shapeKind == 2)
            {
                // Low Cover Barrier (can jump or shoot over)
                float len = 2.4f + (float)rng.NextDouble() * 2.0f;
                float h = 0.95f + (float)rng.NextDouble() * 0.45f;
                CreateRandomObstacleBlock($"Proc_Barrier_{i}", new Vector3(rx, h * 0.5f, rz), new Vector3(len, h, 1.3f), yaw, col);
            }
            else if (shapeKind == 3)
            {
                // Heavy Crate Box
                float s = 1.6f + (float)rng.NextDouble() * 1.2f;
                float h = 1.2f + (float)rng.NextDouble() * 0.9f;
                CreateRandomObstacleBlock($"Proc_Crate_{i}", new Vector3(rx, h * 0.5f, rz), new Vector3(s, h, s), yaw, col);
            }
            else
            {
                // Double Stacked Crate
                float s = 1.8f + (float)rng.NextDouble() * 0.8f;
                CreateRandomObstacleBlock($"Proc_StackBase_{i}", new Vector3(rx, 0.65f, rz), new Vector3(s, 1.3f, s), yaw, crateColor);
                CreateRandomObstacleBlock($"Proc_StackTop_{i}", new Vector3(rx + 0.15f, 1.75f, rz - 0.1f), new Vector3(s * 0.72f, 0.9f, s * 0.72f), yaw + 28f, accentColor);
            }
        }

        // Sync physics colliders immediately so lasers & raycasts cannot shoot through newly spawned or rerolled cover blocks!
        Physics.SyncTransforms();
    }

    private void SpawnCrateCluster(string prefix, Vector3 baseLocalPos, Color primaryCol, Color secondaryCol, System.Random rng)
    {
        float jx = ((float)rng.NextDouble() - 0.5f) * 1.4f;
        float jz = ((float)rng.NextDouble() - 0.5f) * 1.4f;
        Vector3 p = new Vector3(baseLocalPos.x + jx, 0f, baseLocalPos.z + jz);
        if (!IsArenaPositionSafeFromSpawns(p.x, p.z, 4.2f)) return;

        float yaw = (float)rng.NextDouble() * 360f;
        CreateRandomObstacleBlock(prefix + "_Main", new Vector3(p.x, 0.85f, p.z), new Vector3(2.4f, 1.7f, 2.4f), yaw, primaryCol);
        CreateRandomObstacleBlock(prefix + "_Side", new Vector3(p.x + 1.6f, 0.55f, p.z + 0.6f), new Vector3(1.4f, 1.1f, 1.5f), yaw + 22f, secondaryCol);
        if (rng.NextDouble() > 0.35)
        {
            CreateRandomObstacleBlock(prefix + "_Top", new Vector3(p.x - 0.2f, 2.15f, p.z + 0.1f), new Vector3(1.3f, 0.9f, 1.3f), yaw - 30f, secondaryCol);
        }
    }

    private void SpawnRandomObstacle(string name, Vector3 localPos, Vector3 scale, float baseYaw, Color color, System.Random rng)
    {
        // Add subtle organic position & rotation jitter so even presets have random variations
        float jx = ((float)rng.NextDouble() - 0.5f) * 1.2f;
        float jz = ((float)rng.NextDouble() - 0.5f) * 1.2f;
        float jYaw = ((float)rng.NextDouble() - 0.5f) * 16.0f;

        Vector3 finalPos = new Vector3(localPos.x + jx, localPos.y, localPos.z + jz);
        if (!IsArenaPositionSafeFromSpawns(finalPos.x, finalPos.z, 4.0f))
        {
            return;
        }

        CreateRandomObstacleBlock(name, finalPos, scale, baseYaw + jYaw, color);
    }

    private bool IsArenaPositionSafeFromSpawns(float localX, float localZ, float minClearance)
    {
        // Plant spawns at local (0, *, -12) and Zombie spawns at local (0, *, +12)
        Vector2 pt = new Vector2(localX, localZ);
        if (Vector2.Distance(pt, new Vector2(0f, -12.0f)) < minClearance) return false;
        if (Vector2.Distance(pt, new Vector2(0f, 12.0f)) < minClearance) return false;
        // Also keep inside the 44x44 perimeter walls
        if (Mathf.Abs(localX) > 19.2f || Mathf.Abs(localZ) > 19.2f) return false;
        return true;
    }

    private void CreateRandomObstacleBlock(string name, Vector3 localPos, Vector3 scale, float yawDegrees, Color color)
    {
        Transform parent = (arenaObstaclesRoot != null) ? arenaObstaclesRoot.transform : arena3DRoot.transform;
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = localPos;
        block.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
        block.transform.localScale = scale;
        block.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(color);
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
        bgQuad.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.08f, 0.08f, 0.10f));

        GameObject fillQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fillQuad.name = "HPBar_Fill";
        fillQuad.transform.SetParent(hpRoot.transform, false);
        fillQuad.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        fillQuad.transform.localScale = new Vector3(1.24f, 0.12f, 1f);
        DestroyCollider(fillQuad);

        remoteHpBarBaseColor = opponentIsZombie ? new Color(0.82f, 0.32f, 0.98f) : new Color(0.28f, 0.92f, 0.38f);
        remoteHpBarFillRenderer = fillQuad.GetComponent<Renderer>();
        remoteHpBarFillRenderer.sharedMaterial = CreateColoredMaterial(remoteHpBarBaseColor);
        remoteHpBarFill = fillQuad.transform;
    }

    private void CreateArenaBlock(string name, Vector3 localPos, Vector3 scale, Color color)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(arena3DRoot.transform, false);
        block.transform.localPosition = localPos;
        block.transform.localScale = scale;
        block.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(color);
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

    public int GetZombieCost(ZombieType type)
    {
        switch (type)
        {
            case ZombieType.Normal: return zombieCost;
            case ZombieType.Buckethead: return bucketheadCost;
            case ZombieType.Bunny: return bunnyCost;
            default: return zombieCost;
        }
    }

    public float GetZombieMaxCooldown(ZombieType type)
    {
        switch (type)
        {
            case ZombieType.Normal: return zombieCooldown;
            case ZombieType.Buckethead: return bucketheadCooldown;
            case ZombieType.Bunny: return bunnyCooldown;
            default: return zombieCooldown;
        }
    }

    public float GetZombieCooldownRemaining(ZombieType type)
    {
        int idx = Mathf.Clamp((int)type, 0, zombieCooldownTimers.Length - 1);
        return zombieCooldownTimers[idx];
    }

    public bool CanAffordAndReadyZombie(ZombieType type)
    {
        return currentBrains >= GetZombieCost(type) && GetZombieCooldownRemaining(type) <= 0.001f;
    }

    public bool CanAffordAndReadyZombie()
    {
        return CanAffordAndReadyZombie(selectedZombieType);
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

    public void TrySelectZombie(ZombieType type = ZombieType.Normal)
    {
        if (!canPlaceZombies)
        {
            return;
        }

        isPlantSelected = false;

        if (isZombieSelected && selectedZombieType == type)
        {
            isZombieSelected = false;
            return;
        }

        if (!CanAffordAndReadyZombie(type))
        {
            return;
        }

        selectedZombieType = type;
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
        for (int i = 0; i < zombiePreviewGhosts.Length; i++)
        {
            if (zombiePreviewGhosts[i] != null && zombiePreviewGhosts[i].activeSelf)
            {
                zombiePreviewGhosts[i].SetActive(false);
            }
        }
    }

    private void UpdateTileHighlightsAndGhost()
    {
        if (tileRenderers == null)
        {
            return;
        }

        bool hoveredOccupied = hoveredRow >= 0 && hoveredRow < rows && hoveredCol >= 0 && hoveredCol < columns && placedPlants[hoveredRow, hoveredCol] != null;
        if (hoveredRow != prevHoveredRow || hoveredCol != prevHoveredCol || isZombieSelected != prevIsZombieSelected || hoveredOccupied != prevHoveredOccupied)
        {
            if (prevHoveredRow >= 0 && prevHoveredRow < rows && prevHoveredCol >= 0 && prevHoveredCol < columns)
            {
                Renderer prevRend = tileRenderers[prevHoveredRow, prevHoveredCol];
                if (prevRend != null)
                {
                    prevRend.sharedMaterial = CreateColoredMaterial(GetBaseTileColor(prevHoveredRow, prevHoveredCol));
                }
            }

            if (hoveredRow >= 0 && hoveredRow < rows && hoveredCol >= 0 && hoveredCol < columns)
            {
                Renderer curRend = tileRenderers[hoveredRow, hoveredCol];
                if (curRend != null)
                {
                    if (isZombieSelected)
                    {
                        bool allowed = IsColumnAllowedForZombie(hoveredCol);
                        Color col = allowed ? tileZombieHoverColor : new Color(0.85f, 0.2f, 0.2f, 1.0f);
                        curRend.sharedMaterial = CreateColoredMaterial(col);
                    }
                    else
                    {
                        Color col = hoveredOccupied ? tileOccupiedColor : tileHoverColor;
                        curRend.sharedMaterial = CreateColoredMaterial(col);
                    }
                }
            }

            prevHoveredRow = hoveredRow;
            prevHoveredCol = hoveredCol;
            prevIsZombieSelected = isZombieSelected;
            prevHoveredOccupied = hoveredOccupied;
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

        bool showZombieGhost = isZombieSelected
                               && CanAffordAndReadyZombie(selectedZombieType)
                               && hoveredRow >= 0
                               && hoveredCol >= 0
                               && IsColumnAllowedForZombie(hoveredCol);

        for (int i = 0; i < zombiePreviewGhosts.Length; i++)
        {
            GameObject ghost = zombiePreviewGhosts[i];
            if (ghost == null) continue;

            bool active = showZombieGhost && ((int)selectedZombieType == i);
            ghost.SetActive(active);
            if (active)
            {
                ghost.transform.position = GetTileWorldPosition(hoveredRow, hoveredCol, -0.3f);
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
            Rect actualBrainRect = canPlacePlants ? brainBankRect : new Rect(12f, 12f, 110f, 66f);
            if (actualBrainRect.Contains(guiMousePos)) return;
            for (int i = 0; i < zombieButtonRects.Length; i++)
            {
                Rect actualZombieBtn = canPlacePlants
                    ? zombieButtonRects[i]
                    : new Rect(126f + i * 142f, 12f, 138f, 66f);
                if (actualZombieBtn.Contains(guiMousePos)) return;
            }
        }

        if (hoveredRow < 0 || hoveredCol < 0)
        {
            return;
        }

        // 3. Place Zombie if Zombie is selected (Only allowed on the last 2 columns on the right)
        if (isZombieSelected && CanAffordAndReadyZombie(selectedZombieType))
        {
            if (!IsColumnAllowedForZombie(hoveredCol))
            {
                // Disallowed column - feedback: do not spend brains
                return;
            }

            int cost = GetZombieCost(selectedZombieType);
            currentBrains -= cost;
            int idx = Mathf.Clamp((int)selectedZombieType, 0, zombieCooldownTimers.Length - 1);
            zombieCooldownTimers[idx] = GetZombieMaxCooldown(selectedZombieType);
            SpawnZombieOnTile(hoveredRow, hoveredCol, selectedZombieType, notifyNetwork: true);
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

    public WallZombie SpawnZombieOnTile(int row, int col, ZombieType type = ZombieType.Normal, bool notifyNetwork = false)
    {
        if (row < 0 || row >= rows)
        {
            return null;
        }

        int clampedCol = Mathf.Clamp(col, 0, columns - 1);
        Vector3 spawnPos = GetTileWorldPosition(row, clampedCol, -0.3f);

        GameObject zombieObj = CreateZombieVisual(type, $"Zombie_{type}_{row}_{clampedCol}", isGhost: false);
        if (tilesContainer != null)
        {
            zombieObj.transform.SetParent(tilesContainer.transform, true);
        }

        WallZombie zombieComp = zombieObj.AddComponent<WallZombie>();
        zombieComp.Initialize(this, row, spawnPos, type);

        if (notifyNetwork)
        {
            OnZombiePlacedLocal?.Invoke(row, clampedCol, type);
        }

        return zombieComp;
    }

    public WallZombie SpawnZombieOnTile(int row, int col, bool notifyNetwork)
    {
        return SpawnZombieOnTile(row, col, ZombieType.Normal, notifyNetwork);
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

    private GameObject CreateZombieVisual(ZombieType type, string objectName, bool isGhost)
    {
        return GameVisuals.CreateZombie(type, objectName, isGhost, TileWidth, TileHeight);
    }

    private GameObject CreateZombieVisual(string objectName, bool isGhost)
    {
        return GameVisuals.CreateZombie(ZombieType.Normal, objectName, isGhost, TileWidth, TileHeight);
    }

    private GameObject CreatePlantVisual(PlantType type, string objectName, bool isGhost)
    {
        return GameVisuals.CreatePlant(type, objectName, isGhost, TileWidth, TileHeight);
    }

    private GameObject Create3DZombiePlaceholder(string objectName)
    {
        return Create3DZombiePlaceholder(ZombieType.Normal, objectName);
    }

    private GameObject Create3DZombiePlaceholder(ZombieType type, string objectName)
    {
        GameObject root = new GameObject(objectName);

        bool isBuckethead = (type == ZombieType.Buckethead);
        bool isBunny = (type == ZombieType.Bunny);

        Material skinMat = CreateColoredMaterial(isBunny ? new Color(0.56f, 0.80f, 0.36f) : new Color(0.50f, 0.74f, 0.30f));
        Material darkSkinMat = CreateColoredMaterial(new Color(0.38f, 0.58f, 0.22f));

        // Distinct clothing colors per zombie type
        Material coatMat;
        Material pantsMat;
        Material shoeMat;
        if (isBuckethead)
        {
            coatMat = CreateColoredMaterial(new Color(0.24f, 0.27f, 0.30f)); // Heavy charcoal work coat
            pantsMat = CreateColoredMaterial(new Color(0.18f, 0.22f, 0.26f)); // Dark slate pants
            shoeMat = CreateColoredMaterial(new Color(0.16f, 0.14f, 0.12f)); // Heavy work boots
        }
        else if (isBunny)
        {
            coatMat = CreateColoredMaterial(new Color(0.14f, 0.74f, 0.78f)); // Sporty bright teal tracksuit
            pantsMat = CreateColoredMaterial(new Color(0.16f, 0.20f, 0.34f)); // Navy track pants
            shoeMat = CreateColoredMaterial(new Color(0.98f, 0.42f, 0.14f)); // Bright orange running sneakers
        }
        else
        {
            coatMat = CreateColoredMaterial(new Color(0.42f, 0.16f, 0.58f)); // Classic purple suit
            pantsMat = CreateColoredMaterial(new Color(0.20f, 0.28f, 0.38f));
            shoeMat = CreateColoredMaterial(new Color(0.28f, 0.18f, 0.14f));
        }

        Material tieMat = CreateColoredMaterial(new Color(0.85f, 0.18f, 0.18f));
        Material eyeWhiteMat = CreateColoredMaterial(Color.white);
        Material pupilMat = CreateColoredMaterial(new Color(0.08f, 0.10f, 0.08f));

        // Legs & Shoes
        GameObject leftLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        leftLeg.name = "LeftLeg";
        leftLeg.transform.SetParent(root.transform, false);
        leftLeg.transform.localPosition = new Vector3(-0.18f, -0.55f, 0f);
        leftLeg.transform.localScale = new Vector3(0.22f, 0.35f, 0.22f);
        DestroyCollider(leftLeg);
        leftLeg.GetComponent<Renderer>().sharedMaterial = pantsMat;

        GameObject rightLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rightLeg.name = "RightLeg";
        rightLeg.transform.SetParent(root.transform, false);
        rightLeg.transform.localPosition = new Vector3(0.18f, -0.55f, 0f);
        rightLeg.transform.localScale = new Vector3(0.22f, 0.35f, 0.22f);
        DestroyCollider(rightLeg);
        rightLeg.GetComponent<Renderer>().sharedMaterial = pantsMat;

        GameObject leftShoe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftShoe.name = "LeftShoe";
        leftShoe.transform.SetParent(root.transform, false);
        leftShoe.transform.localPosition = new Vector3(-0.18f, -0.88f, 0.06f);
        leftShoe.transform.localScale = new Vector3(0.24f, 0.14f, 0.36f);
        DestroyCollider(leftShoe);
        leftShoe.GetComponent<Renderer>().sharedMaterial = shoeMat;

        GameObject rightShoe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightShoe.name = "RightShoe";
        rightShoe.transform.SetParent(root.transform, false);
        rightShoe.transform.localPosition = new Vector3(0.18f, -0.88f, 0.06f);
        rightShoe.transform.localScale = new Vector3(0.24f, 0.14f, 0.36f);
        DestroyCollider(rightShoe);
        rightShoe.GetComponent<Renderer>().sharedMaterial = shoeMat;

        // Torso
        GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
        torso.name = "Torso";
        torso.transform.SetParent(root.transform, false);
        torso.transform.localPosition = new Vector3(0f, -0.02f, 0f);
        torso.transform.localScale = isBuckethead
            ? new Vector3(0.76f, 0.75f, 0.46f)
            : (isBunny ? new Vector3(0.62f, 0.70f, 0.36f) : new Vector3(0.68f, 0.72f, 0.40f));
        DestroyCollider(torso);
        torso.GetComponent<Renderer>().sharedMaterial = coatMat;

        if (isBuckethead)
        {
            // Heavy Leather Work Belt + Silver Buckle
            GameObject belt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            belt.name = "Belt";
            belt.transform.SetParent(root.transform, false);
            belt.transform.localPosition = new Vector3(0f, -0.32f, 0f);
            belt.transform.localScale = new Vector3(0.78f, 0.10f, 0.48f);
            DestroyCollider(belt);
            belt.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.22f, 0.14f, 0.08f));

            GameObject buckle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            buckle.name = "BeltBuckle";
            buckle.transform.SetParent(root.transform, false);
            buckle.transform.localPosition = new Vector3(0f, -0.32f, 0.25f);
            buckle.transform.localScale = new Vector3(0.16f, 0.12f, 0.04f);
            DestroyCollider(buckle);
            buckle.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.85f, 0.78f, 0.28f));

            // Yellow Mustard Shirt Collar + Red Tie
            GameObject tie = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tie.name = "RedTie";
            tie.transform.SetParent(root.transform, false);
            tie.transform.localPosition = new Vector3(0f, 0.04f, 0.24f);
            tie.transform.localScale = new Vector3(0.13f, 0.44f, 0.04f);
            DestroyCollider(tie);
            tie.GetComponent<Renderer>().sharedMaterial = tieMat;
        }
        else if (isBunny)
        {
            // Coral & White Sporty Racing Stripes on Tracksuit + Fluffy White Bunny Tail on Back
            Material coralMat = CreateColoredMaterial(new Color(0.98f, 0.38f, 0.56f));
            Material whiteMat = CreateColoredMaterial(new Color(0.96f, 0.96f, 0.98f));

            GameObject chestStripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chestStripe.name = "TracksuitStripe";
            chestStripe.transform.SetParent(root.transform, false);
            chestStripe.transform.localPosition = new Vector3(0f, 0.04f, 0.19f);
            chestStripe.transform.localScale = new Vector3(0.64f, 0.16f, 0.04f);
            DestroyCollider(chestStripe);
            chestStripe.GetComponent<Renderer>().sharedMaterial = coralMat;

            GameObject zipperLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zipperLine.name = "ZipperLine";
            zipperLine.transform.SetParent(root.transform, false);
            zipperLine.transform.localPosition = new Vector3(0f, -0.02f, 0.20f);
            zipperLine.transform.localScale = new Vector3(0.06f, 0.68f, 0.04f);
            DestroyCollider(zipperLine);
            zipperLine.GetComponent<Renderer>().sharedMaterial = whiteMat;

            GameObject bunnyTail = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bunnyTail.name = "BunnyTail";
            bunnyTail.transform.SetParent(root.transform, false);
            bunnyTail.transform.localPosition = new Vector3(0f, -0.24f, -0.24f);
            bunnyTail.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            DestroyCollider(bunnyTail);
            bunnyTail.GetComponent<Renderer>().sharedMaterial = whiteMat;
        }
        else
        {
            GameObject tie = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tie.name = "RedTie";
            tie.transform.SetParent(root.transform, false);
            tie.transform.localPosition = new Vector3(0f, 0.02f, 0.21f);
            tie.transform.localScale = new Vector3(0.12f, 0.46f, 0.04f);
            DestroyCollider(tie);
            tie.GetComponent<Renderer>().sharedMaterial = tieMat;
        }

        // Outstretched Zombie Arms reaching forward (+Z)
        GameObject leftArm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        leftArm.name = "LeftArm";
        leftArm.transform.SetParent(root.transform, false);
        leftArm.transform.localPosition = new Vector3(-0.42f, 0.16f, 0.38f);
        leftArm.transform.localRotation = Quaternion.Euler(82f, 0f, 0f);
        leftArm.transform.localScale = new Vector3(0.18f, 0.38f, 0.18f);
        DestroyCollider(leftArm);
        leftArm.GetComponent<Renderer>().sharedMaterial = skinMat;

        GameObject rightArm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rightArm.name = "RightArm";
        rightArm.transform.SetParent(root.transform, false);
        rightArm.transform.localPosition = new Vector3(0.42f, 0.14f, 0.38f);
        rightArm.transform.localRotation = Quaternion.Euler(85f, 0f, 0f);
        rightArm.transform.localScale = new Vector3(0.18f, 0.38f, 0.18f);
        DestroyCollider(rightArm);
        rightArm.GetComponent<Renderer>().sharedMaterial = skinMat;

        // 3D Zombie Head & Jaw
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.62f, 0.04f);
        head.transform.localScale = new Vector3(0.66f, 0.68f, 0.64f);
        DestroyCollider(head);
        head.GetComponent<Renderer>().sharedMaterial = skinMat;

        GameObject jaw = GameObject.CreatePrimitive(PrimitiveType.Cube);
        jaw.name = "Jaw";
        jaw.transform.SetParent(root.transform, false);
        jaw.transform.localPosition = new Vector3(0f, 0.42f, 0.14f);
        jaw.transform.localScale = new Vector3(0.44f, 0.18f, 0.38f);
        DestroyCollider(jaw);
        jaw.GetComponent<Renderer>().sharedMaterial = darkSkinMat;

        // Mismatched Cartoon Zombie Eyes on front (+Z)
        GameObject leftEye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        leftEye.name = "LeftEye";
        leftEye.transform.SetParent(root.transform, false);
        leftEye.transform.localPosition = new Vector3(-0.14f, 0.66f, 0.31f);
        leftEye.transform.localScale = new Vector3(0.18f, 0.18f, 0.10f);
        DestroyCollider(leftEye);
        leftEye.GetComponent<Renderer>().sharedMaterial = eyeWhiteMat;

        GameObject leftPupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        leftPupil.name = "LeftPupil";
        leftPupil.transform.SetParent(root.transform, false);
        leftPupil.transform.localPosition = new Vector3(-0.14f, 0.66f, 0.35f);
        leftPupil.transform.localScale = new Vector3(0.08f, 0.08f, 0.06f);
        DestroyCollider(leftPupil);
        leftPupil.GetComponent<Renderer>().sharedMaterial = pupilMat;

        GameObject rightEye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rightEye.name = "RightEye";
        rightEye.transform.SetParent(root.transform, false);
        rightEye.transform.localPosition = new Vector3(0.14f, 0.65f, 0.31f);
        rightEye.transform.localScale = new Vector3(0.13f, 0.13f, 0.09f);
        DestroyCollider(rightEye);
        rightEye.GetComponent<Renderer>().sharedMaterial = eyeWhiteMat;

        GameObject rightPupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rightPupil.name = "RightPupil";
        rightPupil.transform.SetParent(root.transform, false);
        rightPupil.transform.localPosition = new Vector3(0.14f, 0.65f, 0.34f);
        rightPupil.transform.localScale = new Vector3(0.06f, 0.06f, 0.05f);
        DestroyCollider(rightPupil);
        rightPupil.GetComponent<Renderer>().sharedMaterial = pupilMat;

        // Headgear & Special 3D Visuals per Zombie Type
        if (isBuckethead)
        {
            Material bucketSilver = CreateColoredMaterial(new Color(0.74f, 0.78f, 0.84f));
            Material bucketDark = CreateColoredMaterial(new Color(0.48f, 0.52f, 0.58f));
            Material paintSmudgeMat = CreateColoredMaterial(new Color(0.86f, 0.18f, 0.16f));

            GameObject bucket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bucket.name = "MetalBucket";
            bucket.transform.SetParent(root.transform, false);
            bucket.transform.localPosition = new Vector3(0f, 1.00f, 0.02f);
            bucket.transform.localRotation = Quaternion.Euler(-10f, 0f, 8f);
            bucket.transform.localScale = new Vector3(0.70f, 0.32f, 0.70f);
            DestroyCollider(bucket);
            bucket.GetComponent<Renderer>().sharedMaterial = bucketSilver;

            GameObject bucketRim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bucketRim.name = "BucketRim";
            bucketRim.transform.SetParent(bucket.transform, false);
            bucketRim.transform.localPosition = new Vector3(0f, -0.82f, 0f);
            bucketRim.transform.localScale = new Vector3(1.08f, 0.14f, 1.08f);
            DestroyCollider(bucketRim);
            bucketRim.GetComponent<Renderer>().sharedMaterial = bucketDark;

            GameObject redSplatter = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            redSplatter.name = "BucketPaintSmudge";
            redSplatter.transform.SetParent(bucket.transform, false);
            redSplatter.transform.localPosition = new Vector3(0.24f, 0.10f, 0.45f);
            redSplatter.transform.localScale = new Vector3(0.28f, 0.38f, 0.12f);
            DestroyCollider(redSplatter);
            redSplatter.GetComponent<Renderer>().sharedMaterial = paintSmudgeMat;

            // Parry Shield Aura (shown when Buckethead activates his 1s Parry stance)
            GameObject parryShield = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            parryShield.name = "ParryShieldVisual";
            parryShield.transform.SetParent(root.transform, false);
            parryShield.transform.localPosition = new Vector3(0f, 0.25f, 0.45f);
            parryShield.transform.localScale = new Vector3(1.45f, 1.65f, 0.35f);
            DestroyCollider(parryShield);
            parryShield.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.35f, 0.92f, 1.0f));
            parryShield.SetActive(false);
            remoteParryShieldObj = parryShield;
        }
        else if (isBunny)
        {
            Material earWhiteMat = CreateColoredMaterial(new Color(0.96f, 0.96f, 0.99f));
            Material earPinkMat = CreateColoredMaterial(new Color(0.98f, 0.56f, 0.72f));
            Material headbandMat = CreateColoredMaterial(new Color(0.98f, 0.32f, 0.50f));

            // Sporty Sweatband
            GameObject sweatband = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sweatband.name = "BunnySweatband";
            sweatband.transform.SetParent(root.transform, false);
            sweatband.transform.localPosition = new Vector3(0f, 0.80f, 0.04f);
            sweatband.transform.localScale = new Vector3(0.66f, 0.06f, 0.65f);
            DestroyCollider(sweatband);
            sweatband.GetComponent<Renderer>().sharedMaterial = headbandMat;

            // Tall Left Bunny Ear (White Outer + Pink Inner)
            GameObject leftEar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            leftEar.name = "LeftBunnyEar";
            leftEar.transform.SetParent(root.transform, false);
            leftEar.transform.localPosition = new Vector3(-0.18f, 1.24f, 0.02f);
            leftEar.transform.localRotation = Quaternion.Euler(-6f, 0f, 10f);
            leftEar.transform.localScale = new Vector3(0.18f, 0.36f, 0.10f);
            DestroyCollider(leftEar);
            leftEar.GetComponent<Renderer>().sharedMaterial = earWhiteMat;

            GameObject leftEarPink = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            leftEarPink.name = "LeftBunnyEarInner";
            leftEarPink.transform.SetParent(leftEar.transform, false);
            leftEarPink.transform.localPosition = new Vector3(0f, 0f, 0.32f);
            leftEarPink.transform.localScale = new Vector3(0.58f, 0.76f, 0.55f);
            DestroyCollider(leftEarPink);
            leftEarPink.GetComponent<Renderer>().sharedMaterial = earPinkMat;

            // Tall Right Bunny Ear (White Outer + Pink Inner)
            GameObject rightEar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rightEar.name = "RightBunnyEar";
            rightEar.transform.SetParent(root.transform, false);
            rightEar.transform.localPosition = new Vector3(0.18f, 1.24f, 0.02f);
            rightEar.transform.localRotation = Quaternion.Euler(8f, 0f, -12f);
            rightEar.transform.localScale = new Vector3(0.18f, 0.36f, 0.10f);
            DestroyCollider(rightEar);
            rightEar.GetComponent<Renderer>().sharedMaterial = earWhiteMat;

            GameObject rightEarPink = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rightEarPink.name = "RightBunnyEarInner";
            rightEarPink.transform.SetParent(rightEar.transform, false);
            rightEarPink.transform.localPosition = new Vector3(0f, 0f, 0.32f);
            rightEarPink.transform.localScale = new Vector3(0.58f, 0.76f, 0.55f);
            DestroyCollider(rightEarPink);
            rightEarPink.GetComponent<Renderer>().sharedMaterial = earPinkMat;
        }

        return root;
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
            stem.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.2f, 0.7f, 0.45f));

            GameObject petals = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            petals.name = "Petals";
            petals.transform.SetParent(root.transform, false);
            petals.transform.localPosition = new Vector3(0f, 0.25f, -0.04f);
            petals.transform.localScale = new Vector3(1.15f, 1.15f, 0.40f);
            DestroyCollider(petals);
            petals.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.45f, 0.85f, 1.0f));

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.25f, 0.08f);
            head.transform.localScale = new Vector3(0.78f, 0.78f, 0.50f);
            DestroyCollider(head);
            head.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(1.0f, 0.95f, 0.4f));
        }
        else if (type == PlantType.Rose || type == PlantType.Roze)
        {
            GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            stem.transform.SetParent(root.transform, false);
            stem.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            stem.transform.localScale = new Vector3(0.38f, 0.50f, 0.38f);
            DestroyCollider(stem);
            stem.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.15f, 0.65f, 0.25f));

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            head.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);
            DestroyCollider(head);
            head.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.95f, 0.18f, 0.32f));

            GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel";
            barrel.transform.SetParent(root.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 0.28f, 0.52f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.42f, 0.30f, 0.42f);
            DestroyCollider(barrel);
            barrel.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.8f, 0.12f, 0.25f));
        }
        else
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            body.transform.localScale = new Vector3(1.25f, 1.40f, 1.20f);
            DestroyCollider(body);
            body.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.68f, 0.58f, 0.2f));

            GameObject spikes1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spikes1.name = "Spikes";
            spikes1.transform.SetParent(root.transform, false);
            spikes1.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            spikes1.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            spikes1.transform.localScale = new Vector3(1.05f, 1.05f, 1.05f);
            DestroyCollider(spikes1);
            spikes1.GetComponent<Renderer>().sharedMaterial = CreateColoredMaterial(new Color(0.82f, 0.72f, 0.25f));
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
        Color32 key = color;
        if (cachedMaterials.TryGetValue(key, out Material existing) && existing != null)
        {
            return existing;
        }

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
        cachedMaterials[key] = mat;
        return mat;
    }

    private static GUIStyle s_ClashStyle;
    private static GUIStyle s_TimerStyle;
    private static GUIStyle s_BankStyle;
    private static GUIStyle s_ArenaHudStyle;
    private static GUIStyle s_HitStyle;
    private static GUIStyle s_StunStyle;
    private static GUIStyle s_OppStunStyle;
    private static GUIStyle s_WinStyle;
    private static GUIStyle s_HpTextStyle;
    private static GUIStyle s_ChargeBarTextStyle;
    private static GUIStyle s_PlantBtnStyle;
    private static GUIStyle s_ZombieBtnStyle;
    private static GUIStyle s_GameOverTitleStyle;
    private static GUIStyle s_GameOverDescStyle;
    private static GUIStyle s_GameOverBtnStyle;

    private static void EnsureArenaGuiStyles()
    {
        if (s_ClashStyle != null) return;

        s_ClashStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        s_ClashStyle.normal.textColor = new Color(1f, 0.9f, 0.3f);

        s_TimerStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        s_BankStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        s_ArenaHudStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        s_ArenaHudStyle.normal.textColor = Color.white;

        s_HitStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        s_HitStyle.normal.textColor = new Color(1.0f, 0.95f, 0.35f);

        s_StunStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        s_StunStyle.normal.textColor = new Color(1.0f, 0.85f, 0.25f);

        s_OppStunStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        s_OppStunStyle.normal.textColor = new Color(0.45f, 1.0f, 0.55f);

        s_WinStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 46,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        s_HpTextStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        s_HpTextStyle.normal.textColor = Color.white;

        s_ChargeBarTextStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        s_ChargeBarTextStyle.normal.textColor = Color.white;

        s_PlantBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        s_ZombieBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        s_GameOverTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        s_GameOverDescStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        s_GameOverBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !gameplayActive)
        {
            return;
        }
        EnsureArenaGuiStyles();

        Color prevBg = GUI.backgroundColor;

        // If zooming into clash, show clash alert banner
        if (isZoomingIntoClash)
        {
            GUI.backgroundColor = new Color(0.15f, 0.05f, 0.05f, 0.92f);
            GUI.Box(new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.5f - 45f, 520f, 90f),
                "ZOMBIE TOUCHED PLANT!\n2D World Paused - Entering 3D 1v1 Arena...", s_ClashStyle);
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
        // Dynamically position the Defense Timer so it NEVER overlaps the seed buttons on the left or the Role banner on the right
        float minTimerX = canPlacePlants
            ? (plantButtonRects[2].x + plantButtonRects[2].width + 16f)
            : (canPlaceZombies ? (126f + 3f * 142f + 12f) : 285f);
        float maxTimerX = sw - 245f - 210f; // 10px buffer before the compact 220px top-right role banner
        float timerX = (maxTimerX > minTimerX) ? (minTimerX + (maxTimerX - minTimerX) * 0.5f) : minTimerX;
        Rect timerRect = new Rect(timerX, 12f, 210f, 66f);

        int mins = Mathf.FloorToInt(matchTimeRemaining / 60f);
        int secs = Mathf.FloorToInt(matchTimeRemaining % 60f);
        string timeStr = $"{mins:D2}:{secs:D2}";

        if (matchTimeRemaining <= 30f)
        {
            s_TimerStyle.normal.textColor = new Color(1.0f, 0.35f, 0.35f);
            GUI.backgroundColor = new Color(0.35f, 0.08f, 0.08f, 0.95f);
        }
        else if (matchTimeRemaining <= 60f)
        {
            s_TimerStyle.normal.textColor = new Color(1.0f, 0.95f, 0.35f);
            GUI.backgroundColor = new Color(0.28f, 0.22f, 0.08f, 0.95f);
        }
        else
        {
            s_TimerStyle.normal.textColor = new Color(0.9f, 0.95f, 1.0f);
            GUI.backgroundColor = new Color(0.10f, 0.16f, 0.24f, 0.92f);
        }

        GUI.Box(timerRect, $"DEFENSE TIMER\n{timeStr}\nPlants Win at 00:00", s_TimerStyle);
        GUI.backgroundColor = prevBg;

        // 1. MOON BANK DISPLAY + 3 PLANT SEED BUTTONS (Only shown for Plant Player or None)
        if (canPlacePlants)
        {
            s_BankStyle.normal.textColor = new Color(1.0f, 0.95f, 0.4f);
            GUI.backgroundColor = new Color(0.12f, 0.18f, 0.32f, 0.95f);
            GUI.Box(moonBankRect, $"MOONS\n{currentMoons}", s_BankStyle);

            DrawPlantSeedButton(0, PlantType.Moonflower, "Moonflower [1]", moonflowerCost, new Color(0.4f, 0.85f, 1.0f));
            DrawPlantSeedButton(1, PlantType.Rose, "Rose [2]", roseCost, new Color(1.0f, 0.4f, 0.5f));
            DrawPlantSeedButton(2, PlantType.Durian, "Durian [3]", durianCost, new Color(0.9f, 0.78f, 0.3f));
        }

        // 2. ZOMBIE PLAYER: BRAINS BANK + 3 ZOMBIE SEED BUTTONS (Only shown for Zombie Player or None)
        if (canPlaceZombies)
        {
            // Position on the far left if Plant UI is hidden; if both are active, place on second tier
            Rect actualBrainRect = canPlacePlants ? brainBankRect : new Rect(12f, 12f, 110f, 66f);

            s_BankStyle.normal.textColor = new Color(0.95f, 0.55f, 1.0f);
            GUI.backgroundColor = new Color(0.24f, 0.10f, 0.28f, 0.95f);
            GUI.Box(actualBrainRect, $"BRAINS\n{currentBrains}", s_BankStyle);

            DrawZombieSeedButton(0, ZombieType.Normal, "Zombie [4]", zombieCost, new Color(0.68f, 0.42f, 0.88f));
            DrawZombieSeedButton(1, ZombieType.Buckethead, "Buckethead [5]", bucketheadCost, new Color(0.62f, 0.72f, 0.84f));
            DrawZombieSeedButton(2, ZombieType.Bunny, "Bunny Speed [6]", bunnyCost, new Color(0.22f, 0.86f, 0.84f));
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
        ZombieType activeZombie = (clashingZombie != null) ? clashingZombie.zombieType : clashingZombieType;

        string zombieDisplayName = (activeZombie == ZombieType.Buckethead)
            ? "BUCKETHEAD ZOMBIE"
            : (activeZombie == ZombieType.Bunny ? "BUNNY ZOMBIE" : "NORMAL ZOMBIE");

        string myTitle = isZombie ? $"YOU ({zombieDisplayName})" : $"YOU ({activePlant.ToString().ToUpper()})";
        string oppTitle = isZombie ? $"OPPONENT ({activePlant.ToString().ToUpper()})" : $"OPPONENT ({zombieDisplayName})";

        string abilityHint;
        string primaryAbilityTitle;
        string secondaryAbilityTitle;

        if (isZombie)
        {
            if (activeZombie == ZombieType.Buckethead)
            {
                abilityHint = "[L-Click] Heavy Slash  •  [R-Click / E / Q] Iron Bucket Parry (1s Block -> 3s Speed Boost, 5s CD)";
                primaryAbilityTitle = "Heavy Slash [LMB]\n30 DMG";
                secondaryAbilityTitle = isBucketParryingLocal
                    ? $"PARRY ACTIVE!\n{bucketParryTimer:F1}s"
                    : "Bucket Parry [RMB/E]";
            }
            else if (activeZombie == ZombieType.Bunny)
            {
                abilityHint = "[L-Click / Q / Ctrl] Bunny Slide  •  [R-Click / E] Quick Slash  •  [F / Slide+RMB] Feral Dive Lunge";
                primaryAbilityTitle = "Bunny Claw [RMB/E]\n22 DMG";
                secondaryAbilityTitle = isBunnySlidingLocal
                    ? $"SLIDING!\n{bunnySlideTimer:F1}s"
                    : "Bunny Slide [LMB/Q]";
            }
            else
            {
                abilityHint = "[L-Click / E] Claw Slash  •  [R-Click / Q] Feral Dive Lunge (Fast Pounce)";
                primaryAbilityTitle = "Claw Slash [LMB/E]\n25 DMG";
                secondaryAbilityTitle = isFeralLungingLocal
                    ? $"DIVING!\n{feralLungeTimer:F1}s"
                    : "Feral Lunge [RMB/Q]\n38 DMG";
            }
        }
        else if (activePlant == PlantType.Moonflower)
        {
            abilityHint = "[L-Click] Solar Bolt (18 DMG)  •  [R-Click / E] Charged Moon Beam (65 DMG Laser)";
            primaryAbilityTitle = "Solar Bolt [LMB]\n18 DMG";
            secondaryAbilityTitle = "Moon Beam [RMB/E]\n65 DMG";
        }
        else if (activePlant == PlantType.Rose || activePlant == PlantType.Roze)
        {
            abilityHint = "[L-Click] Hitscan Thorn (25 DMG)  •  [R-Click / E] Heavy Thorn Volley (45 DMG)";
            primaryAbilityTitle = "Thorn Shot [LMB]\n25 DMG";
            secondaryAbilityTitle = "Thorn Volley [RMB/E]\n45 DMG";
        }
        else
        {
            abilityHint = "[L-Click] Spike Bash (28 DMG)  •  [R-Click / E] Durian Roll (50 DMG + 2.0s Stun)";
            primaryAbilityTitle = "Spike Bash [LMB]\n28 DMG";
            secondaryAbilityTitle = "Durian Roll [RMB/E]\n50 DMG + Stun";
        }

        // 1. Top-Center Instructions Banner
        GUI.backgroundColor = new Color(0.10f, 0.14f, 0.22f, 0.88f);
        GUI.Box(new Rect(cx - 360f, 12f, 720f, 54f),
            $"3D 1v1 ARENA ({currentArenaPresetName}) • {abilityHint}\nWASD: Move | Space: Jump | R: Reroll Layout | Hits: {arenaHitsLanded} | Host: N/M = Win",
            s_ArenaHudStyle);

        // 2. Top-Left & Top-Right HP Bars for Local Player and Opponent
        Color myBarCol = isZombie ? new Color(0.78f, 0.32f, 0.96f) : new Color(0.25f, 0.90f, 0.38f);
        Color oppBarCol = isZombie ? new Color(0.25f, 0.90f, 0.38f) : new Color(0.78f, 0.32f, 0.96f);
        if (remoteHitFlashTimer > 0f)
        {
            oppBarCol = new Color(1.0f, 0.95f, 0.35f);
        }

        DrawArenaHPBar(new Rect(18f, 14f, 255f, 48f), myTitle, localArenaCurrentHP, localArenaMaxHP, myBarCol);
        DrawArenaHPBar(new Rect(sw - 273f, 14f, 255f, 48f), oppTitle, remoteArenaCurrentHP, remoteArenaMaxHP, oppBarCol);

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
            GUI.backgroundColor = new Color(0.35f, 0.06f, 0.06f, 0.92f);
            GUI.Box(new Rect(cx - 210f, cy + 26f, 420f, 32f),
                $"★ {arenaLastHitLabel} (Hits: {arenaHitsLanded}) ★", s_HitStyle);
        }
        GUI.color = prevColor;

        // 5. Ability Charge / Roll / Parry / Slide Progress Bar below the Center Cursor
        PlayerController pc = GetPrimaryPlayerController();
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
        else if (isBucketParryingLocal)
        {
            float pct = Mathf.Clamp01(bucketParryTimer / Mathf.Max(0.01f, bucketParryDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, $"🛡️ IRON BUCKET PARRY ACTIVE ({bucketParryTimer:F1}s) 🛡️", new Color(0.35f, 0.95f, 1.0f));
        }
        else if (isBunnySlidingLocal)
        {
            float pct = Mathf.Clamp01(bunnySlideTimer / Mathf.Max(0.01f, bunnySlideDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, $"🐇 BUNNY SLIDING! LOW HITBOX + 1.48x SPEED ({bunnySlideTimer:F1}s)", new Color(0.25f, 0.95f, 0.88f));
        }
        else if (isFeralLungingLocal)
        {
            float pct = Mathf.Clamp01(feralLungeTimer / Mathf.Max(0.01f, feralLungeDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, $"🧟 FERAL DIVE LUNGE! POUNCING AT PLANT ({feralLungeTimer:F1}s)", new Color(0.92f, 0.35f, 1.0f));
        }

        // Show Buckethead Parry Speed Boost active timer
        if (pc != null && pc.parrySpeedBonusTimer > 0f)
        {
            float pct = Mathf.Clamp01(pc.parrySpeedBonusTimer / Mathf.Max(0.01f, bucketParrySpeedDuration));
            int bonusPct = Mathf.RoundToInt((pc.parrySpeedBonusMultiplier - 1.0f) * 100f);
            DrawCenterChargeBar(cx, cy - 62f, pct, $"⚡ PARRY SPEED BOOST +{bonusPct}% ({pc.parrySpeedBonusTimer:F1}s) ⚡", new Color(1.0f, 0.85f, 0.25f));
        }

        // 6. Bottom-Center Ability Cooldown Boxes (Primary & Secondary Abilities)
        DrawArenaAbilityCooldownBox(
            new Rect(cx - 178f, sh - 78f, 170f, 60f),
            primaryAbilityTitle,
            arenaAbilityCooldownTimer,
            arenaPrimaryMaxCooldown,
            new Color(0.22f, 0.72f, 0.95f)
        );
        DrawArenaAbilityCooldownBox(
            new Rect(cx + 8f, sh - 78f, 170f, 60f),
            secondaryAbilityTitle,
            arenaSecondaryCooldownTimer,
            arenaSecondaryMaxCooldown,
            new Color(0.88f, 0.48f, 0.98f)
        );

        // 7. Immobilized Banner if Zombie Player was hit by Durian Roll
        if (pc != null && pc.IsImmobilized)
        {
            GUI.backgroundColor = new Color(0.28f, 0.08f, 0.05f, 0.94f);
            GUI.Box(new Rect(cx - 250f, cy - 95f, 500f, 46f),
                $"⚡ IMMOBILIZED BY DURIAN! ({pc.immobilizedTimer:F1}s) — Movement & Attacks Locked ⚡", s_StunStyle);
        }
        else if (remoteImmobilizedTimer > 0f)
        {
            GUI.backgroundColor = new Color(0.08f, 0.24f, 0.12f, 0.90f);
            GUI.Box(new Rect(cx - 210f, cy + 64f, 420f, 30f),
                $"ZOMBIE IMMOBILIZED & UNABLE TO ATTACK ({remoteImmobilizedTimer:F1}s)", s_OppStunStyle);
        }

        // 8. Arena Resolution Banner (N / M or 0 HP)
        if (arenaResolving)
        {
            s_WinStyle.normal.textColor = arenaZombieWon ? new Color(0.8f, 0.45f, 1f) : new Color(0.5f, 1f, 0.45f);
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.85f);
            float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.04f;
            float w = 560f * pulse, h = 130f * pulse;
            GUI.Box(new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h),
                arenaZombieWon ? "ZOMBIE WON!" : "PLANT WON!", s_WinStyle);
        }

        GUI.backgroundColor = prevBg;
    }

    private void DrawArenaAbilityCooldownBox(Rect rect, string title, float cdRemaining, float cdMax, Color readyTint)
    {
        Color prevBg = GUI.backgroundColor;
        Color prevColor = GUI.color;

        bool isReady = cdRemaining <= 0.005f;
        GUI.backgroundColor = isReady ? readyTint : new Color(0.25f, 0.25f, 0.28f, 0.95f);

        string status = isReady ? "READY" : $"CD: {cdRemaining:F1}s";
        GUI.Box(rect, $"{title}\n{status}", s_PlantBtnStyle);

        // PvZ-style vertical recharge curtain sweep
        if (!isReady && cdMax > 0.01f)
        {
            float ratio = Mathf.Clamp01(cdRemaining / cdMax);
            GUI.color = new Color(0.02f, 0.02f, 0.05f, 0.65f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, rect.height * ratio), Texture2D.whiteTexture);
            GUI.color = prevColor;
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
        GUI.Label(new Rect(rect.x, rect.y + 3f, rect.width, 20f), $"{label}  •  HP: {currentHP} / {maxHP}", s_HpTextStyle);
    }

    private void DrawCenterChargeBar(float cx, float y, float pct, string text, Color fillCol)
    {
        Color prevColor = GUI.color;
        Rect bgRect = new Rect(cx - 140f, y, 280f, 20f);
        GUI.color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
        GUI.DrawTexture(bgRect, Texture2D.whiteTexture);

        Rect fillRect = new Rect(bgRect.x + 2f, bgRect.y + 2f, (bgRect.width - 4f) * Mathf.Clamp01(pct), bgRect.height - 4f);
        GUI.color = fillCol;
        GUI.DrawTexture(fillRect, Texture2D.whiteTexture);

        GUI.color = prevColor;
        GUI.Label(bgRect, text, s_ChargeBarTextStyle);
    }

    private void DrawPlantSeedButton(int index, PlantType type, string title, int cost, Color readyTint)
    {
        Rect rect = plantButtonRects[index];
        float cd = GetPlantCooldownRemaining(type);
        float maxCd = GetPlantMaxCooldown(type);
        bool enoughMoons = currentMoons >= cost;
        bool isReady = cd <= 0.001f && enoughMoons;
        bool isSelected = isPlantSelected && selectedPlantType == type;

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
            statusLine = $"RECHARGING {cd:F1}s";
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
        if (GUI.Button(rect, label, s_PlantBtnStyle))
        {
            TrySelectPlant(type);
        }

        // PvZ-style vertical dark recharge overlay curtain sweeping top-to-bottom as cooldown recharges
        if (cd > 0.001f && maxCd > 0.01f)
        {
            Color prevCol = GUI.color;
            float cdRatio = Mathf.Clamp01(cd / maxCd);
            GUI.color = new Color(0.02f, 0.02f, 0.05f, 0.62f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, rect.height * cdRatio), Texture2D.whiteTexture);
            GUI.color = prevCol;
        }
    }

    private void DrawZombieSeedButton(int index, ZombieType type, string title, int cost, Color readyTint)
    {
        Rect targetRect = canPlacePlants
            ? zombieButtonRects[index]
            : new Rect(126f + index * 142f, 12f, 138f, 66f);

        float cd = GetZombieCooldownRemaining(type);
        float maxCd = GetZombieMaxCooldown(type);
        bool enoughBrains = currentBrains >= cost;
        bool isReady = cd <= 0.001f && enoughBrains;
        bool isSelected = isZombieSelected && selectedZombieType == type;

        if (isSelected)
        {
            GUI.backgroundColor = new Color(0.85f, 0.3f, 1.0f);
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
            statusLine = $"RECHARGING {cd:F1}s";
        }
        else if (!enoughBrains)
        {
            statusLine = $"Need {cost} 🧠";
        }
        else if (isSelected)
        {
            statusLine = "SELECTED ✓";
        }
        else
        {
            statusLine = "READY";
        }

        string label = $"{title}\n{cost} 🧠 • {statusLine}";
        if (GUI.Button(targetRect, label, s_ZombieBtnStyle))
        {
            TrySelectZombie(type);
        }

        // PvZ-style vertical dark recharge overlay curtain sweeping top-to-bottom as cooldown recharges
        if (cd > 0.001f && maxCd > 0.01f)
        {
            Color prevCol = GUI.color;
            float cdRatio = Mathf.Clamp01(cd / maxCd);
            GUI.color = new Color(0.02f, 0.02f, 0.05f, 0.62f);
            GUI.DrawTexture(new Rect(targetRect.x, targetRect.y, targetRect.width, targetRect.height * cdRatio), Texture2D.whiteTexture);
            GUI.color = prevCol;
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

        GUIStyle titleStyle = s_GameOverTitleStyle;
        GUIStyle descStyle = s_GameOverDescStyle;
        GUIStyle btnStyle = s_GameOverBtnStyle;

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
