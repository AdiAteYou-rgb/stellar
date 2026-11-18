using System.Collections.Generic;
using UnityEngine;
using Stellar.Combat;

/// <summary>
/// Manages the 3D 1v1 Arena combat lifecycle: clash camera zoom, 3D environment & opponent avatar,
/// Plant & Zombie arena abilities, raycast/melee hit detection, pose/combat sync, and resolution.
/// </summary>
public class ArenaCombatController : MonoBehaviour
{
    private GridManager grid;
    private readonly ArenaSparringBot sparringBot = new ArenaSparringBot();
    private readonly ArenaHudRenderer hudRenderer = new ArenaHudRenderer();

    // Clash & zoom state
    private bool isZoomingIntoClash = false;
    private bool isIn3DArena = false;
    private float clashZoomTimer = 0f;
    private Vector3 clashZoomStartCamPos;
    private Vector3 clashZoomTargetCamPos;
    private float clashZoomStartOrthoSize;
    private WallZombie clashingZombie;
    private PlaceholderPlant clashingPlant;
    private int clashingPlantRow = -1;
    private int clashingPlantCol = -1;
    private PlantType clashingPlantType = PlantType.Rose;
    private ZombieType clashingZombieType = ZombieType.Normal;
    private float clashRetrySendTimer = 0f;
    private bool localInitiatedCurrentClash = false;

    // 3D Arena objects & layout
    private GameObject arena3DRoot;
    private GameObject arenaObstaclesRoot;
    private Renderer arenaFloorRenderer;
    private int currentArenaLayoutSeed = 0;
    private int currentClashId = 0;
    private readonly HashSet<int> activeClashIds = new HashSet<int>();
    private readonly HashSet<int> resolvedClashIds = new HashSet<int>();
    private float lastArenaExitUnscaledTime = -999f;
    private int lastResolvedPlantRow = -1;
    private int lastResolvedPlantCol = -1;
    private string currentArenaPresetName = "Random";
    private string currentArenaOpponentName = "OPPONENT";

    // Remote opponent state
    private GameObject remoteArenaAvatar;
    private CapsuleCollider remoteBodyHitbox;
    private SphereCollider remoteHeadHitbox;
    private Transform remoteZombieVisualRoot;
    private Transform remotePlantVisualRoot;
    private GameObject remoteParryShieldObj;
    private bool remoteIsBunnySliding = false;
    private bool remoteIsBucketParrying = false;
    private bool remoteHasParrySpeedBuff = false;
    private bool remoteIsFeralLunging = false;
    private bool remoteIsDurianRolling = false;
    private float remoteDurianRollAngle = 0f;
    private Vector3 remoteArenaTargetPos;
    private float remoteArenaTargetYaw;
    private bool hasRemoteArenaPose = false;
    private float arenaPoseSendTimer = 0f;

    // HP, Hitboxes, Abilities & Hit Indicator State
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
    private float localAbilityLockTimer = 0f;
    private float remoteAbilityLockedTimer = 0f;

    private float arenaAbilityCooldownTimer = 0f;
    private float arenaPrimaryMaxCooldown = 1.0f;
    private float arenaSecondaryCooldownTimer = 0f;
    private float arenaSecondaryMaxCooldown = 5.0f;

    // Ability runtime states
    private bool isChargingRoseThorn = false;
    private float roseThornChargeTimer = 0f;
    private bool isDurianRollingLocal = false;
    private float durianRollTimer = 0f;
    private GameObject localDurianRollVisual;
    private GameObject localDurianShadowVisual;

    private bool isBucketParryingLocal = false;
    private float bucketParryTimer = 0f;
    private GameObject localBucketParryVisual;

    private bool isBunnySlidingLocal = false;
    private float bunnySlideTimer = 0f;
    private GameObject localBunnySlideVisual;

    private bool isFeralLungingLocal = false;
    private float feralLungeTimer = 0f;
    private bool feralLungeHitDealt = false;
    private GameObject localFeralLungeVisual;

    private float hazmatGunRecoilTimer = 0f;
    private GameObject localHazmatPovVisual;

    private bool arenaResolving = false;
    private bool arenaZombieWon = false;
    private float arenaResolveTimer = 0f;

    public bool IsIn3DArena => isIn3DArena;
    public bool IsZoomingIntoClash => isZoomingIntoClash;

    public void Initialize(GridManager manager)
    {
        grid = manager;
    }

    public void ResetSessionState()
    {
        clashingZombie = null;
        clashingPlant = null;
        clashingPlantRow = -1;
        clashingPlantCol = -1;
        currentClashId = 0;
        activeClashIds.Clear();
        resolvedClashIds.Clear();
        lastResolvedPlantRow = -1;
        lastResolvedPlantCol = -1;
        lastArenaExitUnscaledTime = -999f;
        isZoomingIntoClash = false;
        isIn3DArena = false;
        arenaResolving = false;
        sparringBot.Reset();
    }

    // ==================== CLASH TRIGGER & ZOOM ====================

    public void TriggerZombiePlantClash(WallZombie zombie, PlaceholderPlant plant, PlantType fallbackType, bool notifyNetwork = true)
    {
        if (grid == null || grid.is2DWorldPaused || isZoomingIntoClash || isIn3DArena)
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
            currentClashId = Random.Range(100000, 999999999);
            activeClashIds.Clear();
            activeClashIds.Add(currentClashId);
            localInitiatedCurrentClash = true;
            clashRetrySendTimer = 0.22f;
            currentArenaLayoutSeed = Random.Range(1000, 9999999);
            grid.NotifyClashStarted(r, c, pType, grid.GetEffectiveLocalRole(), zType, currentClashId);
            grid.NotifyArenaSeed(currentArenaLayoutSeed);
        }
        else
        {
            localInitiatedCurrentClash = false;
            if (currentArenaLayoutSeed == 0)
            {
                currentArenaLayoutSeed = Random.Range(1000, 9999999);
            }
        }

        grid.is2DWorldPaused = true;
        grid.isPlantSelected = false;
        grid.isZombieSelected = false;
        grid.HideAllPreviewGhosts();

        clashingZombie = zombie;
        clashingPlant = plant;
        if (zombie != null)
        {
            zombie.MarkClashResolved();
        }

        Camera cam = grid.GetTargetCamera();
        if (cam != null)
        {
            clashZoomStartCamPos = cam.transform.position;
            clashZoomStartOrthoSize = cam.orthographicSize;

            Vector3 zPos = zombie != null ? zombie.transform.position : (plant != null ? plant.transform.position : transform.position);
            Vector3 pPos = plant != null ? plant.transform.position : zPos;
            Vector3 midpoint = (zPos + pPos) * 0.5f;
            clashZoomTargetCamPos = new Vector3(midpoint.x, midpoint.y, clashZoomStartCamPos.z);
        }

