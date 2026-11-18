using UnityEngine;

/// <summary>
/// Renders the 3D 1v1 Arena HUD (top HP bars, crosshair, hit indicators,
/// active ability progress bars, cooldown boxes, status banners, and winner overlay).
/// </summary>
public class ArenaHudRenderer
{
    public struct ArenaHudState
    {
        public bool isZombie;
        public PlantType activePlant;
        public ZombieType activeZombie;
        public string presetName;
        public int localHP;
        public int localMaxHP;
        public int remoteHP;
        public int remoteMaxHP;
        public int hitsLanded;
        public float hitIndicatorTimer;
        public string lastHitLabel;
        public float remoteHitFlashTimer;
        public float primaryCooldown;
        public float primaryMaxCooldown;
        public float secondaryCooldown;
        public float secondaryMaxCooldown;
        public float localAbilityLockTimer;
        public float remoteAbilityLockedTimer;
        public float remoteImmobilizedTimer;
        public float hazmatDisruptDuration;
        public bool isChargingRoseThorn;
        public float roseThornChargeTimer;
        public float roseThornChargeDuration;
        public bool isDurianRolling;
        public float durianRollTimer;
        public float durianRollDuration;
        public bool isBucketParrying;
        public float bucketParryTimer;
        public float bucketParryDuration;
        public float bucketParrySpeedDuration;
        public bool isBunnySliding;
        public float bunnySlideTimer;
        public float bunnySlideDuration;
        public bool isFeralLunging;
        public float feralLungeTimer;
        public float feralLungeDuration;
        public bool arenaResolving;
        public bool arenaZombieWon;
        public PlayerController playerController;
        public GameConfigSO gameConfig;
    }

    private static GUIStyle s_ArenaHudStyle;
    private static GUIStyle s_HitStyle;
    private static GUIStyle s_StunStyle;
    private static GUIStyle s_OppStunStyle;
    private static GUIStyle s_WinStyle;
    private static GUIStyle s_HpTextStyle;
    private static GUIStyle s_ChargeBarTextStyle;
    private static GUIStyle s_BtnStyle;

