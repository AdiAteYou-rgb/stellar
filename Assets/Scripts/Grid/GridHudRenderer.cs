using UnityEngine;

/// <summary>
/// Renders all 2D gameplay HUD elements (Moon/Brain banks, unit seed packet buttons,
/// 5-minute defense countdown timer, clash transition banner, and Game Over modal).
/// </summary>
public class GridHudRenderer : MonoBehaviour
{
    private GridManager grid;

    public readonly Rect MoonBankRect = new Rect(12f, 12f, 106f, 66f);
    public readonly Rect[] PlantButtonRects = new Rect[3]
    {
        new Rect(122f, 12f, 124f, 66f),
        new Rect(250f, 12f, 124f, 66f),
        new Rect(378f, 12f, 124f, 66f)
    };
    public readonly Rect BrainBankRect = new Rect(12f, 82f, 106f, 66f);

    private static GUIStyle s_ClashStyle;
    private static GUIStyle s_TimerStyle;
    private static GUIStyle s_BankStyle;
    private static GUIStyle s_PlantBtnStyle;
    private static GUIStyle s_ZombieBtnStyle;
    private static GUIStyle s_GameOverTitleStyle;
    private static GUIStyle s_GameOverDescStyle;
    private static GUIStyle s_GameOverBtnStyle;

    public void Initialize(GridManager manager)
    {
        grid = manager;
    }

    public Rect GetZombieButtonRect(int index)
    {
        if (grid != null && grid.canPlacePlants)
        {
            return new Rect(122f + index * 122f, 82f, 118f, 66f);
        }
        return new Rect(126f + index * 124f, 12f, 120f, 66f);
    }

    public bool IsPointOverTopHud(Vector2 guiMousePos)
    {
        if (grid == null) return false;

        if (grid.canPlacePlants)
        {
            if (MoonBankRect.Contains(guiMousePos)) return true;
            for (int i = 0; i < PlantButtonRects.Length; i++)
            {
                if (PlantButtonRects[i].Contains(guiMousePos)) return true;
            }
        }

        if (grid.canPlaceZombies)
        {
            Rect actualBrainRect = grid.canPlacePlants ? BrainBankRect : new Rect(12f, 12f, 110f, 66f);
            if (actualBrainRect.Contains(guiMousePos)) return true;
            for (int i = 0; i < 4; i++)
            {
                if (GetZombieButtonRect(i).Contains(guiMousePos)) return true;
            }
        }

        return false;
    }

    public Vector3 GetMoonBankWorldPosition(Camera targetCamera)
    {
        if (targetCamera == null) targetCamera = Camera.main;

        if (targetCamera != null && grid != null)
        {
            Vector2 bankCenterGui = MoonBankRect.center;
            Vector3 screenPt = new Vector3(bankCenterGui.x, Screen.height - bankCenterGui.y, 0f);
            Ray ray = targetCamera.ScreenPointToRay(screenPt);
            Vector3 wallFrontCenter = transform.position - transform.forward * (grid.wallThickness * 0.5f + 0.4f);
            Plane plane = new Plane(-transform.forward, wallFrontCenter);
            if (plane.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }
        }

        float w = grid != null ? grid.wallWidth : 16f;
        float h = grid != null ? grid.wallHeight : 9f;
        return new Vector3(
            transform.position.x - w * 0.42f,
            transform.position.y + h * 0.40f,
            transform.position.z - 0.5f
        );
    }