        clashZoomTimer = 0f;
        isZoomingIntoClash = true;
    }

    public void TriggerRemoteClash(
        int row,
        int col,
        PlantType plantType,
        MultiplayerMenuManager.PlayerRole remoteInitiatorRole = MultiplayerMenuManager.PlayerRole.None,
        ZombieType zombieType = ZombieType.Normal,
        int clashId = 0)
    {
        if (grid == null) return;
        if (clashId != 0 && resolvedClashIds.Contains(clashId)) return;
        if (clashId == 0 && (Time.unscaledTime - lastArenaExitUnscaledTime) < 0.75f && row == lastResolvedPlantRow && col == lastResolvedPlantCol)
        {
            return;
        }

        grid.gameplayActive = true;

        if (remoteInitiatorRole == MultiplayerMenuManager.PlayerRole.Zombie)
        {
            grid.SetLocalRole(MultiplayerMenuManager.PlayerRole.Plant);
        }
        else if (remoteInitiatorRole == MultiplayerMenuManager.PlayerRole.Plant)
        {
            grid.SetLocalRole(MultiplayerMenuManager.PlayerRole.Zombie);
        }

        PlaceholderPlant plant = grid.GetPlacedPlantAt(row, col);
        Vector3 refPos = plant != null ? plant.transform.position : grid.GetTileWorldPosition(row, col, 0f);
        WallZombie best = grid.FindClosestZombieInRow(row, refPos);

        if (grid.is2DWorldPaused || isZoomingIntoClash || isIn3DArena)
        {
            if (grid.matchResult != GridManager.MatchResult.InProgress || arenaResolving)
            {
                return;
            }

            if (clashId != 0)
            {
                activeClashIds.Add(clashId);
                if (currentClashId == 0) currentClashId = clashId;
            }

            bool wasMissingMetadata = clashingPlantRow < 0;
            if (wasMissingMetadata)
            {
                clashingPlantRow = row;
                clashingPlantCol = col;
                clashingPlantType = plantType;
                clashingZombieType = (best != null) ? best.zombieType : zombieType;
            }
            if (clashingPlant == null && plant != null)
            {
                clashingPlant = plant;
            }
            if (clashingZombie == null && best != null)
            {
                clashingZombie = best;
                clashingZombieType = best.zombieType;
                best.MarkClashResolved();
            }
            if (wasMissingMetadata && isIn3DArena && arenaHitsLanded == 0)
            {
                Enter3D1v1Arena();
            }
            return;
        }

        currentClashId = clashId;
        activeClashIds.Clear();
        if (clashId != 0)
        {
            activeClashIds.Add(clashId);
        }

        clashingPlantRow = row;
        clashingPlantCol = col;
        clashingPlantType = plantType;
        clashingZombieType = (best != null) ? best.zombieType : zombieType;

        TriggerZombiePlantClash(best, plant, plantType, notifyNetwork: false);
    }

    public void UpdateClashZoomAnimation()
    {
        if (grid == null) return;

        clashZoomTimer += Time.deltaTime;
        float t = Mathf.Clamp01(clashZoomTimer / Mathf.Max(0.1f, grid.clashZoomDuration));
        float smoothT = Mathf.SmoothStep(0f, 1f, t);

        if (localInitiatedCurrentClash && !hasRemoteArenaPose && !arenaResolving)
        {
            clashRetrySendTimer -= Time.deltaTime;
            if (clashRetrySendTimer <= 0f)
            {
                clashRetrySendTimer = 0.25f;
                grid.NotifyClashStarted(clashingPlantRow, clashingPlantCol, clashingPlantType, grid.GetEffectiveLocalRole(), clashingZombieType, currentClashId);
                grid.NotifyArenaSeed(currentArenaLayoutSeed);
            }
        }

        Camera cam = grid.GetTargetCamera();
        if (cam != null)
        {
            cam.transform.position = Vector3.Lerp(clashZoomStartCamPos, clashZoomTargetCamPos, smoothT);
            cam.orthographicSize = Mathf.Lerp(clashZoomStartOrthoSize, grid.clashTargetOrthoSize, smoothT);
        }

        if (t >= 1.0f)
        {
            isZoomingIntoClash = false;
            Enter3D1v1Arena();
        }
    }

    // ==================== ARENA ENTRY, TICK & EXIT ====================

    public void TickArena(bool isHostOrSolo)
    {
        if (!isIn3DArena || grid == null) return;

        UpdateArenaNetworking();

        if (arenaResolving)
        {
            arenaResolveTimer -= Time.deltaTime;
            if (arenaResolveTimer <= 0f)
            {
                FinishArenaResolution();
            }
            return;
        }

        UpdateArenaCombat();

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
                currentArenaLayoutSeed = Random.Range(1000, 9999999);
                RebuildRandomArenaLayout(currentArenaLayoutSeed);
                grid.NotifyArenaSeed(currentArenaLayoutSeed);
            }
        }

        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            Exit3DArenaBackTo2DWall(destroyZombie: true);
        }
    }

    public void LateTickArena()
    {
        if (!isIn3DArena || !isDurianRollingLocal || grid == null) return;
        UpdateLocalDurianRollVisualTransform(grid.GetPrimaryPlayerController());
    }

    public void DrawArenaHUD()
    {
        if (!isIn3DArena || grid == null) return;

        bool isZombie = grid.GetEffectiveLocalRole() == MultiplayerMenuManager.PlayerRole.Zombie;
        PlantType activePlant = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;
        ZombieType activeZombie = (clashingZombie != null) ? clashingZombie.zombieType : clashingZombieType;

        var state = new ArenaHudRenderer.ArenaHudState
        {
            isZombie = isZombie,
            activePlant = activePlant,
            activeZombie = activeZombie,
            presetName = currentArenaPresetName,
            localHP = localArenaCurrentHP,
            localMaxHP = localArenaMaxHP,
            remoteHP = remoteArenaCurrentHP,
            remoteMaxHP = remoteArenaMaxHP,
            hitsLanded = arenaHitsLanded,
            hitIndicatorTimer = arenaHitIndicatorTimer,
            lastHitLabel = arenaLastHitLabel,
            remoteHitFlashTimer = remoteHitFlashTimer,
            primaryCooldown = arenaAbilityCooldownTimer,
            primaryMaxCooldown = arenaPrimaryMaxCooldown,
            secondaryCooldown = arenaSecondaryCooldownTimer,
            secondaryMaxCooldown = arenaSecondaryMaxCooldown,
            localAbilityLockTimer = localAbilityLockTimer,
            remoteAbilityLockedTimer = remoteAbilityLockedTimer,
            remoteImmobilizedTimer = remoteImmobilizedTimer,
            hazmatDisruptDuration = grid.hazmatDisruptDuration,
            isChargingRoseThorn = isChargingRoseThorn,
            roseThornChargeTimer = roseThornChargeTimer,
            roseThornChargeDuration = grid.roseThornChargeDuration,
            isDurianRolling = isDurianRollingLocal,
            durianRollTimer = durianRollTimer,
            durianRollDuration = grid.durianRollDuration,
            isBucketParrying = isBucketParryingLocal,
            bucketParryTimer = bucketParryTimer,
            bucketParryDuration = grid.bucketParryDuration,
            bucketParrySpeedDuration = grid.bucketParrySpeedDuration,
            isBunnySliding = isBunnySlidingLocal,
            bunnySlideTimer = bunnySlideTimer,
            bunnySlideDuration = grid.bunnySlideDuration,
            isFeralLunging = isFeralLungingLocal,
            feralLungeTimer = feralLungeTimer,
            feralLungeDuration = grid.feralLungeDuration,
            arenaResolving = arenaResolving,
            arenaZombieWon = arenaZombieWon,
            playerController = grid.GetPrimaryPlayerController(),
            gameConfig = grid.GameConfig
        };

        hudRenderer.Draw(in state);
    }

    public void Enter3D1v1Arena()
    {
        if (grid == null) return;

        isIn3DArena = true;
        arenaResolving = false;
        arenaPoseSendTimer = 0f;
        grid.Pause2DMusic();

        bool isZombie = grid.GetEffectiveLocalRole() == MultiplayerMenuManager.PlayerRole.Zombie;
        PlantType activePlant = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;
        ZombieType activeZombie = (clashingZombie != null) ? clashingZombie.zombieType : clashingZombieType;
        clashingPlantType = activePlant;
        clashingZombieType = activeZombie;

        PlantDefinitionSO plantDef = grid.GetPlantDefinition(activePlant);
        ZombieDefinitionSO zombieDef = grid.GetZombieDefinition(activeZombie);

        localArenaMaxHP = isZombie ? zombieDef.maxArenaHP : plantDef.maxArenaHP;
        localArenaCurrentHP = isZombie ? localArenaMaxHP : GetPlantStartingArenaHP(localArenaMaxHP, clashingPlant);
        remoteArenaMaxHP = isZombie ? plantDef.maxArenaHP : zombieDef.maxArenaHP;
        remoteArenaCurrentHP = isZombie ? GetPlantStartingArenaHP(remoteArenaMaxHP, clashingPlant) : remoteArenaMaxHP;

        arenaHitsLanded = 0;
        arenaHitIndicatorTimer = 0f;
        remoteHitFlashTimer = 0f;
        remoteImmobilizedTimer = 0f;
        localAbilityLockTimer = 0f;
        remoteAbilityLockedTimer = 0f;
        arenaAbilityCooldownTimer = 0f;
        arenaSecondaryCooldownTimer = 0f;
        arenaSecondaryMaxCooldown = (!isZombie && activePlant == PlantType.Moonflower)
            ? grid.moonflowerBurstCooldown
            : ((isZombie && activeZombie == ZombieType.Hazmat) ? grid.hazmatSludgeCooldown : 5.0f);

        hazmatGunRecoilTimer = 0f;
        localHazmatPovVisual = null;
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
        remoteIsDurianRolling = false;
        remoteDurianRollAngle = 0f;
        sparringBot.Reset();

        if (localDurianShadowVisual != null)
        {
            Destroy(localDurianShadowVisual);
            localDurianShadowVisual = null;
        }

        BuildOrShow3DArenaEnvironment();
        UpdateRemoteWorldHPBarScale();

        Vector3 arenaSpawnPos = isZombie ? ArenaEnvironmentBuilder.ArenaZombieSpawn : ArenaEnvironmentBuilder.ArenaPlantSpawn;
        Quaternion arenaSpawnRot = Quaternion.Euler(0f, isZombie ? 180f : 0f, 0f);

        PlayerController chosenPlayer = grid.GetPrimaryPlayerController();
        Camera cam = grid.GetTargetCamera();
        if (chosenPlayer != null && cam != null)
        {
            chosenPlayer.inputFrozen = false;
            chosenPlayer.characterSpeedMultiplier = isZombie ? zombieDef.arenaSpeedMultiplier : plantDef.arenaSpeedMultiplier;
            chosenPlayer.characterBaseFovBonus = isZombie ? zombieDef.arenaFovBonus : 0f;
            chosenPlayer.EnterFirstPersonArena(cam, arenaSpawnPos, arenaSpawnRot, currentArenaOpponentName);

            if (!isZombie && activePlant == PlantType.Durian)
            {
                SetupLocalDurianShadowVisual(chosenPlayer);
            }
        }
    }

    private int GetPlantStartingArenaHP(int maxArenaHP, PlaceholderPlant plant)
    {
        if (plant != null && plant.maxHealth > 0 && plant.currentHealth < plant.maxHealth)
        {
            float ratio = Mathf.Clamp01((float)plant.currentHealth / plant.maxHealth);
            return Mathf.Clamp(Mathf.Max(Mathf.RoundToInt(maxArenaHP * 0.25f), Mathf.RoundToInt(maxArenaHP * ratio)), 1, maxArenaHP);
        }
        return maxArenaHP;
    }

    public void ResolveArena(bool zombieWon, bool notifyNetwork = true, int resolvedRow = -1, int resolvedCol = -1, int clashId = 0)
    {
        if (grid == null) return;
        if (clashId != 0 && resolvedClashIds.Contains(clashId)) return;
        if (arenaResolving) return;
        if (!isIn3DArena && !isZoomingIntoClash) return;

        if (clashingPlantRow < 0 && resolvedRow >= 0)
        {
            clashingPlantRow = resolvedRow;
            clashingPlantCol = resolvedCol;
            if (clashingPlant == null)
            {
                clashingPlant = grid.GetPlacedPlantAt(resolvedRow, resolvedCol);
            }
        }

        if (currentClashId != 0) resolvedClashIds.Add(currentClashId);
        foreach (int id in activeClashIds) resolvedClashIds.Add(id);
        if (clashId != 0) resolvedClashIds.Add(clashId);
        lastResolvedPlantRow = clashingPlantRow;
        lastResolvedPlantCol = clashingPlantCol;

        if (notifyNetwork)
        {
            int sendClashId = (clashId != 0) ? clashId : currentClashId;
            grid.NotifyArenaResolved(zombieWon, clashingPlantRow, clashingPlantCol, sendClashId);
        }

        if (isZoomingIntoClash)
        {
            isZoomingIntoClash = false;
            Enter3D1v1Arena();
        }

        arenaResolving = true;
        arenaZombieWon = zombieWon;
        arenaResolveTimer = grid.arenaResultPauseDuration;

        PlayerController pc = grid.GetPrimaryPlayerController();
        if (pc != null) pc.inputFrozen = true;
    }

    private void FinishArenaResolution()
    {
        arenaResolving = false;

        if (arenaZombieWon)
        {
            PlaceholderPlant slotPlant = grid.GetPlacedPlantAt(clashingPlantRow, clashingPlantCol);
            if (slotPlant != null)
            {
                grid.ClearPlantSlot(clashingPlantRow, clashingPlantCol, slotPlant);
                slotPlant.gameObject.SetActive(false);
                Destroy(slotPlant.gameObject);
            }
            if (clashingPlant != null)
            {
                grid.ClearPlantSlot(clashingPlant.row, clashingPlant.column, clashingPlant);
                GameObject cpObj = clashingPlant.gameObject;
                clashingPlant = null;
                cpObj.SetActive(false);
                Destroy(cpObj);
            }

            WallZombie survivingZombie = clashingZombie;
            if (survivingZombie == null && clashingPlantRow >= 0)
            {
                Vector3 refPos = grid.GetTileWorldPosition(clashingPlantRow, Mathf.Max(0, clashingPlantCol), 0f);
                survivingZombie = grid.FindClosestZombieInRow(clashingPlantRow, refPos);
            }
            if (survivingZombie != null)
            {
                survivingZombie.ResetForNextClash();
            }

            Exit3DArenaBackTo2DWall(destroyZombie: false);
        }
        else
        {
            clashingPlant = null;
            Exit3DArenaBackTo2DWall(destroyZombie: true);
        }
    }

    public void Exit3DArenaBackTo2DWall(bool destroyZombie = true)
    {
        if (grid == null) return;

        PlayerController chosenPlayer = grid.GetPrimaryPlayerController();
        CancelAllActiveArenaAbilities(chosenPlayer);

        lastArenaExitUnscaledTime = Time.unscaledTime;
        if (currentClashId != 0) resolvedClashIds.Add(currentClashId);
        foreach (int id in activeClashIds) resolvedClashIds.Add(id);
        activeClashIds.Clear();
        currentClashId = 0;

        isIn3DArena = false;
        isZoomingIntoClash = false;
        arenaResolving = false;
        hasRemoteArenaPose = false;
        localInitiatedCurrentClash = false;
        sparringBot.Reset();
        localAbilityLockTimer = 0f;
        remoteAbilityLockedTimer = 0f;

        if (localHazmatPovVisual != null)
        {
            Destroy(localHazmatPovVisual);
            localHazmatPovVisual = null;
        }
        if (localDurianShadowVisual != null)
        {
            Destroy(localDurianShadowVisual);
            localDurianShadowVisual = null;
        }

        if (arena3DRoot != null)
        {
            DisposeCurrentArenaRoot();
        }

        if (destroyZombie)
        {
            WallZombie targetZombie = clashingZombie;
            if (targetZombie == null && clashingPlantRow >= 0)
            {
                Vector3 refPos = grid.GetTileWorldPosition(clashingPlantRow, Mathf.Max(0, clashingPlantCol), 0f);
                targetZombie = grid.FindClosestZombieInRow(clashingPlantRow, refPos);
            }
            if (targetZombie != null)
            {
                targetZombie.MarkClashResolved();
                targetZombie.gameObject.SetActive(false);
                Destroy(targetZombie.gameObject);
            }
        }

        clashingZombie = null;
        clashingPlant = null;
        clashingPlantRow = -1;
        clashingPlantCol = -1;

        if (chosenPlayer != null)
        {
            chosenPlayer.inputFrozen = false;
            chosenPlayer.ExitFirstPersonArena();
        }

        grid.is2DWorldPaused = false;
        grid.SyncWallScaleAndCamera();
        grid.Update2DBackgroundMusicPublic();
    }

    // ==================== ENVIRONMENT & REMOTE AVATAR ====================

    private void DisposeCurrentArenaRoot()
    {
        if (remoteArenaAvatar != null)
        {
            remoteArenaAvatar.name = "Arena3D_RemotePlayer_Destroyed";
        }
        if (arena3DRoot != null)
        {
            arena3DRoot.name = "Arena3D_1v1_Destroyed";
            arena3DRoot.SetActive(false);
            Destroy(arena3DRoot);
            arena3DRoot = null;
        }
        arenaObstaclesRoot = null;
        arenaFloorRenderer = null;
        remoteArenaAvatar = null;
        remoteBodyHitbox = null;
        remoteHeadHitbox = null;
        remoteZombieVisualRoot = null;
        remotePlantVisualRoot = null;
        remoteIsDurianRolling = false;
        remoteDurianRollAngle = 0f;
        remoteParryShieldObj = null;
        remoteHpBarFill = null;
        remoteHpBarFillRenderer = null;
    }

    private void BuildOrShow3DArenaEnvironment()
    {
        if (arena3DRoot != null)
        {
            DisposeCurrentArenaRoot();
        }

        if (currentArenaLayoutSeed == 0)
        {
            currentArenaLayoutSeed = Random.Range(1000, 9999999);
        }

        bool localIsPlant = grid.GetEffectiveLocalRole() == MultiplayerMenuManager.PlayerRole.Plant;
        PlantType touchedType = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;
        ZombieType touchedZombieType = (clashingZombie != null) ? clashingZombie.zombieType : clashingZombieType;

        var built = ArenaEnvironmentBuilder.BuildEnvironment(
            currentArenaLayoutSeed,
            localIsPlant,
            touchedType,
            touchedZombieType,
            grid.GameConfig);

        arena3DRoot = built.arena3DRoot;
        arenaObstaclesRoot = built.arenaObstaclesRoot;
        arenaFloorRenderer = built.arenaFloorRenderer;
        remoteArenaAvatar = built.remoteArenaAvatar;
        remoteBodyHitbox = built.remoteBodyHitbox;
        remoteHeadHitbox = built.remoteHeadHitbox;
        remoteZombieVisualRoot = built.remoteZombieVisualRoot;
        remotePlantVisualRoot = built.remotePlantVisualRoot;
        remoteParryShieldObj = built.remoteParryShieldObj;
        remoteHpBarFill = built.remoteHpBarFill;
        remoteHpBarFillRenderer = built.remoteHpBarFillRenderer;
        remoteHpBarBaseColor = built.remoteHpBarBaseColor;
        currentArenaOpponentName = built.opponentName;
        currentArenaPresetName = built.presetName;
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
        ArenaEnvironmentBuilder.RebuildRandomArenaLayout(
            seed,
            arena3DRoot,
            ref arenaObstaclesRoot,
            arenaFloorRenderer,
            out currentArenaPresetName);
    }

    // ==================== NETWORK POSE & REMOTE EVENTS ====================

    private void UpdateArenaNetworking()
    {
        if (arenaResolving || grid == null) return;

        if (localInitiatedCurrentClash && !hasRemoteArenaPose)
        {
            clashRetrySendTimer -= Time.deltaTime;
            if (clashRetrySendTimer <= 0f)
            {
                clashRetrySendTimer = 0.35f;
                grid.NotifyClashStarted(clashingPlantRow, clashingPlantCol, clashingPlantType, grid.GetEffectiveLocalRole(), clashingZombieType, currentClashId);
                grid.NotifyArenaSeed(currentArenaLayoutSeed);
            }
        }

        PlayerController pc = grid.GetPrimaryPlayerController();
        arenaPoseSendTimer -= Time.deltaTime;
        if (pc != null && arenaPoseSendTimer <= 0f)
        {
            arenaPoseSendTimer = 0.05f;
            int stateFlags = 0;
            if (isBunnySlidingLocal) stateFlags |= 1;
            if (isBucketParryingLocal) stateFlags |= 2;
            if (pc.parrySpeedBonusTimer > 0f) stateFlags |= 4;
            if (isFeralLungingLocal) stateFlags |= 8;
            if (isDurianRollingLocal) stateFlags |= 16;
            grid.NotifyArenaPose(pc.transform.position, pc.transform.eulerAngles.y, stateFlags, currentClashId);
        }

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

        if (remotePlantVisualRoot != null)
        {
            if (remoteIsDurianRolling)
            {
                remoteDurianRollAngle = Mathf.Repeat(remoteDurianRollAngle + Time.deltaTime * 540f, 360f);
                float bounce = Mathf.Abs(Mathf.Sin(remoteDurianRollAngle * Mathf.Deg2Rad)) * 0.16f;
                remotePlantVisualRoot.localPosition = new Vector3(0f, -0.15f + bounce, 0f);
                remotePlantVisualRoot.localRotation = Quaternion.Euler(remoteDurianRollAngle, 0f, 0f);
            }
            else
            {
                remoteDurianRollAngle = 0f;
                remotePlantVisualRoot.localPosition = Vector3.Lerp(remotePlantVisualRoot.localPosition, Vector3.zero, Time.deltaTime * 14f);
                remotePlantVisualRoot.localRotation = Quaternion.Slerp(remotePlantVisualRoot.localRotation, Quaternion.identity, Time.deltaTime * 14f);
            }
        }
    }

    public void SetRemoteArenaPose(Vector3 pos, float yaw, int stateFlags = 0, int clashId = 0)
    {
        if (grid == null || arenaResolving) return;
        if (clashId != 0 && resolvedClashIds.Contains(clashId)) return;

        if (!isIn3DArena && !isZoomingIntoClash)
        {
            if ((Time.unscaledTime - lastArenaExitUnscaledTime) < 2.5f) return;

            grid.gameplayActive = true;
            grid.is2DWorldPaused = true;
            if (clashId != 0)
            {
                currentClashId = clashId;
                activeClashIds.Add(clashId);
            }
            Enter3D1v1Arena();
        }
        else if (clashId != 0)
        {
            activeClashIds.Add(clashId);
            if (currentClashId == 0) currentClashId = clashId;
        }

        remoteArenaTargetPos = pos;
        remoteArenaTargetYaw = yaw;
        remoteIsBunnySliding = (stateFlags & 1) != 0;
        remoteIsBucketParrying = (stateFlags & 2) != 0;
        remoteHasParrySpeedBuff = (stateFlags & 4) != 0;
        remoteIsFeralLunging = (stateFlags & 8) != 0;
        remoteIsDurianRolling = (stateFlags & 16) != 0;

        if (!hasRemoteArenaPose && remoteArenaAvatar != null)
        {
            remoteArenaAvatar.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }
        hasRemoteArenaPose = true;
    }

    public void ApplyLocalArenaImmobilize(float duration)
    {
        if (isBucketParryingLocal)
        {
            TriggerLocalBucketParrySuccess(50);
            return;
        }

        PlayerController pc = grid != null ? grid.GetPrimaryPlayerController() : null;
        if (pc != null)
        {
            pc.Immobilize(duration);
            CancelAllActiveArenaAbilities(pc);
        }
    }

    public void ApplyLocalArenaDisrupt(float duration)
    {
        if (!isIn3DArena || arenaResolving) return;

        localAbilityLockTimer = Mathf.Max(localAbilityLockTimer, duration);

        bool wasDurianRolling = isDurianRollingLocal;
        bool wasCharging = isChargingRoseThorn;

        PlayerController pc = grid != null ? grid.GetPrimaryPlayerController() : null;
        if (pc != null)
        {
            pc.CancelMobilityMomentum();
            CancelAllActiveArenaAbilities(pc);
        }

        if (wasDurianRolling)
        {
            arenaSecondaryCooldownTimer = 6.0f;
            arenaSecondaryMaxCooldown = 6.0f;
        }

        arenaHitIndicatorTimer = 1.25f;
        arenaLastHitLabel = (wasDurianRolling || wasCharging)
            ? $"☣️ ABILITY CANCELED! LOCKED ({duration:F1}s) ☣️"
            : $"☣️ DISRUPTED! ABILITIES LOCKED ({duration:F1}s) ☣️";
    }

    public void ApplyRemoteArenaDamage(int damage, float stunDuration)
    {
        if (!isIn3DArena || arenaResolving || grid == null) return;

        if (isBucketParryingLocal)
        {
            TriggerLocalBucketParrySuccess(damage);
            grid.NotifyArenaParried(damage, localArenaCurrentHP);
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
            bool localIsZombie = grid.GetEffectiveLocalRole() == MultiplayerMenuManager.PlayerRole.Zombie;
            ResolveArena(zombieWon: !localIsZombie, notifyNetwork: true);
        }
    }

    public void ApplyRemoteArenaKnockback(Vector3 knockbackVel)
    {
        if (!isIn3DArena || arenaResolving || isBucketParryingLocal || grid == null) return;

        PlayerController pc = grid.GetPrimaryPlayerController();
        if (pc != null)
        {
            pc.ApplyKnockback(new Vector3(knockbackVel.x, 0f, knockbackVel.z), knockbackVel.y);
            arenaHitIndicatorTimer = Mathf.Max(arenaHitIndicatorTimer, 0.85f);
            arenaLastHitLabel = "💥 BLASTED BACK BY MOONFLOWER!";
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

    public void SpawnRemoteArenaAbilityFx(string fxType, Vector3 startPos, Vector3 endPos)
    {
        if (!isIn3DArena || arena3DRoot == null || grid == null) return;

        if (fxType == "BEAM")
        {
            DispatchHitscanVisuals(grid.GetMoonflowerSolarBoltConfig(), startPos, endPos, false);
        }
        else if (fxType == "THORN")
        {
            DispatchHitscanVisuals(grid.GetRoseThornConfig(), startPos, endPos, false);
        }
        else if (fxType == "SLASH")
        {
            Quaternion rot = endPos.sqrMagnitude > 0.001f ? Quaternion.LookRotation(endPos) : Quaternion.identity;
            ArenaVfxFactory.SpawnSlashEffect(arena3DRoot.transform, startPos, rot);
        }
        else if (fxType == "DURIAN_BASH")
        {
            Quaternion rot = endPos.sqrMagnitude > 0.001f ? Quaternion.LookRotation(endPos) : Quaternion.identity;
            ArenaVfxFactory.SpawnDurianSpikeBurst(arena3DRoot.transform, startPos, rot);
        }
        else if (fxType == "PARRY")
        {
            ArenaVfxFactory.SpawnParryBurst(arena3DRoot.transform, startPos + Vector3.up * 0.9f);
        }
        else if (fxType == "MOON_BURST")
        {
            ArenaVfxFactory.SpawnMoonExplosion(arena3DRoot.transform, startPos);
        }
        else if (fxType == "ACID_SHOT")
        {
            DispatchHitscanVisuals(grid.GetHazmatAcidShotConfig(), startPos, endPos, false);
        }
        else if (fxType == "ACID_BURST")
        {
            DispatchHitscanVisuals(grid.GetHazmatSludgeBlastConfig(), startPos, endPos, false);
            ArenaVfxFactory.SpawnAcidExplosion(arena3DRoot.transform, endPos);
        }
    }

    // ==================== COMBAT LOOP & ABILITIES ====================

    private void UpdateArenaCombat()
    {
        PlayerController pc = grid.GetPrimaryPlayerController();
        if (pc == null) return;
        Camera cam = grid.GetTargetCamera();

        if (arenaHitIndicatorTimer > 0f) arenaHitIndicatorTimer = Mathf.Max(0f, arenaHitIndicatorTimer - Time.deltaTime);
        if (remoteImmobilizedTimer > 0f) remoteImmobilizedTimer = Mathf.Max(0f, remoteImmobilizedTimer - Time.deltaTime);
        if (localAbilityLockTimer > 0f) localAbilityLockTimer = Mathf.Max(0f, localAbilityLockTimer - Time.deltaTime);
        if (remoteAbilityLockedTimer > 0f) remoteAbilityLockedTimer = Mathf.Max(0f, remoteAbilityLockedTimer - Time.deltaTime);

        if (remoteHitFlashTimer > 0f)
        {
            remoteHitFlashTimer = Mathf.Max(0f, remoteHitFlashTimer - Time.deltaTime);
            if (remoteHpBarFillRenderer != null)
            {
                Color col = (remoteHitFlashTimer > 0f) ? new Color(1.0f, 0.95f, 0.35f) : remoteHpBarBaseColor;
                remoteHpBarFillRenderer.sharedMaterial = MaterialCache.GetColoredMaterial(col);
            }
        }

        if (arenaAbilityCooldownTimer > 0f) arenaAbilityCooldownTimer = Mathf.Max(0f, arenaAbilityCooldownTimer - Time.deltaTime);
        if (arenaSecondaryCooldownTimer > 0f) arenaSecondaryCooldownTimer = Mathf.Max(0f, arenaSecondaryCooldownTimer - Time.deltaTime);

        bool isZombie = grid.GetEffectiveLocalRole() == MultiplayerMenuManager.PlayerRole.Zombie;
        PlantType activePlant = (clashingPlant != null) ? clashingPlant.plantType : clashingPlantType;
        ZombieType activeZombie = (clashingZombie != null) ? clashingZombie.zombieType : clashingZombieType;

        if (!hasRemoteArenaPose && remoteArenaAvatar != null)
        {
            sparringBot.UpdateBot(
                pc,
                remoteArenaAvatar,
                arena3DRoot != null ? arena3DRoot.transform : null,
                isZombie,
                activePlant,
                activeZombie,
                isBunnySlidingLocal,
                isBucketParryingLocal,
                isDurianRollingLocal,
                isChargingRoseThorn,
                remoteImmobilizedTimer,
                remoteAbilityLockedTimer,
                localArenaMaxHP,
                grid.moonflowerBurstRadius,
                grid.moonflowerBurstDamage,
                grid.hazmatDisruptDuration,
                grid.GetMoonflowerSolarBoltConfig(),
                grid.GetRoseThornConfig(),
                grid.GetHazmatAcidShotConfig(),
                grid.GetHazmatSludgeBlastConfig(),
                ref remoteIsBucketParrying,
                ref remoteIsBunnySliding,
                (start, target) =>
                {
                    bool los = TryRaycastArenaBeam(start, target, out Vector3 beamEnd);
                    return (los, beamEnd);
                },
                DispatchHitscanVisuals,
                ApplyRemoteArenaDamage,
                ApplyLocalArenaDisrupt,
                label =>
                {
                    arenaHitIndicatorTimer = 0.85f;
                    arenaLastHitLabel = label;
                });
        }

        if (hazmatGunRecoilTimer > 0f)
        {
            hazmatGunRecoilTimer = Mathf.Max(0f, hazmatGunRecoilTimer - Time.deltaTime);
        }

        if (isZombie && activeZombie == ZombieType.Hazmat && cam != null)
        {
            if (localHazmatPovVisual == null && arena3DRoot != null)
            {
                localHazmatPovVisual = ArenaModelFactory.CreateLocalHazmatGunPovVisual(arena3DRoot.transform);
            }
            if (localHazmatPovVisual != null)
            {
                float recoilBack = Mathf.Clamp01(hazmatGunRecoilTimer / 0.28f) * 0.14f;
                float bobY = Mathf.Sin(Time.time * 4.5f) * 0.01f;
                localHazmatPovVisual.transform.position = cam.transform.position
                    + cam.transform.forward * (0.60f - recoilBack)
                    + cam.transform.right * 0.34f
                    - cam.transform.up * (0.26f + bobY);
                localHazmatPovVisual.transform.rotation = cam.transform.rotation * Quaternion.Euler(-recoilBack * 45f, -4f, 0f);
            }
        }

        if (pc.IsImmobilized)
        {
            CancelAllActiveArenaAbilities(pc);
            return;
        }

        if (TickActiveAbilityStates(pc, cam, activeZombie))
        {
            return;
        }

        if (localAbilityLockTimer > 0f)
        {
            return;
        }

        HandleAbilityInputs(pc, isZombie, activePlant, activeZombie);
    }

    private bool TickActiveAbilityStates(PlayerController pc, Camera cam, ZombieType activeZombie)
    {
        if (isBucketParryingLocal)
        {
            bucketParryTimer -= Time.deltaTime;
            if (localBucketParryVisual == null && arena3DRoot != null)
            {
                localBucketParryVisual = ArenaModelFactory.CreateLocalParryShieldVisual(arena3DRoot.transform);
            }
            if (localBucketParryVisual != null && cam != null)
            {
                localBucketParryVisual.transform.position = cam.transform.position + cam.transform.forward * 0.72f + new Vector3(0f, -0.08f, 0f);
                localBucketParryVisual.transform.rotation = cam.transform.rotation;
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

        if (isBunnySlidingLocal)
        {
            bunnySlideTimer -= Time.deltaTime;
            pc.isBunnySliding = true;

            if (localBunnySlideVisual == null && arena3DRoot != null)
            {
                localBunnySlideVisual = ArenaModelFactory.CreateLocalBunnySlideLegsVisual(arena3DRoot.transform);
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

        if (isFeralLungingLocal)
        {
            feralLungeTimer -= Time.deltaTime;
            pc.isFeralLunging = true;

            if (localFeralLungeVisual == null && arena3DRoot != null)
            {
                localFeralLungeVisual = ArenaModelFactory.CreateLocalFeralLungePovVisual(arena3DRoot.transform, activeZombie);
            }
            if (localFeralLungeVisual != null && cam != null)
            {
                float diveBob = Mathf.Sin((1f - (feralLungeTimer / Mathf.Max(0.01f, grid.feralLungeDuration))) * Mathf.PI) * 0.14f;
                localFeralLungeVisual.transform.position = cam.transform.position
                    + cam.transform.forward * (0.64f + diveBob)
                    - cam.transform.up * 0.24f;
                localFeralLungeVisual.transform.rotation = cam.transform.rotation * Quaternion.Euler(14f, 0f, 0f);
            }

            if (!feralLungeHitDealt && IsOpponentWithinHorizontalRange(pc, 2.35f, 2.2f))
            {
                feralLungeHitDealt = true;
                Vector3 slashCenter = cam != null ? cam.transform.position + cam.transform.forward * 1.15f : pc.transform.position + pc.transform.forward * 1.15f;
                Quaternion slashRot = cam != null ? cam.transform.rotation : pc.transform.rotation;
                ArenaVfxFactory.SpawnSlashEffect(arena3DRoot != null ? arena3DRoot.transform : null, slashCenter, slashRot);
                grid.NotifyArenaAbilityFx("SLASH", slashCenter, pc.transform.forward);
                DealDamageToOpponent(38, "FERAL DIVE LUNGE HIT (38 DMG)!", 0f);
                StopFeralLunge(pc);
                return true;
            }

            if (feralLungeTimer <= 0f)
            {
                StopFeralLunge(pc);
            }
        }

        if (isChargingRoseThorn)
        {
            roseThornChargeTimer -= Time.deltaTime;
            if (roseThornChargeTimer <= 0f)
            {
                isChargingRoseThorn = false;
                FireRoseHitscanThorn(pc, isHeavyVolley: false);
            }
            return true;
        }

        if (isDurianRollingLocal)
        {
            durianRollTimer -= Time.deltaTime;
            pc.isDurianRolling = true;

            EnsureLocalDurianRollVisualCreated();
            UpdateLocalDurianRollVisualTransform(pc);

            if (IsOpponentWithinHorizontalRange(pc, 1.95f, 2.2f))
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
                    grid.NotifyArenaStun(2.0f);
                    DealDamageToOpponent(50, "DURIAN ROLL SLAM! (50 DMG + 2s STUN)", 2.0f);
                }
                return true;
            }

            if (durianRollTimer <= 0f)
            {
                StopDurianRoll(pc);
                arenaSecondaryCooldownTimer = 6.0f;
                arenaSecondaryMaxCooldown = 6.0f;
            }
            return true;
        }

        return false;
    }

    private bool IsOpponentWithinHorizontalRange(PlayerController pc, float maxHorizDist, float maxVertDist)
    {
        if (remoteArenaAvatar == null || pc == null) return false;
        Vector3 a = new Vector3(pc.transform.position.x, 0f, pc.transform.position.z);
        Vector3 b = new Vector3(remoteArenaAvatar.transform.position.x, 0f, remoteArenaAvatar.transform.position.z);
        return Vector3.Distance(a, b) <= maxHorizDist && Mathf.Abs(pc.transform.position.y - remoteArenaAvatar.transform.position.y) <= maxVertDist;
    }

    private void HandleAbilityInputs(PlayerController pc, bool isZombie, PlantType activePlant, ZombieType activeZombie)
    {
        bool leftClick = Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked;
        bool rightClick = Input.GetMouseButtonDown(1) && Cursor.lockState == CursorLockMode.Locked;
        bool keyE = Input.GetKeyDown(KeyCode.E);
        bool keyQ = Input.GetKeyDown(KeyCode.Q);
        bool keyCtrl = Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C);

        if (isZombie)
        {
            if (activeZombie == ZombieType.Buckethead)
            {
                if ((rightClick || keyE || keyQ) && arenaSecondaryCooldownTimer <= 0f && !isBucketParryingLocal)
                {
                    StartBucketheadParry(pc);
                }
                else if (leftClick && arenaAbilityCooldownTimer <= 0f)
                {
                    PerformZombieMeleeSlash(pc, 30, 1.0f, "BUCKETHEAD SLAM (30 DMG)!");
                }
            }
            else if (activeZombie == ZombieType.Bunny)
            {
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
                        PerformZombieMeleeSlash(pc, 22, 0.65f, "BUNNY CLAW SLASH (22 DMG)!");
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
                        PerformZombieMeleeSlash(pc, 22, 0.65f, "BUNNY CLAW SLASH (22 DMG)!");
                    }
                }
            }
            else if (activeZombie == ZombieType.Hazmat)
            {
                if ((rightClick || keyE || keyQ) && arenaSecondaryCooldownTimer <= 0f)
                {
                    FireHazmatSludgeBlast(pc);
                }
                else if (leftClick && arenaAbilityCooldownTimer <= 0f)
                {
                    FireHazmatAcidShot(pc);
                }
            }
            else
            {
                if ((rightClick || keyQ) && arenaSecondaryCooldownTimer <= 0f && !isFeralLungingLocal)
                {
                    StartFeralLunge(pc);
                }
                else if ((leftClick || keyE) && arenaAbilityCooldownTimer <= 0f)
                {
                    PerformZombieMeleeSlash(pc, 25, 0.85f, "ZOMBIE CLAW SLASH (25 DMG)!");
                }
            }
        }
        else
        {
            if (activePlant == PlantType.Moonflower)
            {
                if ((rightClick || keyE) && arenaSecondaryCooldownTimer <= 0f)
                {
                    TriggerMoonflowerOutburst(pc);
                }
                else if (leftClick && arenaAbilityCooldownTimer <= 0f)
                {
                    FireMoonflowerSolarBolt(pc);
                }
            }
            else if (activePlant == PlantType.Rose || activePlant == PlantType.Roze)
            {
                if ((rightClick || keyE) && arenaSecondaryCooldownTimer <= 0f)
                {
                    FireRoseHitscanThorn(pc, isHeavyVolley: true);
                }
                else if (leftClick && arenaAbilityCooldownTimer <= 0f)
                {
                    isChargingRoseThorn = true;
                    roseThornChargeTimer = grid.roseThornChargeDuration;
                }
            }
            else if (activePlant == PlantType.Durian)
            {
                if ((rightClick || keyE) && arenaSecondaryCooldownTimer <= 0f)
                {
                    StartDurianRoll(pc);
                }
                else if (leftClick && arenaAbilityCooldownTimer <= 0f)
                {
                    PerformDurianSpikeBash(pc);
                }
            }
        }
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

        float bonusPct = Mathf.Clamp(incomingDamage * 1.8f, 25f, 140f);
        float speedMul = 1.0f + (bonusPct / 100f);

        PlayerController pc = grid.GetPrimaryPlayerController();
        if (pc != null)
        {
            pc.ApplyParrySpeedBoost(speedMul, grid.bucketParrySpeedDuration);
            grid.NotifyArenaAbilityFx("PARRY", pc.transform.position, pc.transform.forward);
        }

        arenaHitIndicatorTimer = 1.5f;
        arenaLastHitLabel = $"⚡ PARRIED {incomingDamage} DMG! +{bonusPct:F0}% SPEED FOR 3.0s! ⚡";
    }

    private void CancelAllActiveArenaAbilities(PlayerController pc)
    {
        isChargingRoseThorn = false;
        roseThornChargeTimer = 0f;

        StopDurianRoll(pc);

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

    private void StartBucketheadParry(PlayerController pc)
    {
        isBucketParryingLocal = true;
        bucketParryTimer = grid.bucketParryDuration;
        arenaSecondaryCooldownTimer = grid.bucketParryCooldown;
        arenaSecondaryMaxCooldown = grid.bucketParryCooldown;
        grid.NotifyArenaAbilityFx("PARRY_START", pc.transform.position, pc.transform.forward);
    }

    private void StartBunnySlide(PlayerController pc)
    {
        isBunnySlidingLocal = true;
        bunnySlideTimer = grid.bunnySlideDuration;
        arenaSecondaryCooldownTimer = grid.bunnySlideCooldown;
        arenaSecondaryMaxCooldown = grid.bunnySlideCooldown;
        pc.isBunnySliding = true;
        grid.NotifyArenaAbilityFx("SLIDE", pc.transform.position, pc.transform.forward);
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

    private void StartFeralLunge(PlayerController pc)
    {
        isFeralLungingLocal = true;
        feralLungeTimer = grid.feralLungeDuration;
        feralLungeHitDealt = false;
        arenaSecondaryCooldownTimer = grid.feralLungeCooldown;
        arenaSecondaryMaxCooldown = grid.feralLungeCooldown;
        arenaAbilityCooldownTimer = Mathf.Max(arenaAbilityCooldownTimer, 0.55f);
        pc.isFeralLunging = true;
        grid.NotifyArenaAbilityFx("LUNGE", pc.transform.position, pc.transform.forward);
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

    private void StartDurianRoll(PlayerController pc)
    {
        isDurianRollingLocal = true;
        durianRollTimer = grid.durianRollDuration;
        if (pc != null) pc.isDurianRolling = true;
        if (localDurianShadowVisual != null)
        {
            localDurianShadowVisual.SetActive(false);
        }
        EnsureLocalDurianRollVisualCreated();
        UpdateLocalDurianRollVisualTransform(pc);
        if (pc != null)
        {
            grid.NotifyArenaAbilityFx("ROLL", pc.transform.position, pc.transform.forward);
        }
    }

    private void EnsureLocalDurianRollVisualCreated()
    {
        if (localDurianRollVisual != null || arena3DRoot == null) return;
        localDurianRollVisual = ArenaModelFactory.Create3DPlantModel(PlantType.Durian, "DurianRollVisual", grid.GameConfig);
        localDurianRollVisual.transform.SetParent(arena3DRoot.transform, true);
        foreach (Collider c in localDurianRollVisual.GetComponentsInChildren<Collider>())
        {
            Destroy(c);
        }
    }

    private void UpdateLocalDurianRollVisualTransform(PlayerController pc)
    {
        if (localDurianRollVisual == null || pc == null) return;

        localDurianRollVisual.transform.position = pc.transform.position
            + new Vector3(0f, -0.50f + pc.DurianRollVisualBounceY, 0f)
            + pc.transform.forward * 1.02f;
        localDurianRollVisual.transform.rotation = Quaternion.Euler(0f, pc.transform.eulerAngles.y, pc.DurianRollSteerTilt);
        localDurianRollVisual.transform.localScale = Vector3.one * 0.74f;

        Transform spinPivot = localDurianRollVisual.transform.Find("DurianModel");
        if (spinPivot == null && localDurianRollVisual.transform.childCount > 0)
        {
            spinPivot = localDurianRollVisual.transform.GetChild(0);
        }
        if (spinPivot != null)
        {
            spinPivot.localRotation = Quaternion.Euler(pc.DurianRollAngleDeg, 0f, 0f);
        }
    }

    private void SetupLocalDurianShadowVisual(PlayerController pc)
    {
        if (pc == null) return;
        MeshRenderer capsuleMr = pc.GetComponent<MeshRenderer>();
        if (capsuleMr != null) capsuleMr.enabled = false;

        if (localDurianShadowVisual != null)
        {
            Destroy(localDurianShadowVisual);
            localDurianShadowVisual = null;
        }

        localDurianShadowVisual = ArenaModelFactory.Create3DPlantModel(PlantType.Durian, "LocalDurianShadow", grid.GameConfig);
        localDurianShadowVisual.transform.SetParent(pc.transform, false);
        localDurianShadowVisual.transform.localPosition = new Vector3(0f, -0.20f, 0f);
        localDurianShadowVisual.transform.localRotation = Quaternion.identity;
        localDurianShadowVisual.transform.localScale = Vector3.one * 0.90f;

        foreach (Collider c in localDurianShadowVisual.GetComponentsInChildren<Collider>(true))
        {
            Destroy(c);
        }
        foreach (Renderer r in localDurianShadowVisual.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
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
        if (localDurianShadowVisual != null)
        {
            localDurianShadowVisual.SetActive(true);
        }
    }

    private void FireHazmatAcidShot(PlayerController pc)
    {
        arenaAbilityCooldownTimer = 0.75f;
        arenaPrimaryMaxCooldown = 0.75f;
        hazmatGunRecoilTimer = 0.16f;
        Camera aimCam = GetCharacterAimCamera(pc);
        if (aimCam == null) return;

        Vector3 start = pc.transform.position + new Vector3(0f, 0.50f, 0f) + aimCam.transform.forward * 0.55f + aimCam.transform.right * 0.22f;
        HitscanResult hitResult = HitscanRayUtility.PerformHitscan(
            aimCam,
            start,
            grid.GetHazmatAcidShotConfig(),
            IsOpponentHitbox,
            c => c.GetComponent<PlayerController>() != null);

        grid.NotifyArenaHitscanFired(hitResult);
        grid.NotifyArenaAbilityFx("ACID_SHOT", start, hitResult.endPoint);

        if (hitResult.hitOpponent)
        {
            int dmg = 20 + Mathf.RoundToInt(remoteArenaMaxHP * 0.04f);
            DealDamageToOpponent(dmg, $"CORROSIVE ACID HIT ({dmg} DMG)!", 0f);
        }
    }

    private void FireHazmatSludgeBlast(PlayerController pc)
    {
        arenaSecondaryCooldownTimer = grid.hazmatSludgeCooldown;
        arenaSecondaryMaxCooldown = grid.hazmatSludgeCooldown;
        hazmatGunRecoilTimer = 0.28f;
        Camera aimCam = GetCharacterAimCamera(pc);
        if (aimCam == null) return;

        Vector3 aimForward = aimCam.transform.forward;
        Vector3 flatForward = new Vector3(aimForward.x, 0f, aimForward.z);
        if (flatForward.sqrMagnitude < 0.001f) flatForward = pc.transform.forward;
        flatForward.Normalize();
        pc.ApplyKnockback(-flatForward * 6.5f, 4.0f, suppressLandingSlow: true);

        Vector3 start = pc.transform.position + new Vector3(0f, 0.50f, 0f) + aimCam.transform.forward * 0.55f + aimCam.transform.right * 0.22f;
        HitscanResult hitResult = HitscanRayUtility.PerformHitscan(
            aimCam,
            start,
            grid.GetHazmatSludgeBlastConfig(),
            IsOpponentHitbox,
            c => c.GetComponent<PlayerController>() != null);

        grid.NotifyArenaHitscanFired(hitResult);
        ArenaVfxFactory.SpawnAcidExplosion(arena3DRoot != null ? arena3DRoot.transform : null, hitResult.endPoint);
        grid.NotifyArenaAbilityFx("ACID_BURST", start, hitResult.endPoint);

        if (hitResult.hitOpponent)
        {
            sparringBot.knockbackVelocity = Vector3.zero;
            remoteAbilityLockedTimer = grid.hazmatDisruptDuration;
            grid.NotifyArenaDisrupt(grid.hazmatDisruptDuration);

            arenaHitsLanded++;
            arenaHitIndicatorTimer = 1.25f;
            arenaLastHitLabel = $"☣️ DISRUPTED! CHARGE / ROLL / MOBILITY CANCELED ({grid.hazmatDisruptDuration:F1}s) ☣️";
            remoteHitFlashTimer = 0.20f;
        }
    }

    private void FireMoonflowerSolarBolt(PlayerController pc)
    {
        arenaAbilityCooldownTimer = 0.80f;
        arenaPrimaryMaxCooldown = 0.80f;
        Camera aimCam = GetCharacterAimCamera(pc);
        if (aimCam == null) return;

        Vector3 start = pc.transform.position + new Vector3(0f, 0.55f, 0f) + aimCam.transform.forward * 0.45f;
        HitscanResult hitResult = HitscanRayUtility.PerformHitscan(
            aimCam,
            start,
            grid.GetMoonflowerSolarBoltConfig(),
            IsOpponentHitbox,
            c => c.GetComponent<PlayerController>() != null);

        grid.NotifyArenaHitscanFired(hitResult);
        grid.NotifyArenaAbilityFx("BEAM", start, hitResult.endPoint);

        if (hitResult.hitOpponent)
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

    private void TriggerMoonflowerOutburst(PlayerController pc)
    {
        arenaSecondaryCooldownTimer = grid.moonflowerBurstCooldown;
        arenaSecondaryMaxCooldown = grid.moonflowerBurstCooldown;
        Camera cam = grid.GetTargetCamera();

        Vector3 aimForward = (cam != null) ? cam.transform.forward : pc.transform.forward;
        Vector3 flatForward = new Vector3(aimForward.x, 0f, aimForward.z);
        if (flatForward.sqrMagnitude < 0.001f) flatForward = pc.transform.forward;
        flatForward.Normalize();

        Vector3 explosionCenter = pc.transform.position + flatForward * 0.45f;
        ArenaVfxFactory.SpawnMoonExplosion(arena3DRoot != null ? arena3DRoot.transform : null, explosionCenter);
        grid.NotifyArenaAbilityFx("MOON_BURST", explosionCenter, flatForward);

        pc.ApplyKnockback(-flatForward * 9.8f, 6.4f, suppressLandingSlow: true);

        if (remoteArenaAvatar == null) return;

        Physics.SyncTransforms();
        Vector3 oppPos = remoteArenaAvatar.transform.position;
        float dist = Vector3.Distance(explosionCenter, oppPos);
        if (dist > grid.moonflowerBurstRadius) return;

        if (dist > 1.6f &&
            Physics.Linecast(explosionCenter + Vector3.up * 0.6f, oppPos + Vector3.up * 0.6f, out RaycastHit blockHit) &&
            !IsOpponentHitbox(blockHit.collider) &&
            blockHit.collider.GetComponent<PlayerController>() == null)
        {
            return;
        }

        if (remoteIsBucketParrying)
        {
            RegisterArenaParriedByOpponent(grid.moonflowerBurstDamage);
            return;
        }

        DealDamageToOpponent(grid.moonflowerBurstDamage, $"LUNAR OUTBURST HIT ({grid.moonflowerBurstDamage} DMG)!", 0f);

        Vector3 pushDir = oppPos - explosionCenter;
        pushDir.y = 0f;
        pushDir = (pushDir.sqrMagnitude > 0.01f) ? pushDir.normalized : flatForward;
        Vector3 zombieKnockbackVel = (pushDir * 10.5f) + (Vector3.up * 6.5f);

        grid.NotifyArenaKnockback(zombieKnockbackVel);
        if (!hasRemoteArenaPose)
        {
            sparringBot.knockbackVelocity = zombieKnockbackVel;
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

        Camera aimCam = GetCharacterAimCamera(pc);
        if (aimCam == null) return;

        Vector3 shotStart = pc.transform.position + new Vector3(0f, 0.52f, 0f) + aimCam.transform.forward * 0.45f + aimCam.transform.right * 0.18f;
        HitscanAttackConfig config = isHeavyVolley ? grid.GetRoseHeavyVolleyConfig() : grid.GetRoseThornConfig();

        HitscanResult hitResult = HitscanRayUtility.PerformHitscan(
            aimCam,
            shotStart,
            config,
            IsOpponentHitbox,
            c => c.GetComponent<PlayerController>() != null);

        grid.NotifyArenaHitscanFired(hitResult);
        grid.NotifyArenaAbilityFx("THORN", shotStart, hitResult.endPoint);

        if (hitResult.hitOpponent)
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
        Camera cam = grid.GetTargetCamera();
        if (cam == null) return;

        Vector3 bashCenter = cam.transform.position + cam.transform.forward * 1.25f;
        ArenaVfxFactory.SpawnDurianSpikeBurst(arena3DRoot != null ? arena3DRoot.transform : null, bashCenter, cam.transform.rotation);
        grid.NotifyArenaAbilityFx("DURIAN_BASH", bashCenter, cam.transform.forward);

        if (CheckMeleeOrConeHitOpponent(pc, cam, 0.95f, 3.8f, 62f))
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

    private void PerformZombieMeleeSlash(PlayerController pc, int damage = 25, float cooldown = 0.8f, string hitLabel = "ZOMBIE MELEE SLASH!")
    {
        arenaAbilityCooldownTimer = cooldown;
        arenaPrimaryMaxCooldown = cooldown;
        Camera cam = grid.GetTargetCamera();
        if (cam == null) return;

        Vector3 slashCenter = cam.transform.position + cam.transform.forward * 1.15f;
        ArenaVfxFactory.SpawnSlashEffect(arena3DRoot != null ? arena3DRoot.transform : null, slashCenter, cam.transform.rotation);
        grid.NotifyArenaAbilityFx("SLASH", slashCenter, cam.transform.forward);

        if (CheckMeleeOrConeHitOpponent(pc, cam, 0.85f, 3.6f, 58f))
        {
            DealDamageToOpponent(damage, hitLabel, 0f);
        }
    }

    private bool CheckMeleeOrConeHitOpponent(PlayerController pc, Camera cam, float sphereRadius, float maxRange, float maxConeAngle)
    {
        Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (CheckCursorRayHitOpponent(aimRay, sphereRadius, maxRange, out _))
        {
            return true;
        }

        if (remoteArenaAvatar != null)
        {
            Vector3 toOpp = remoteArenaAvatar.transform.position - pc.transform.position;
            if (toOpp.magnitude <= maxRange && Vector3.Angle(cam.transform.forward, toOpp) <= maxConeAngle)
            {
                if (!Physics.Linecast(cam.transform.position, remoteArenaAvatar.transform.position + Vector3.up * 0.5f, out RaycastHit blockHit) ||
                    IsOpponentHitbox(blockHit.collider) ||
                    blockHit.collider.GetComponent<PlayerController>() != null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void DealDamageToOpponent(int damage, string label, float stunDuration)
    {
        arenaHitsLanded++;
        arenaHitIndicatorTimer = 0.95f;
        arenaLastHitLabel = label;
        remoteHitFlashTimer = 0.25f;

        remoteArenaCurrentHP = Mathf.Max(0, remoteArenaCurrentHP - damage);
        UpdateRemoteWorldHPBarScale();

        grid.NotifyArenaAttackHit(damage, stunDuration);

        if (remoteArenaCurrentHP <= 0)
        {
            bool localIsZombie = grid.GetEffectiveLocalRole() == MultiplayerMenuManager.PlayerRole.Zombie;
            ResolveArena(zombieWon: localIsZombie, notifyNetwork: true);
        }
    }

    private void RegisterArenaParriedByOpponent(int blockedDamage)
    {
        float bonusPct = Mathf.Clamp(blockedDamage * 1.8f, 25f, 140f);
        arenaHitIndicatorTimer = 1.2f;
        arenaLastHitLabel = $"🛡 BUCKETHEAD PARRIED YOUR {blockedDamage} DMG! (+{bonusPct:F0}% ZOMBIE SPEED) 🛡";
        grid.NotifyArenaAttackHit(blockedDamage, 0f);
    }

    private void UpdateRemoteWorldHPBarScale()
    {
        if (remoteHpBarFill == null) return;
        float pct = Mathf.Clamp01((float)remoteArenaCurrentHP / Mathf.Max(1, remoteArenaMaxHP));
        remoteHpBarFill.localScale = new Vector3(1.24f * pct, 0.12f, 1f);
        remoteHpBarFill.localPosition = new Vector3(-0.62f * (1f - pct), 0f, -0.01f);
    }

    // ==================== RAYCAST & HITBOX HELPERS ====================

    private Camera GetCharacterAimCamera(PlayerController pc)
    {
        if (pc != null && pc.playerCamera != null) return pc.playerCamera;
        return grid != null ? grid.GetTargetCamera() : Camera.main;
    }

    private void DispatchHitscanVisuals(HitscanAttackConfig config, Vector3 start, Vector3 end, bool hitOpponent)
    {
        Vector3 dir = end - start;
        float dist = dir.magnitude;
        HitscanResult fxResult = new HitscanResult
        {
            abilityId = config != null ? config.AbilityName : string.Empty,
            aimRay = dist > 0.0001f ? new Ray(start, dir / dist) : new Ray(start, Vector3.forward),
            startPoint = start,
            endPoint = end,
            didHit = hitOpponent,
            hitOpponent = hitOpponent,
            hitCollider = null,
            hitNormal = dist > 0.0001f ? (-dir / dist) : Vector3.back,
            hitDistance = dist
        };
        config?.TriggerVisuals(fxResult);
        grid?.NotifyArenaHitscanFired(fxResult);
    }

    private bool TryRaycastArenaBeam(Vector3 start, Vector3 target, out Vector3 beamEnd)
    {
        Physics.SyncTransforms();
        Vector3 dir = target - start;
        float dist = dir.magnitude;
        bool blockedByObstacle = false;
        beamEnd = target;

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
        return !blockedByObstacle;
    }

    private bool CheckCursorRayHitOpponent(Ray aimRay, float sphereRadius, float maxDist, out Vector3 endPoint)
    {
        Physics.SyncTransforms();

        endPoint = aimRay.GetPoint(maxDist);
        bool hitOpponent = false;
        float closestEnvDist = maxDist;
        float closestOpponentDist = maxDist;

        RaycastHit[] directHits = Physics.RaycastAll(aimRay, maxDist);
        for (int i = 0; i < directHits.Length; i++)
        {
            Collider c = directHits[i].collider;
            if (c == null || c.GetComponent<PlayerController>() != null) continue;

            if (!IsOpponentHitbox(c) && directHits[i].distance < closestEnvDist)
            {
                closestEnvDist = directHits[i].distance;
                endPoint = directHits[i].point;
            }
        }

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

        if (hitOpponent) return true;

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
                        if (lc == null || lc.GetComponent<PlayerController>() != null) continue;
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
}