    public void Draw(in ArenaHudState state)
    {
        EnsureStyles();

        Color prevBg = GUI.backgroundColor;
        float sw = Screen.width;
        float sh = Screen.height;
        float cx = sw * 0.5f;
        float cy = sh * 0.5f;

        string zombieDisplayName = GetZombieDisplayName(state.activeZombie);
        string myTitle = state.isZombie ? $"YOU ({zombieDisplayName})" : $"YOU ({state.activePlant.ToString().ToUpper()})";
        string oppTitle = state.isZombie ? $"OPPONENT ({state.activePlant.ToString().ToUpper()})" : $"OPPONENT ({zombieDisplayName})";

        GetAbilityStrings(state, out string abilityHint, out string primaryTitle, out string secondaryTitle);

        GUI.backgroundColor = new Color(0.10f, 0.14f, 0.22f, 0.88f);
        GUI.Box(new Rect(cx - 360f, 12f, 720f, 54f),
            $"3D 1v1 ARENA ({state.presetName}) • {abilityHint}\nWASD: Move | Space: Jump | R: Reroll Layout | Hits: {state.hitsLanded} | Host: N/M = Win",
            s_ArenaHudStyle);

        Color myBarCol = state.isZombie ? new Color(0.78f, 0.32f, 0.96f) : new Color(0.25f, 0.90f, 0.38f);
        Color oppBarCol = state.isZombie ? new Color(0.25f, 0.90f, 0.38f) : new Color(0.78f, 0.32f, 0.96f);
        if (state.remoteHitFlashTimer > 0f)
        {
            oppBarCol = new Color(1.0f, 0.95f, 0.35f);
        }

        DrawArenaHPBar(new Rect(18f, 14f, 255f, 48f), myTitle, state.localHP, state.localMaxHP, myBarCol);
        DrawArenaHPBar(new Rect(sw - 273f, 14f, 255f, 48f), oppTitle, state.remoteHP, state.remoteMaxHP, oppBarCol);

        DrawCrosshairAndHitMarker(cx, cy, state.hitIndicatorTimer > 0f, state.lastHitLabel, state.hitsLanded);
        DrawActiveAbilityProgressBars(cx, cy, state);

        bool abilitiesLocked = state.localAbilityLockTimer > 0f;
        DrawArenaAbilityCooldownBox(
            new Rect(cx - 178f, sh - 78f, 170f, 60f),
            primaryTitle,
            state.primaryCooldown,
            state.primaryMaxCooldown,
            new Color(0.22f, 0.72f, 0.95f),
            abilitiesLocked,
            state.localAbilityLockTimer,
            state.hazmatDisruptDuration);

        DrawArenaAbilityCooldownBox(
            new Rect(cx + 8f, sh - 78f, 170f, 60f),
            secondaryTitle,
            state.secondaryCooldown,
            state.secondaryMaxCooldown,
            new Color(0.88f, 0.48f, 0.98f),
            abilitiesLocked,
            state.localAbilityLockTimer,
            state.hazmatDisruptDuration);

        DrawStatusBanners(cx, cy, state);

        if (state.arenaResolving)
        {
            s_WinStyle.normal.textColor = state.arenaZombieWon ? new Color(0.8f, 0.45f, 1f) : new Color(0.5f, 1f, 0.45f);
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.85f);
            float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.04f;
            float w = 560f * pulse;
            float h = 130f * pulse;
            GUI.Box(new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h),
                state.arenaZombieWon ? "ZOMBIE WON!" : "PLANT WON!", s_WinStyle);
        }

        GUI.backgroundColor = prevBg;
    }

    private static string GetZombieDisplayName(ZombieType type)
    {
        switch (type)
        {
            case ZombieType.Buckethead: return "BUCKETHEAD ZOMBIE";
            case ZombieType.Bunny: return "BUNNY ZOMBIE";
            case ZombieType.Hazmat: return "HAZMAT ZOMBIE";
            default: return "NORMAL ZOMBIE";
        }
    }

    private static void GetAbilityStrings(in ArenaHudState state, out string abilityHint, out string primaryTitle, out string secondaryTitle)
    {
        if (state.isZombie)
        {
            ZombieDefinitionSO zDef = state.gameConfig != null ? state.gameConfig.GetZombieDefinition(state.activeZombie) : null;
            abilityHint = zDef != null && !string.IsNullOrEmpty(zDef.arenaAbilityHint)
                ? zDef.arenaAbilityHint
                : "[L-Click / E] Claw Slash  •  [R-Click / Q] Feral Dive Lunge";
            primaryTitle = zDef != null && !string.IsNullOrEmpty(zDef.primaryAbilityTitle)
                ? zDef.primaryAbilityTitle
                : "Claw Slash [LMB/E]\n25 DMG";
            secondaryTitle = zDef != null && !string.IsNullOrEmpty(zDef.secondaryAbilityTitle)
                ? zDef.secondaryAbilityTitle
                : "Feral Lunge [RMB/Q]\n38 DMG";

            if (state.activeZombie == ZombieType.Buckethead && state.isBucketParrying)
            {
                secondaryTitle = $"PARRY ACTIVE!\n{state.bucketParryTimer:F1}s";
            }
            else if (state.activeZombie == ZombieType.Bunny && state.isBunnySliding)
            {
                secondaryTitle = $"SLIDING!\n{state.bunnySlideTimer:F1}s";
            }
            else if (state.activeZombie == ZombieType.Normal && state.isFeralLunging)
            {
                secondaryTitle = $"DIVING!\n{state.feralLungeTimer:F1}s";
            }
        }
        else
        {
            PlantDefinitionSO pDef = state.gameConfig != null ? state.gameConfig.GetPlantDefinition(state.activePlant) : null;
            abilityHint = pDef != null && !string.IsNullOrEmpty(pDef.arenaAbilityHint)
                ? pDef.arenaAbilityHint
                : "[L-Click] Primary  •  [R-Click / E] Secondary";
            primaryTitle = pDef != null && !string.IsNullOrEmpty(pDef.primaryAbilityTitle)
                ? pDef.primaryAbilityTitle
                : "Primary [LMB]";
            secondaryTitle = pDef != null && !string.IsNullOrEmpty(pDef.secondaryAbilityTitle)
                ? pDef.secondaryAbilityTitle
                : "Secondary [RMB/E]";
        }
    }

    private static void DrawCrosshairAndHitMarker(float cx, float cy, bool hitActive, string lastHitLabel, int hitsLanded)
    {
        Color prevColor = GUI.color;
        GUI.color = hitActive ? new Color(1.0f, 0.30f, 0.30f, 0.98f) : new Color(0.95f, 1.0f, 0.95f, 0.92f);

        GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 14f, cy - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx + 6f, cy - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy - 14f, 2f, 8f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy + 6f, 2f, 8f), Texture2D.whiteTexture);

        if (hitActive)
        {
            GUI.color = new Color(1.0f, 0.88f, 0.25f, 0.98f);
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
                $"★ {lastHitLabel} (Hits: {hitsLanded}) ★", s_HitStyle);
        }

        GUI.color = prevColor;
    }

    private static void DrawActiveAbilityProgressBars(float cx, float cy, in ArenaHudState state)
    {
        if (state.isChargingRoseThorn)
        {
            float pct = 1.0f - Mathf.Clamp01(state.roseThornChargeTimer / Mathf.Max(0.01f, state.roseThornChargeDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, "CHARGING THORN...", new Color(1.0f, 0.25f, 0.45f));
        }
        else if (state.isDurianRolling)
        {
            float pct = Mathf.Clamp01(state.durianRollTimer / Mathf.Max(0.01f, state.durianRollDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, "DURIAN ROLLING! (RAM ZOMBIE TO IMMOBILIZE)", new Color(0.95f, 0.80f, 0.25f));
        }
        else if (state.isBucketParrying)
        {
            float pct = Mathf.Clamp01(state.bucketParryTimer / Mathf.Max(0.01f, state.bucketParryDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, $"🛡️ IRON BUCKET PARRY ACTIVE ({state.bucketParryTimer:F1}s) 🛡️", new Color(0.35f, 0.95f, 1.0f));
        }
        else if (state.isBunnySliding)
        {
            float pct = Mathf.Clamp01(state.bunnySlideTimer / Mathf.Max(0.01f, state.bunnySlideDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, $"🐇 BUNNY SLIDING! LOW HITBOX + 1.48x SPEED ({state.bunnySlideTimer:F1}s)", new Color(0.25f, 0.95f, 0.88f));
        }
        else if (state.isFeralLunging)
        {
            float pct = Mathf.Clamp01(state.feralLungeTimer / Mathf.Max(0.01f, state.feralLungeDuration));
            DrawCenterChargeBar(cx, cy - 36f, pct, $"🧟 FERAL DIVE LUNGE! POUNCING AT PLANT ({state.feralLungeTimer:F1}s)", new Color(0.92f, 0.35f, 1.0f));
        }

        if (state.playerController != null && state.playerController.parrySpeedBonusTimer > 0f)
        {
            float pct = Mathf.Clamp01(state.playerController.parrySpeedBonusTimer / Mathf.Max(0.01f, state.bucketParrySpeedDuration));
            int bonusPct = Mathf.RoundToInt((state.playerController.parrySpeedBonusMultiplier - 1.0f) * 100f);
            DrawCenterChargeBar(cx, cy - 62f, pct, $"⚡ PARRY SPEED BOOST +{bonusPct}% ({state.playerController.parrySpeedBonusTimer:F1}s) ⚡", new Color(1.0f, 0.85f, 0.25f));
        }
    }

    private static void DrawStatusBanners(float cx, float cy, in ArenaHudState state)
    {
        if (state.playerController != null && state.playerController.IsImmobilized)
        {
            GUI.backgroundColor = new Color(0.28f, 0.08f, 0.05f, 0.94f);
            GUI.Box(new Rect(cx - 250f, cy - 95f, 500f, 46f),
                $"⚡ IMMOBILIZED BY DURIAN! ({state.playerController.immobilizedTimer:F1}s) — Movement & Attacks Locked ⚡", s_StunStyle);
        }
        else if (state.localAbilityLockTimer > 0f)
        {
            GUI.backgroundColor = new Color(0.10f, 0.24f, 0.08f, 0.94f);
            GUI.Box(new Rect(cx - 250f, cy - 95f, 500f, 46f),
                $"☣️ ABILITIES DISRUPTED! ({state.localAbilityLockTimer:F1}s) — Charges, Roll & Mobility Locked ☣️", s_StunStyle);
        }
        else if (state.remoteImmobilizedTimer > 0f)
        {
            GUI.backgroundColor = new Color(0.08f, 0.24f, 0.12f, 0.90f);
            GUI.Box(new Rect(cx - 210f, cy + 64f, 420f, 30f),
                $"ZOMBIE IMMOBILIZED & UNABLE TO ATTACK ({state.remoteImmobilizedTimer:F1}s)", s_OppStunStyle);
        }
        else if (state.remoteAbilityLockedTimer > 0f)
        {
            GUI.backgroundColor = new Color(0.08f, 0.24f, 0.12f, 0.90f);
            GUI.Box(new Rect(cx - 210f, cy + 64f, 420f, 30f),
                $"PLANT ABILITIES DISRUPTED ({state.remoteAbilityLockedTimer:F1}s)", s_OppStunStyle);
        }
    }

    private static void DrawArenaAbilityCooldownBox(
        Rect rect,
        string title,
        float cdRemaining,
        float cdMax,
        Color readyTint,
        bool isLocked,
        float lockRemaining,
        float lockMax)
    {
        Color prevBg = GUI.backgroundColor;
        Color prevColor = GUI.color;

        bool isReady = !isLocked && cdRemaining <= 0.005f;
        GUI.backgroundColor = isLocked
            ? new Color(0.18f, 0.32f, 0.14f, 0.95f)
            : (isReady ? readyTint : new Color(0.25f, 0.25f, 0.28f, 0.95f));

        string status = isLocked
            ? $"LOCKED: {lockRemaining:F1}s"
            : (isReady ? "READY" : $"CD: {cdRemaining:F1}s");
        GUI.Box(rect, $"{title}\n{status}", s_BtnStyle);

        float sweepRem = isLocked ? Mathf.Max(cdRemaining, lockRemaining) : cdRemaining;
        float sweepMax = isLocked ? Mathf.Max(cdMax, lockMax) : cdMax;
        if ((isLocked || !isReady) && sweepMax > 0.01f)
        {
            float ratio = Mathf.Clamp01(sweepRem / sweepMax);
            GUI.color = new Color(0.02f, 0.02f, 0.05f, 0.65f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, rect.height * ratio), Texture2D.whiteTexture);
            GUI.color = prevColor;
        }

        GUI.backgroundColor = prevBg;
    }

    private static void DrawArenaHPBar(Rect rect, string label, int currentHP, int maxHP, Color fillColor)
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

    private static void DrawCenterChargeBar(float cx, float y, float pct, string text, Color fillCol)
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

    private static void EnsureStyles()
    {
        if (s_ArenaHudStyle != null) return;

        s_ArenaHudStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_ArenaHudStyle.normal.textColor = Color.white;

        s_HitStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_HitStyle.normal.textColor = new Color(1.0f, 0.95f, 0.35f);

        s_StunStyle = new GUIStyle(GUI.skin.box) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_StunStyle.normal.textColor = new Color(1.0f, 0.85f, 0.25f);

        s_OppStunStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_OppStunStyle.normal.textColor = new Color(0.45f, 1.0f, 0.55f);

        s_WinStyle = new GUIStyle(GUI.skin.box) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        s_HpTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_HpTextStyle.normal.textColor = Color.white;

        s_ChargeBarTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_ChargeBarTextStyle.normal.textColor = Color.white;

        s_BtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }
}