    public void DrawClashZoomBanner()
    {
        EnsureStyles();
        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.15f, 0.05f, 0.05f, 0.92f);
        GUI.Box(new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.5f - 45f, 520f, 90f),
            "ZOMBIE TOUCHED PLANT!\n2D World Paused - Entering 3D 1v1 Arena...", s_ClashStyle);
        GUI.backgroundColor = prevBg;
    }

    public void Draw2DGameplayHUD()
    {
        if (grid == null) return;
        EnsureStyles();

        if (grid.matchResult != GridManager.MatchResult.InProgress)
        {
            DrawGameOverModal();
            return;
        }

        Color prevBg = GUI.backgroundColor;
        float sw = Screen.width;

        float minTimerX = grid.canPlacePlants
            ? (PlantButtonRects[2].x + PlantButtonRects[2].width + 16f)
            : (grid.canPlaceZombies ? (126f + 4f * 124f + 12f) : 285f);
        float maxTimerX = sw - 245f - 210f;
        float timerX = (maxTimerX > minTimerX) ? (minTimerX + (maxTimerX - minTimerX) * 0.5f) : minTimerX;
        Rect timerRect = new Rect(timerX, 12f, 210f, 66f);

        int mins = Mathf.FloorToInt(grid.matchTimeRemaining / 60f);
        int secs = Mathf.FloorToInt(grid.matchTimeRemaining % 60f);
        string timeStr = $"{mins:D2}:{secs:D2}";

        if (grid.matchTimeRemaining <= 30f)
        {
            s_TimerStyle.normal.textColor = new Color(1.0f, 0.35f, 0.35f);
            GUI.backgroundColor = new Color(0.35f, 0.08f, 0.08f, 0.95f);
        }
        else if (grid.matchTimeRemaining <= 60f)
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

        if (grid.canPlacePlants)
        {
            s_BankStyle.normal.textColor = new Color(1.0f, 0.95f, 0.4f);
            GUI.backgroundColor = new Color(0.12f, 0.18f, 0.32f, 0.95f);
            GUI.Box(MoonBankRect, $"MOONS\n{grid.currentMoons}", s_BankStyle);

            DrawPlantSeedButton(0, PlantType.Moonflower, "Moonflower [1]", grid.moonflowerCost, new Color(0.4f, 0.85f, 1.0f));
            DrawPlantSeedButton(1, PlantType.Rose, "Rose [2]", grid.roseCost, new Color(1.0f, 0.4f, 0.5f));
            DrawPlantSeedButton(2, PlantType.Durian, "Durian [3]", grid.durianCost, new Color(0.9f, 0.78f, 0.3f));
        }

        if (grid.canPlaceZombies)
        {
            Rect actualBrainRect = grid.canPlacePlants ? BrainBankRect : new Rect(12f, 12f, 110f, 66f);

            s_BankStyle.normal.textColor = new Color(0.95f, 0.55f, 1.0f);
            GUI.backgroundColor = new Color(0.24f, 0.10f, 0.28f, 0.95f);
            GUI.Box(actualBrainRect, $"BRAINS\n{grid.currentBrains}", s_BankStyle);

            DrawZombieSeedButton(0, ZombieType.Normal, "Zombie [4]", grid.zombieCost, new Color(0.68f, 0.42f, 0.88f));
            DrawZombieSeedButton(1, ZombieType.Buckethead, "Buckethead [5]", grid.bucketheadCost, new Color(0.62f, 0.72f, 0.84f));
            DrawZombieSeedButton(2, ZombieType.Bunny, "Bunny Speed [6]", grid.bunnyCost, new Color(0.22f, 0.86f, 0.84f));
            DrawZombieSeedButton(3, ZombieType.Hazmat, "Hazmat [7]", grid.hazmatCost, new Color(0.55f, 0.95f, 0.25f));
        }

        GUI.backgroundColor = prevBg;
    }

    private void DrawPlantSeedButton(int index, PlantType type, string title, int cost, Color readyTint)
    {
        Rect rect = PlantButtonRects[index];
        float cd = grid.GetPlantCooldownRemaining(type);
        float maxCd = grid.GetPlantMaxCooldown(type);
        bool enoughMoons = grid.currentMoons >= cost;
        bool isReady = cd <= 0.001f && enoughMoons;
        bool isSelected = grid.isPlantSelected && grid.selectedPlantType == type;

        GUI.backgroundColor = isSelected
            ? new Color(0.25f, 1.0f, 0.35f)
            : (isReady ? readyTint : new Color(0.45f, 0.45f, 0.45f));

        string statusLine = cd > 0.001f
            ? $"RECHARGING {cd:F1}s"
            : (!enoughMoons ? $"Need {cost} 🌙" : (isSelected ? "SELECTED ✓" : "READY"));

        if (GUI.Button(rect, $"{title}\n{cost} 🌙 • {statusLine}", s_PlantBtnStyle))
        {
            grid.TrySelectPlant(type);
        }

        DrawCooldownCurtain(rect, cd, maxCd);
    }

    private void DrawZombieSeedButton(int index, ZombieType type, string title, int cost, Color readyTint)
    {
        Rect targetRect = GetZombieButtonRect(index);
        float cd = grid.GetZombieCooldownRemaining(type);
        float maxCd = grid.GetZombieMaxCooldown(type);
        bool enoughBrains = grid.currentBrains >= cost;
        bool isReady = cd <= 0.001f && enoughBrains;
        bool isSelected = grid.isZombieSelected && grid.selectedZombieType == type;

        GUI.backgroundColor = isSelected
            ? new Color(0.85f, 0.3f, 1.0f)
            : (isReady ? readyTint : new Color(0.45f, 0.45f, 0.45f));

        string statusLine = cd > 0.001f
            ? $"RECHARGING {cd:F1}s"
            : (!enoughBrains ? $"Need {cost} 🧠" : (isSelected ? "SELECTED ✓" : "READY"));

        if (GUI.Button(targetRect, $"{title}\n{cost} 🧠 • {statusLine}", s_ZombieBtnStyle))
        {
            grid.TrySelectZombie(type);
        }

        DrawCooldownCurtain(targetRect, cd, maxCd);
    }

    private static void DrawCooldownCurtain(Rect rect, float cd, float maxCd)
    {
        if (cd > 0.001f && maxCd > 0.01f)
        {
            Color prevCol = GUI.color;
            float cdRatio = Mathf.Clamp01(cd / maxCd);
            GUI.color = new Color(0.02f, 0.02f, 0.05f, 0.62f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, rect.height * cdRatio), Texture2D.whiteTexture);
            GUI.color = prevCol;
        }
    }

    private void DrawGameOverModal()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        Color prevBg = GUI.backgroundColor;

        GUI.backgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.78f);
        GUI.Box(new Rect(0f, 0f, sw, sh), GUIContent.none);

        Rect boxRect = new Rect((sw - 580f) * 0.5f, (sh - 280f) * 0.5f, 580f, 280f);
        bool plantsWon = (grid.matchResult == GridManager.MatchResult.PlantsWin);

        GUI.backgroundColor = plantsWon
            ? new Color(0.06f, 0.22f, 0.10f, 0.96f)
            : new Color(0.26f, 0.06f, 0.12f, 0.96f);
        GUI.Box(boxRect, GUIContent.none);

        string title;
        if (plantsWon)
        {
            title = (grid.localRole == MultiplayerMenuManager.PlayerRole.Plant)
                ? "VICTORY! (PLANTS WIN)"
                : (grid.localRole == MultiplayerMenuManager.PlayerRole.Zombie ? "DEFEAT! (PLANTS WIN)" : "PLANTS WIN!");
        }
        else
        {
            title = (grid.localRole == MultiplayerMenuManager.PlayerRole.Zombie)
                ? "VICTORY! (ZOMBIES WIN)"
                : (grid.localRole == MultiplayerMenuManager.PlayerRole.Plant ? "DEFEAT! (ZOMBIES WIN)" : "ZOMBIES WIN!");
        }

        s_GameOverTitleStyle.normal.textColor = plantsWon ? new Color(0.35f, 1.0f, 0.45f) : new Color(1.0f, 0.35f, 0.35f);
        GUI.Label(new Rect(boxRect.x, boxRect.y + 22f, boxRect.width, 42f), title, s_GameOverTitleStyle);

        s_GameOverDescStyle.normal.textColor = plantsWon ? new Color(0.9f, 0.98f, 0.9f) : new Color(1.0f, 0.9f, 0.9f);
        string description = plantsWon
            ? "The 5-minute defense timer reached 00:00!\nThe Plants stood strong and successfully defended the garden wall against the zombie assault!"
            : "A Zombie crossed the defense line of the board!\nThe zombies breached your perimeter and ate your brains!";
        GUI.Label(new Rect(boxRect.x + 30f, boxRect.y + 75f, boxRect.width - 60f, 70f), description, s_GameOverDescStyle);

        float btnWidth = 220f;
        float btnHeight = 55f;
        float btnGap = 24f;
        float buttonsStartX = boxRect.x + (boxRect.width - (btnWidth * 2f + btnGap)) * 0.5f;
        float btnY = boxRect.y + 185f;

        GUI.backgroundColor = new Color(0.24f, 0.68f, 0.95f);
        if (GUI.Button(new Rect(buttonsStartX, btnY, btnWidth, btnHeight), "MAIN MENU", s_GameOverBtnStyle))
        {
            grid.GoToMainMenuFromGameOver();
        }

        GUI.backgroundColor = new Color(0.85f, 0.28f, 0.32f);
        if (GUI.Button(new Rect(buttonsStartX + btnWidth + btnGap, btnY, btnWidth, btnHeight), "EXIT GAME", s_GameOverBtnStyle))
        {
            grid.ExitGameFromGameOver();
        }

        GUI.backgroundColor = prevBg;
    }

    private static void EnsureStyles()
    {
        if (s_ClashStyle != null) return;

        s_ClashStyle = new GUIStyle(GUI.skin.box) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_ClashStyle.normal.textColor = new Color(1f, 0.9f, 0.3f);

        s_TimerStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_BankStyle = new GUIStyle(GUI.skin.box) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_PlantBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_ZombieBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_GameOverTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s_GameOverDescStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        s_GameOverBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }
}
