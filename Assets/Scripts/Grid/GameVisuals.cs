using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 2D Asset & Visual Generator for Plants vs. Zombies wall defense.
/// Generates crisp, high-definition, anti-aliased 2D sprites for Moonflower, Rose, Durian,
/// Zombie, Collectible Moons, and Rose Projectiles, with full alpha blending and cached sprites.
/// </summary>
public static class GameVisuals
{
    private static Material sharedLitMaterial;
    private static readonly Dictionary<string, Sprite> cachedSprites = new Dictionary<string, Sprite>();

    public static Material GetColoredMaterial(Color color, float smoothness = 0.35f)
    {
        if (sharedLitMaterial == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Sprites/Default");
            sharedLitMaterial = new Material(s);
        }

        Material mat = new Material(sharedLitMaterial);
        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", smoothness);
        }
        return mat;
    }

    // ==================== 2D SPRITE PUBLIC FACTORIES ====================

    public static GameObject CreatePlant(PlantType type, string name, bool isGhost, float tileWidth, float tileHeight)
    {
        EnsureSpritesLoaded();
        GameObject root = new GameObject(name);

        SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
        Sprite spr = GetPlantSprite(type);
        sr.sprite = spr;
        sr.sortingOrder = 10;
        if (isGhost)
        {
            sr.color = new Color(1.0f, 1.0f, 1.0f, 0.55f);
        }

        // Scale sprite to comfortably fit ~92% of the tile height
        float targetHeight = tileHeight * 0.92f;
        float sprHeightUnits = spr != null ? (spr.rect.height / spr.pixelsPerUnit) : 1.0f;
        float s = targetHeight / Mathf.Max(0.01f, sprHeightUnits);
        root.transform.localScale = new Vector3(s, s, 1.0f);

        if (!isGhost)
        {
            PlantBobber bobber = root.AddComponent<PlantBobber>();
            if (type == PlantType.Moonflower)
            {
                bobber.breatheSpeed = 2.2f;
                bobber.swaySpeed = 1.8f;
                bobber.swayAngle = 3.0f;
            }
            else if (type == PlantType.Rose || type == PlantType.Roze)
            {
                bobber.breatheSpeed = 2.6f;
                bobber.swaySpeed = 2.4f;
                bobber.swayAngle = 3.5f;
            }
            else if (type == PlantType.Durian)
            {
                bobber.breatheSpeed = 1.6f;
                bobber.swaySpeed = 1.2f;
                bobber.swayAngle = 2.0f;
            }
        }

        return root;
    }

    public static GameObject CreateZombie(string name, bool isGhost, float tileWidth, float tileHeight)
    {
        return CreateZombie(ZombieType.Normal, name, isGhost, tileWidth, tileHeight);
    }

    public static GameObject CreateZombie(ZombieType type, string name, bool isGhost, float tileWidth, float tileHeight)
    {
        EnsureSpritesLoaded();
        GameObject root = new GameObject(name);

        SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
        Sprite spr = GetZombieSprite(type);
        sr.sprite = spr;
        sr.sortingOrder = 15;
        if (isGhost)
        {
            sr.color = new Color(1.0f, 1.0f, 1.0f, 0.55f);
        }

        // Scale sprite to fit tile height
        float targetHeight = tileHeight * 0.98f;
        float sprHeightUnits = spr != null ? (spr.rect.height / spr.pixelsPerUnit) : 1.0f;
        float s = targetHeight / Mathf.Max(0.01f, sprHeightUnits);
        root.transform.localScale = new Vector3(s, s, 1.0f);

        return root;
    }

    public static GameObject CreateMoon(string name, float scale = 0.5f)
    {
        EnsureSpritesLoaded();
        GameObject root = new GameObject(name);

        SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
        Sprite spr = GetMoonSprite();
        sr.sprite = spr;
        sr.sortingOrder = 25;

        float s = scale * 2.2f;
        root.transform.localScale = new Vector3(s, s, 1.0f);
        return root;
    }

    public static GameObject CreateRoseProjectile(string name, float scale = 0.35f)
    {
        EnsureSpritesLoaded();
        GameObject root = new GameObject(name);

        SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
        Sprite spr = GetRoseProjectileSprite();
        sr.sprite = spr;
        sr.sortingOrder = 20;

        float s = scale * 1.8f;
        root.transform.localScale = new Vector3(s, s, 1.0f);
        return root;
    }

    // ==================== SPRITE ACCESSORS & CACHE ====================

    public static Sprite GetPlantSprite(PlantType type)
    {
        EnsureSpritesLoaded();
        switch (type)
        {
            case PlantType.Moonflower:
                return cachedSprites.TryGetValue("Moonflower", out var mf) ? mf : null;
            case PlantType.Rose:
                return cachedSprites.TryGetValue("Rose", out var r) ? r : null;
            case PlantType.Durian:
            default:
                return cachedSprites.TryGetValue("Durian", out var d) ? d : null;
        }
    }

    public static Sprite GetZombieSprite(ZombieType type = ZombieType.Normal)
    {
        EnsureSpritesLoaded();
        switch (type)
        {
            case ZombieType.Buckethead:
                return cachedSprites.TryGetValue("BucketheadZombie", out var bz) ? bz : (cachedSprites.TryGetValue("Zombie", out var z1) ? z1 : null);
            case ZombieType.Bunny:
                return cachedSprites.TryGetValue("BunnyZombie", out var rbz) ? rbz : (cachedSprites.TryGetValue("Zombie", out var z2) ? z2 : null);
            case ZombieType.Normal:
            default:
                return cachedSprites.TryGetValue("Zombie", out var z) ? z : null;
        }
    }

    public static Sprite GetMoonSprite()
    {
        EnsureSpritesLoaded();
        return cachedSprites.TryGetValue("Moon", out var m) ? m : null;
    }

    public static Sprite GetRoseProjectileSprite()
    {
        EnsureSpritesLoaded();
        return cachedSprites.TryGetValue("RoseProjectile", out var rp) ? rp : null;
    }

    public static void EnsureSpritesLoaded()
    {
        if (cachedSprites.Count >= 8) return;

        LoadOrGenerateSprite("Moonflower", 256, 256, new Vector2(0.5f, 0.15f), 128f, GenerateMoonflowerTexture);
        LoadOrGenerateSprite("Rose", 256, 256, new Vector2(0.5f, 0.15f), 128f, GenerateRoseTexture);
        LoadOrGenerateSprite("Durian", 256, 256, new Vector2(0.5f, 0.15f), 128f, GenerateDurianTexture);
        LoadOrGenerateSprite("Zombie", 256, 256, new Vector2(0.5f, 0.12f), 128f, GenerateZombieTexture);
        LoadOrGenerateSprite("BucketheadZombie", 256, 256, new Vector2(0.5f, 0.12f), 128f, GenerateBucketheadZombieTexture);
        LoadOrGenerateSprite("BunnyZombie", 256, 256, new Vector2(0.5f, 0.12f), 128f, GenerateBunnyZombieTexture);
        LoadOrGenerateSprite("Moon", 256, 256, new Vector2(0.5f, 0.5f), 128f, GenerateMoonTexture);
        LoadOrGenerateSprite("RoseProjectile", 128, 128, new Vector2(0.5f, 0.5f), 100f, GenerateRoseProjectileTexture);
    }

    private static void LoadOrGenerateSprite(
        string key,
        int width,
        int height,
        Vector2 pivot,
        float pixelsPerUnit,
        System.Action<Canvas2D> drawAction)
    {
        if (cachedSprites.ContainsKey(key) && cachedSprites[key] != null) return;

        string folder = "Assets/Sprites";
        string filePath = Path.Combine(folder, $"{key}.png");

        Texture2D tex = null;
        if (File.Exists(filePath))
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(fileData))
            {
                tex = null;
            }
            else
            {
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
            }
        }

        if (tex == null)
        {
            Canvas2D c = new Canvas2D(width, height);
            drawAction(c);
            tex = c.ToTexture2D();

            try
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllBytes(filePath, tex.EncodeToPNG());
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GameVisuals] Could not cache {key}.png to disk: {ex.Message}");
            }
        }

        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, width, height), pivot, pixelsPerUnit);
        sprite.name = key;
        cachedSprites[key] = sprite;
    }

    // ==================== PROCEDURAL MENU TEXTURE CACHE & GENERATORS ====================

    private static readonly Dictionary<string, Texture2D> cachedMenuTextures = new Dictionary<string, Texture2D>();

    public static Texture2D GetMenuTexture(string key)
    {
        if (cachedMenuTextures.TryGetValue(key, out Texture2D existing) && existing != null)
        {
            return existing;
        }

        string folder = "Assets/Sprites";
        string filePath = Path.Combine(folder, $"{key}.png");

        if (File.Exists(filePath))
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (loaded.LoadImage(fileData))
            {
                loaded.filterMode = FilterMode.Bilinear;
                loaded.wrapMode = TextureWrapMode.Clamp;
                cachedMenuTextures[key] = loaded;
                return loaded;
            }
        }

        int w = 480, h = 76;
        Canvas2D c = null;

        if (key == "MenuContainer")
        {
            w = 560; h = 460;
            c = new Canvas2D(w, h);
            GenerateMenuContainer(c, w, h);
        }
        else if (key == "MenuTitleBox")
        {
            w = 440; h = 88;
            c = new Canvas2D(w, h);
            GenerateMenuTitleBox(c, w, h);
        }
        else if (key.StartsWith("BtnAdventure_"))
        {
            w = 480; h = 76;
            c = new Canvas2D(w, h);
            int state = key.EndsWith("Pressed") ? 2 : (key.EndsWith("Hover") ? 1 : 0);
            GenerateAdventureButton(c, w, h, state);
        }
        else if (key.StartsWith("BtnControls_"))
        {
            w = 480; h = 70;
            c = new Canvas2D(w, h);
            int state = key.EndsWith("Pressed") ? 2 : (key.EndsWith("Hover") ? 1 : 0);
            GenerateControlsButton(c, w, h, state);
        }
        else if (key.StartsWith("BtnQuit_"))
        {
            w = 480; h = 64;
            c = new Canvas2D(w, h);
            int state = key.EndsWith("Pressed") ? 2 : (key.EndsWith("Hover") ? 1 : 0);
            GenerateQuitButton(c, w, h, state);
        }

        if (c != null)
        {
            Texture2D tex = c.ToTexture2D();
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            cachedMenuTextures[key] = tex;
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllBytes(filePath, tex.EncodeToPNG());
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GameVisuals] Could not cache {key}.png to disk: {ex.Message}");
            }
            return tex;
        }

        return Texture2D.whiteTexture;
    }

    private static void GenerateMenuContainer(Canvas2D c, int w, int h)
    {
        // Deep indigo / night sky card with dark wood/bronze border
        c.DrawVerticalGradientRect(10, 10, w - 20, h - 20, new Color32(24, 34, 54, 250), new Color32(11, 16, 28, 252), 22f, 4.5f, new Color32(72, 42, 23, 255));
        // Subtle inner rim line
        c.DrawRect(16, 16, w - 32, h - 32, new Color32(0, 0, 0, 0), 17f, 1.2f, new Color32(40, 60, 95, 110));
    }

    private static void GenerateMenuTitleBox(Canvas2D c, int w, int h)
    {
        // Carved dark brown wood slab with warm amber border
        c.DrawVerticalGradientRect(6, 6, w - 12, h - 12, new Color32(72, 40, 21, 255), new Color32(44, 23, 11, 255), 16f, 3.5f, new Color32(142, 82, 38, 255));
        // Inner accent border
        c.DrawRect(11, 11, w - 22, h - 22, new Color32(0, 0, 0, 0), 12f, 1.2f, new Color32(205, 135, 65, 80));
    }

    private static void GenerateAdventureButton(Canvas2D c, int w, int h, int state)
    {
        float yShift = (state == 2) ? -2f : 0f;
        Color32 topCol = (state == 1) ? new Color32(126, 189, 46, 255) : ((state == 2) ? new Color32(82, 128, 28, 255) : new Color32(110, 165, 39, 255));
        Color32 botCol = (state == 1) ? new Color32(82, 131, 28, 255) : ((state == 2) ? new Color32(52, 85, 16, 255) : new Color32(70, 111, 22, 255));
        Color32 borderCol = (state == 1) ? new Color32(180, 240, 82, 255) : ((state == 2) ? new Color32(120, 171, 42, 255) : new Color32(159, 224, 56, 255));

        // Bottom 3D bevel shadow
        if (state != 2)
        {
            c.DrawRect(12, 4, w - 24, 12, new Color32(33, 54, 11, 240), 16f);
        }

        // Main pill slab
        c.DrawVerticalGradientRect(10, 8 + yShift, w - 20, h - 16, topCol, botCol, 20f, 3.8f, borderCol);

        // Blue rounded square on the left
        float iconY = (h - 40) * 0.5f + yShift;
        c.DrawVerticalGradientRect(26, iconY, 40, 40, new Color32(66, 135, 245, 255), new Color32(29, 78, 216, 255), 9f, 2f, new Color32(96, 165, 250, 255));

        // White play arrow inside the blue square
        c.DrawTriangle(new Vector2(40, iconY + 11), new Vector2(40, iconY + 29), new Vector2(56, iconY + 20), new Color32(255, 255, 255, 255));
    }

    private static void GenerateControlsButton(Canvas2D c, int w, int h, int state)
    {
        float yShift = (state == 2) ? -2f : 0f;
        Color32 topCol = (state == 1) ? new Color32(110, 68, 36, 255) : ((state == 2) ? new Color32(72, 41, 20, 255) : new Color32(91, 55, 28, 255));
        Color32 botCol = (state == 1) ? new Color32(71, 40, 18, 255) : ((state == 2) ? new Color32(46, 23, 9, 255) : new Color32(59, 34, 15, 255));
        Color32 borderCol = (state == 1) ? new Color32(181, 112, 56, 255) : ((state == 2) ? new Color32(120, 68, 32, 255) : new Color32(146, 90, 45, 255));

        // Bottom 3D bevel shadow
        if (state != 2)
        {
            c.DrawRect(12, 4, w - 24, 10, new Color32(28, 15, 7, 240), 14f);
        }

        // Main pill slab
        c.DrawVerticalGradientRect(10, 7 + yShift, w - 20, h - 14, topCol, botCol, 18f, 3.4f, borderCol);

        // Rolled scroll icon on the left
        float iconY = (h - 38) * 0.5f + yShift;
        c.DrawRect(28, iconY + 5, 34, 26, new Color32(254, 240, 138, 255), 3f, 1.5f, new Color32(180, 110, 35, 255));
        c.DrawEllipse(45, iconY + 31, 18, 5, new Color32(253, 224, 71, 255), 1.5f, new Color32(160, 90, 25, 255));
        c.DrawEllipse(45, iconY + 5, 18, 5, new Color32(250, 204, 21, 255), 1.5f, new Color32(160, 90, 25, 255));
        c.DrawRect(34, iconY + 22, 22, 2.5f, new Color32(146, 64, 14, 200));
        c.DrawRect(34, iconY + 16, 22, 2.5f, new Color32(146, 64, 14, 200));
        c.DrawRect(34, iconY + 10, 15, 2.5f, new Color32(146, 64, 14, 200));
    }

    private static void GenerateQuitButton(Canvas2D c, int w, int h, int state)
    {
        float yShift = (state == 2) ? -2f : 0f;
        Color32 topCol = (state == 1) ? new Color32(106, 116, 135, 255) : ((state == 2) ? new Color32(68, 76, 89, 255) : new Color32(89, 98, 116, 255));
        Color32 botCol = (state == 1) ? new Color32(70, 79, 95, 255) : ((state == 2) ? new Color32(43, 48, 56, 255) : new Color32(60, 68, 81, 255));
        Color32 borderCol = (state == 1) ? new Color32(159, 174, 196, 255) : ((state == 2) ? new Color32(104, 116, 135, 255) : new Color32(132, 145, 166, 255));

        // Bottom 3D bevel shadow
        if (state != 2)
        {
            c.DrawRect(12, 4, w - 24, 10, new Color32(23, 28, 36, 240), 12f);
        }

        // Main pill slab
        c.DrawVerticalGradientRect(10, 6 + yShift, w - 20, h - 12, topCol, botCol, 16f, 3.2f, borderCol);

        // Tombstone icon on the left
        float iconY = (h - 36) * 0.5f + yShift;
        c.DrawRect(32, iconY + 3, 24, 19, new Color32(148, 163, 184, 255), 2f, 1.5f, new Color32(51, 65, 85, 255));
        c.DrawCircle(44, iconY + 22, 12, new Color32(148, 163, 184, 255), 1.5f, new Color32(51, 65, 85, 255));
        c.DrawRect(42.5f, iconY + 9, 3, 13, new Color32(51, 65, 85, 255));
        c.DrawRect(38.5f, iconY + 15, 11, 3, new Color32(51, 65, 85, 255));
        c.DrawTriangle(new Vector2(28, iconY + 3), new Vector2(33, iconY + 9), new Vector2(36, iconY + 3), new Color32(34, 197, 94, 255));
        c.DrawTriangle(new Vector2(51, iconY + 3), new Vector2(55, iconY + 10), new Vector2(59, iconY + 3), new Color32(34, 197, 94, 255));
    }

    // ==================== PROCEDURAL 2D ARTWORK GENERATORS ====================

    private static void GenerateMoonflowerTexture(Canvas2D c)
    {
        // 1. Soft base shadow on the ground
        c.DrawEllipse(128, 20, 52, 10, new Color32(10, 30, 20, 90));

        // 2. Base leafy pads
        c.DrawRotatedEllipse(86, 38, 38, 15, -28f, new Color32(39, 174, 96, 255), 3f, new Color32(20, 90, 50, 255));
        c.DrawRotatedEllipse(170, 38, 38, 15, 28f, new Color32(46, 204, 113, 255), 3f, new Color32(20, 90, 50, 255));
        // Leaf midrib veins
        c.DrawCapsule(86, 38, 112, 32, 2f, new Color32(30, 130, 76, 220));
        c.DrawCapsule(170, 38, 144, 32, 2f, new Color32(30, 130, 76, 220));

        // 3. Sturdy curved emerald stem
        c.DrawCapsule(128, 25, 126, 75, 12f, new Color32(39, 174, 96, 255), 3.5f, new Color32(20, 90, 50, 255));
        c.DrawCapsule(126, 75, 128, 135, 11f, new Color32(46, 204, 113, 255), 3.5f, new Color32(20, 90, 50, 255));
        // Stem highlight
        c.DrawCapsule(124, 35, 124, 125, 3f, new Color32(130, 224, 170, 180));

        // 4. Soft cyan outer petal glow
        c.DrawGlow(128, 152, 105f, new Color32(0, 229, 255, 95));

        // 5. Radiating cyan/teal flower petals (12 rounded petals)
        int petalCount = 12;
        float petalR = 62f;
        for (int i = 0; i < petalCount; i++)
        {
            float ang = i * (360f / petalCount) * Mathf.Deg2Rad;
            float px = 128f + Mathf.Cos(ang) * petalR;
            float py = 152f + Mathf.Sin(ang) * petalR;
            float deg = i * (360f / petalCount);

            // Petal gradient from bright cyan to deep teal
            Color32 petalFill = (i % 2 == 0)
                ? new Color32(0, 229, 255, 255)
                : new Color32(29, 233, 182, 255);

            c.DrawRotatedEllipse(px, py, 26f, 15f, deg, petalFill, 3.5f, new Color32(0, 77, 64, 255));
            // Inner petal highlight vein
            c.DrawCapsule(128f + Mathf.Cos(ang) * 38f, 152f + Mathf.Sin(ang) * 38f, px, py, 2f, new Color32(224, 247, 250, 220));
        }

        // 6. Radiant inner golden aura
        c.DrawCircle(128, 152, 45f, new Color32(255, 213, 79, 255), 4f, new Color32(230, 81, 0, 255));
        c.DrawCircle(128, 152, 41f, new Color32(255, 193, 7, 255));

        // 7. Smiling golden crescent moon face
        // Crescent outer & inner cut
        c.DrawCrescent(128, 152, 35f, 26f, 10f, 6f, new Color32(255, 238, 88, 255), 2.5f, new Color32(230, 81, 0, 255));

        // Cute cartoon eyes
        c.DrawEllipse(116, 156, 6f, 8f, new Color32(255, 255, 255, 255), 2f, new Color32(33, 33, 33, 255));
        c.DrawEllipse(138, 156, 6f, 8f, new Color32(255, 255, 255, 255), 2f, new Color32(33, 33, 33, 255));
        c.DrawCircle(117, 155, 4f, new Color32(20, 20, 25, 255));
        c.DrawCircle(139, 155, 4f, new Color32(20, 20, 25, 255));
        // Eye shine sparkles
        c.DrawCircle(115, 158, 2f, new Color32(255, 255, 255, 255));
        c.DrawCircle(137, 158, 2f, new Color32(255, 255, 255, 255));

        // Cheerful curved smile
        c.DrawCapsule(122, 142, 128, 137, 2.5f, new Color32(191, 54, 12, 255));
        c.DrawCapsule(128, 137, 134, 142, 2.5f, new Color32(191, 54, 12, 255));

        // Cute rosy cheeks
        c.DrawCircle(108, 147, 6f, new Color32(255, 112, 67, 110));
        c.DrawCircle(146, 147, 6f, new Color32(255, 112, 67, 110));
    }

    private static void GenerateRoseTexture(Canvas2D c)
    {
        // 1. Soft base shadow on the ground
        c.DrawEllipse(128, 20, 50, 10, new Color32(15, 25, 20, 90));

        // 2. Thorny stem & leaves
        c.DrawRotatedEllipse(82, 42, 34, 14, -30f, new Color32(39, 174, 96, 255), 3f, new Color32(20, 90, 50, 255));
        c.DrawRotatedEllipse(168, 40, 32, 13, 26f, new Color32(46, 204, 113, 255), 3f, new Color32(20, 90, 50, 255));

        // Sturdy stem
        c.DrawCapsule(128, 25, 118, 115, 12f, new Color32(39, 174, 96, 255), 3.5f, new Color32(20, 90, 50, 255));
        // Stem thorns
        c.DrawTriangle(new Vector2(118, 55), new Vector2(104, 62), new Vector2(119, 69), new Color32(192, 57, 43, 255), 1.5f, new Color32(120, 20, 20, 255));
        c.DrawTriangle(new Vector2(125, 82), new Vector2(138, 89), new Vector2(124, 96), new Color32(192, 57, 43, 255), 1.5f, new Color32(120, 20, 20, 255));

        // 3. Blooming Rose Blossom Cup (centered at 112, 150)
        c.DrawGlow(112, 150, 95f, new Color32(255, 42, 109, 80));

        // Outer petals layer (deep crimson)
        c.DrawCircle(112, 150, 60f, new Color32(173, 20, 87, 255), 4f, new Color32(74, 4, 34, 255));
        c.DrawRotatedEllipse(75, 162, 32, 22, 35f, new Color32(194, 24, 91, 255), 3f, new Color32(74, 4, 34, 255));
        c.DrawRotatedEllipse(145, 162, 30, 20, -35f, new Color32(194, 24, 91, 255), 3f, new Color32(74, 4, 34, 255));

        // Main blooming ruby flower head
        c.DrawCircle(112, 150, 48f, new Color32(233, 30, 99, 255), 3.5f, new Color32(136, 14, 79, 255));
        c.DrawCircle(110, 153, 40f, new Color32(255, 64, 129, 255));

        // Spiraling rose petal folds
        c.DrawRotatedEllipse(96, 142, 24, 16, 25f, new Color32(233, 30, 99, 255), 2.5f, new Color32(136, 14, 79, 255));
        c.DrawRotatedEllipse(126, 142, 24, 16, -25f, new Color32(233, 30, 99, 255), 2.5f, new Color32(136, 14, 79, 255));
        c.DrawRotatedEllipse(112, 134, 26, 14, 0f, new Color32(255, 128, 171, 255), 2.5f, new Color32(136, 14, 79, 255));

        // 4. Cannon Barrel / Peashooter Muzzle pointing to the RIGHT
        // Cylinder snout extending from x=135 to x=195, y=148
        c.DrawCapsule(130, 148, 185, 148, 18f, new Color32(233, 30, 99, 255), 3.5f, new Color32(136, 14, 79, 255));
        // Snout top highlight
        c.DrawCapsule(135, 155, 180, 155, 5f, new Color32(255, 128, 171, 220));

        // Golden muzzle rim ring
        c.DrawRotatedEllipse(185, 148, 8, 22, 0f, new Color32(255, 215, 0, 255), 3f, new Color32(180, 120, 0, 255));
        // Cannon barrel opening (dark interior)
        c.DrawRotatedEllipse(188, 148, 6, 17, 0f, new Color32(42, 0, 16, 255), 2f, new Color32(20, 0, 5, 255));

        // 5. Determined, cute Warrior Eyes (aiming forward down the lane)
        c.DrawRotatedEllipse(96, 160, 8f, 11f, 10f, new Color32(255, 255, 255, 255), 2f, new Color32(30, 10, 20, 255));
        c.DrawRotatedEllipse(120, 160, 8f, 11f, 10f, new Color32(255, 255, 255, 255), 2f, new Color32(30, 10, 20, 255));

        // Black pupils focused to the right
        c.DrawCircle(99, 160, 5f, new Color32(25, 10, 20, 255));
        c.DrawCircle(123, 160, 5f, new Color32(25, 10, 20, 255));
        // Eye gleam spots
        c.DrawCircle(98, 162, 2f, new Color32(255, 255, 255, 255));
        c.DrawCircle(122, 162, 2f, new Color32(255, 255, 255, 255));

        // Determined sharp warrior brows angled forward
        c.DrawCapsule(88, 172, 105, 169, 3f, new Color32(136, 14, 79, 255));
        c.DrawCapsule(112, 169, 129, 172, 3f, new Color32(136, 14, 79, 255));
    }

    private static void GenerateDurianTexture(Canvas2D c)
    {
        // 1. Sturdy ground shadow
        c.DrawEllipse(128, 20, 68, 12, new Color32(20, 25, 15, 100));

        // 2. Armored Body Silhouette (golden-olive shield/oval)
        float cx = 128f;
        float cy = 125f;

        // Radial sharp protective spikes (18 spikes around the perimeter)
        int spikeCount = 18;
        for (int i = 0; i < spikeCount; i++)
        {
            float ang = i * (360f / spikeCount) * Mathf.Deg2Rad;
            float baseRx = 64f;
            float baseRy = 76f;
            float tipR = 88f;

            float bx1 = cx + Mathf.Cos(ang - 0.16f) * baseRx;
            float by1 = cy + Mathf.Sin(ang - 0.16f) * baseRy;
            float bx2 = cx + Mathf.Cos(ang + 0.16f) * baseRx;
            float by2 = cy + Mathf.Sin(ang + 0.16f) * baseRy;
            float tx = cx + Mathf.Cos(ang) * tipR;
            float ty = cy + Mathf.Sin(ang) * (tipR * 1.1f);

            c.DrawTriangle(new Vector2(bx1, by1), new Vector2(tx, ty), new Vector2(bx2, by2),
                new Color32(249, 211, 66, 255), 2.5f, new Color32(74, 55, 10, 255));
        }

        // Armored main fruit body
        c.DrawEllipse(cx, cy, 66f, 78f, new Color32(181, 138, 42, 255), 4.5f, new Color32(60, 45, 10, 255));
        c.DrawEllipse(cx, cy, 61f, 73f, new Color32(212, 175, 55, 255));

        // Armored segment facets (curved protective plates like Wall-nut / Durian armor)
        c.DrawRotatedEllipse(cx - 30f, cy, 28f, 65f, 15f, new Color32(163, 128, 37, 255), 3f, new Color32(60, 45, 10, 255));
        c.DrawRotatedEllipse(cx + 30f, cy, 28f, 65f, -15f, new Color32(163, 128, 37, 255), 3f, new Color32(60, 45, 10, 255));
        c.DrawEllipse(cx, cy, 32f, 68f, new Color32(230, 194, 91, 255), 3f, new Color32(60, 45, 10, 255));

        // Armor plate specular highlight on top
        c.DrawEllipse(cx, cy + 42f, 26f, 12f, new Color32(255, 238, 140, 180));

        // 3. Sturdy woody pedicel stem & top leaves
        c.DrawCapsule(cx, cy + 74f, cx, cy + 96f, 7f, new Color32(90, 107, 47, 255), 2.5f, new Color32(40, 50, 20, 255));
        c.DrawRotatedEllipse(cx - 18f, cy + 86f, 16f, 7f, 25f, new Color32(120, 144, 60, 255), 2f, new Color32(40, 50, 20, 255));
        c.DrawRotatedEllipse(cx + 18f, cy + 86f, 16f, 7f, -25f, new Color32(120, 144, 60, 255), 2f, new Color32(40, 50, 20, 255));

        // 4. Stoic Defender Eyes & Face (cute, resolute, confident)
        c.DrawEllipse(cx - 18f, cy + 12f, 12f, 15f, new Color32(255, 255, 255, 255), 2.5f, new Color32(35, 25, 10, 255));
        c.DrawEllipse(cx + 18f, cy + 12f, 12f, 15f, new Color32(255, 255, 255, 255), 2.5f, new Color32(35, 25, 10, 255));

        // Large dark pupils
        c.DrawEllipse(cx - 16f, cy + 11f, 8f, 10f, new Color32(35, 25, 10, 255));
        c.DrawEllipse(cx + 20f, cy + 11f, 8f, 10f, new Color32(35, 25, 10, 255));
        // Eye gleam shines
        c.DrawCircle(cx - 18f, cy + 14f, 3.5f, new Color32(255, 255, 255, 255));
        c.DrawCircle(cx + 18f, cy + 14f, 3.5f, new Color32(255, 255, 255, 255));
        c.DrawCircle(cx - 14f, cy + 8f, 1.5f, new Color32(255, 255, 255, 255));
        c.DrawCircle(cx + 22f, cy + 8f, 1.5f, new Color32(255, 255, 255, 255));

        // Resolute smile / defender grin
        c.DrawCapsule(cx - 14f, cy - 18f, cx, cy - 23f, 3f, new Color32(60, 45, 10, 255));
        c.DrawCapsule(cx, cy - 23f, cx + 14f, cy - 18f, 3f, new Color32(60, 45, 10, 255));

        // Defender cheeks
        c.DrawCircle(cx - 30f, cy - 5f, 8f, new Color32(230, 140, 60, 90));
        c.DrawCircle(cx + 30f, cy - 5f, 8f, new Color32(230, 140, 60, 90));
    }

    private static void GenerateZombieTexture(Canvas2D c)
    {
        // 1. Ground shadow
        c.DrawEllipse(128, 16, 56, 10, new Color32(15, 20, 25, 100));

        // 2. Scuffed dark brown shoes
        c.DrawCapsule(102, 20, 118, 20, 8f, new Color32(78, 52, 46, 255), 2.5f, new Color32(33, 20, 15, 255));
        c.DrawCapsule(138, 22, 154, 22, 8f, new Color32(78, 52, 46, 255), 2.5f, new Color32(33, 20, 15, 255));

        // 3. Blue trousers legs with ragged hem
        c.DrawCapsule(110, 26, 114, 75, 10f, new Color32(44, 62, 80, 255), 3f, new Color32(20, 30, 40, 255));
        c.DrawCapsule(146, 28, 142, 75, 10f, new Color32(52, 73, 94, 255), 3f, new Color32(20, 30, 40, 255));

        // 4. Purple ragged business suit jacket
        c.DrawRect(96, 75, 66, 68, new Color32(106, 27, 154, 255), 8f, 3.5f, new Color32(42, 10, 65, 255));
        c.DrawRect(102, 72, 54, 30, new Color32(74, 20, 110, 255));

        // White shirt collar
        c.DrawTriangle(new Vector2(118, 142), new Vector2(128, 126), new Vector2(128, 142), new Color32(236, 240, 241, 255), 1.5f, new Color32(50, 50, 50, 255));
        c.DrawTriangle(new Vector2(138, 142), new Vector2(128, 126), new Vector2(128, 142), new Color32(236, 240, 241, 255), 1.5f, new Color32(50, 50, 50, 255));

        // Bright red necktie dangling down
        c.DrawTriangle(new Vector2(124, 134), new Vector2(132, 134), new Vector2(128, 86), new Color32(211, 47, 47, 255), 2f, new Color32(110, 20, 20, 255));
        c.DrawTriangle(new Vector2(124, 88), new Vector2(132, 88), new Vector2(128, 76), new Color32(183, 28, 28, 255), 1.5f, new Color32(110, 20, 20, 255));

        // 5. Outstretched Arms reaching forward to the left (towards plants)
        // Back arm (right arm)
        c.DrawCapsule(135, 118, 72, 115, 8f, new Color32(124, 179, 66, 255), 3f, new Color32(46, 71, 19, 255));
        // Back hand & claw fingers
        c.DrawCapsule(72, 115, 52, 114, 5f, new Color32(124, 179, 66, 255), 2f, new Color32(46, 71, 19, 255));

        // Front arm (left arm)
        c.DrawCapsule(116, 128, 54, 132, 9f, new Color32(139, 195, 74, 255), 3f, new Color32(46, 71, 19, 255));
        // Front claw hand reaching out
        c.DrawCapsule(54, 132, 38, 135, 6f, new Color32(139, 195, 74, 255), 2.5f, new Color32(46, 71, 19, 255));
        c.DrawCapsule(54, 130, 40, 126, 4f, new Color32(139, 195, 74, 255), 2f, new Color32(46, 71, 19, 255));

        // 6. Comical tilted green Zombie Head
        float hx = 132f;
        float hy = 186f;
        c.DrawRotatedEllipse(hx, hy, 38f, 36f, 6f, new Color32(124, 179, 66, 255), 4f, new Color32(46, 71, 19, 255));
        c.DrawRotatedEllipse(hx - 2f, hy + 2f, 33f, 31f, 6f, new Color32(139, 195, 74, 255));

        // Protruding Underbite Jaw
        c.DrawCapsule(hx - 22f, hy - 20f, hx + 4f, hy - 22f, 10f, new Color32(104, 159, 56, 255), 3f, new Color32(46, 71, 19, 255));
        // Gaping dark mouth slot
        c.DrawCapsule(hx - 20f, hy - 16f, hx + 2f, hy - 17f, 4.5f, new Color32(30, 45, 15, 255));
        // Single cute crooked white tooth sticking UP!
        c.DrawCapsule(hx - 14f, hy - 18f, hx - 14f, hy - 10f, 3.5f, new Color32(255, 253, 231, 255), 1.5f, new Color32(80, 70, 40, 255));

        // Mismatched cartoon eyes!
        // Left eye: huge wide circle staring forward
        c.DrawCircle(hx - 16f, hy + 8f, 14f, new Color32(255, 255, 255, 255), 3f, new Color32(33, 33, 33, 255));
        c.DrawCircle(hx - 19f, hy + 8f, 5.5f, new Color32(20, 25, 20, 255));
        c.DrawCircle(hx - 21f, hy + 10f, 2f, new Color32(255, 255, 255, 255));

        // Right eye: comical smaller wandering circle
        c.DrawCircle(hx + 14f, hy + 6f, 10f, new Color32(255, 255, 255, 255), 2.5f, new Color32(33, 33, 33, 255));
        c.DrawCircle(hx + 12f, hy + 7f, 4f, new Color32(20, 25, 20, 255));
        c.DrawCircle(hx + 11f, hy + 8f, 1.5f, new Color32(255, 255, 255, 255));

        // Disheveled messy hair tufts on top of head
        c.DrawCapsule(hx - 14f, hy + 34f, hx - 18f, hy + 46f, 2.5f, new Color32(33, 33, 33, 255));
        c.DrawCapsule(hx, hy + 35f, hx - 2f, hy + 49f, 2.5f, new Color32(33, 33, 33, 255));
        c.DrawCapsule(hx + 12f, hy + 33f, hx + 16f, hy + 44f, 2.5f, new Color32(33, 33, 33, 255));
    }

    private static void GenerateBucketheadZombieTexture(Canvas2D c)
    {
        // 1. Heavy ground shadow
        c.DrawEllipse(128, 16, 62, 11, new Color32(15, 20, 25, 115));

        // 2. Heavy steel-toed boots
        c.DrawCapsule(100, 20, 118, 20, 9f, new Color32(60, 45, 40, 255), 2.5f, new Color32(25, 15, 12, 255));
        c.DrawCapsule(138, 22, 156, 22, 9f, new Color32(60, 45, 40, 255), 2.5f, new Color32(25, 15, 12, 255));

        // 3. Sturdy patched charcoal trousers
        c.DrawCapsule(110, 26, 114, 75, 11f, new Color32(55, 65, 75, 255), 3f, new Color32(20, 25, 30, 255));
        c.DrawCapsule(146, 28, 142, 75, 11f, new Color32(62, 72, 84, 255), 3f, new Color32(20, 25, 30, 255));

        // 4. Heavy Charcoal / Iron-Brown Coat with Shoulder Pads & Belt
        c.DrawRect(92, 74, 74, 70, new Color32(78, 62, 54, 255), 9f, 3.5f, new Color32(35, 24, 18, 255));
        c.DrawRect(98, 72, 62, 28, new Color32(58, 44, 38, 255));
        // Heavy utility belt & silver buckle
        c.DrawRect(92, 82, 74, 10, new Color32(42, 30, 22, 255), 2f, 2f, new Color32(20, 14, 10, 255));
        c.DrawRect(120, 80, 16, 14, new Color32(190, 200, 210, 255), 2f, 2f, new Color32(60, 70, 80, 255));

        // Red tattered tie
        c.DrawTriangle(new Vector2(124, 134), new Vector2(132, 134), new Vector2(128, 90), new Color32(198, 40, 40, 255), 2f, new Color32(100, 15, 15, 255));

        // 5. Outstretched Arms
        c.DrawCapsule(135, 118, 70, 115, 9f, new Color32(124, 179, 66, 255), 3f, new Color32(46, 71, 19, 255));
        c.DrawCapsule(70, 115, 50, 114, 5.5f, new Color32(124, 179, 66, 255), 2f, new Color32(46, 71, 19, 255));
        c.DrawCapsule(116, 128, 52, 132, 9.5f, new Color32(139, 195, 74, 255), 3f, new Color32(46, 71, 19, 255));
        c.DrawCapsule(52, 132, 36, 135, 6f, new Color32(139, 195, 74, 255), 2.5f, new Color32(46, 71, 19, 255));

        // 6. Green Zombie Head & Jaw
        float hx = 132f;
        float hy = 182f;
        c.DrawRotatedEllipse(hx, hy, 38f, 35f, 5f, new Color32(124, 179, 66, 255), 4f, new Color32(46, 71, 19, 255));
        c.DrawCapsule(hx - 22f, hy - 20f, hx + 4f, hy - 22f, 10f, new Color32(104, 159, 56, 255), 3f, new Color32(46, 71, 19, 255));
        c.DrawCapsule(hx - 20f, hy - 16f, hx + 2f, hy - 17f, 4.5f, new Color32(30, 45, 15, 255));

        // Eyes peeking out from under the bucket rim
        c.DrawCircle(hx - 16f, hy + 2f, 12f, new Color32(255, 255, 255, 255), 2.5f, new Color32(33, 33, 33, 255));
        c.DrawCircle(hx - 19f, hy + 2f, 5f, new Color32(20, 25, 20, 255));
        c.DrawCircle(hx + 12f, hy + 1f, 9f, new Color32(255, 255, 255, 255), 2.5f, new Color32(33, 33, 33, 255));
        c.DrawCircle(hx + 10f, hy + 1f, 3.5f, new Color32(20, 25, 20, 255));

        // 7. Galvanized Metal Bucket Helmet on Top of Head!
        // Bucket body (trapezoid/rounded cylinder tilted slightly forward)
        c.DrawRect(hx - 36f, hy + 10f, 72f, 52f, new Color32(168, 179, 190, 255), 6f, 3.5f, new Color32(55, 65, 75, 255));
        // Metallic sheen stripe on bucket
        c.DrawRect(hx - 20f, hy + 14f, 16f, 44f, new Color32(215, 225, 235, 220));
        // Bucket bottom rim (now at the top of the inverted bucket)
        c.DrawCapsule(hx - 32f, hy + 61f, hx + 32f, hy + 61f, 5f, new Color32(140, 152, 165, 255), 2.5f, new Color32(50, 60, 70, 255));
        // Bucket mouth rim (resting over zombie brow)
        c.DrawCapsule(hx - 40f, hy + 11f, hx + 40f, hy + 9f, 6.5f, new Color32(190, 202, 214, 255), 3f, new Color32(45, 55, 65, 255));
        // Red paint smudge on the bucket (classic PvZ detail)
        c.DrawRotatedEllipse(hx - 6f, hy + 36f, 12f, 7f, -20f, new Color32(215, 45, 45, 235));
        // Wire bail handle hanging down across front
        c.DrawCapsule(hx + 34f, hy + 22f, hx - 28f, hy - 2f, 2.5f, new Color32(210, 220, 230, 255), 1.5f, new Color32(45, 55, 65, 255));
    }

    private static void GenerateBunnyZombieTexture(Canvas2D c)
    {
        // 1. Ground shadow
        c.DrawEllipse(128, 16, 52, 9, new Color32(15, 20, 25, 95));

        // 2. Bright Sporty Orange & White Running Sneakers
        c.DrawCapsule(96, 20, 114, 20, 8f, new Color32(255, 109, 0, 255), 2.5f, new Color32(80, 30, 0, 255));
        c.DrawCapsule(98, 16, 114, 16, 3f, new Color32(255, 255, 255, 255));
        c.DrawCapsule(140, 24, 158, 24, 8f, new Color32(255, 109, 0, 255), 2.5f, new Color32(80, 30, 0, 255));
        c.DrawCapsule(142, 20, 158, 20, 3f, new Color32(255, 255, 255, 255));

        // 3. Dark Navy Athletic Track Pants with White Side Stripes
        c.DrawCapsule(106, 26, 114, 76, 9f, new Color32(30, 41, 59, 255), 3f, new Color32(15, 23, 42, 255));
        c.DrawCapsule(148, 28, 140, 76, 9f, new Color32(30, 41, 59, 255), 3f, new Color32(15, 23, 42, 255));
        c.DrawCapsule(104, 30, 112, 72, 2f, new Color32(240, 248, 255, 230));

        // 4. Fluffy White Bunny Tail on the back (+X side)
        c.DrawCircle(166, 80, 14f, new Color32(250, 250, 255, 255), 3f, new Color32(140, 150, 170, 255));
        c.DrawCircle(168, 82, 9f, new Color32(255, 255, 255, 255));

        // 5. Vibrant Teal & Coral Sporty Hoodie / Tracksuit Jacket (distinct clothing!)
        c.DrawRect(98, 75, 62, 66, new Color32(0, 172, 193, 255), 10f, 3.5f, new Color32(0, 77, 90, 255));
        // Coral/Orange V-chest racing panel & white stripes
        c.DrawTriangle(new Vector2(102, 138), new Vector2(156, 138), new Vector2(129, 92), new Color32(255, 112, 67, 255), 2f, new Color32(140, 40, 15, 255));
        c.DrawRect(98, 102, 62, 7, new Color32(255, 255, 255, 245));

        // 6. Fast Runner Outstretched Arms
        c.DrawCapsule(135, 118, 68, 118, 7.5f, new Color32(139, 195, 74, 255), 3f, new Color32(46, 71, 19, 255));
        c.DrawCapsule(116, 128, 50, 134, 8f, new Color32(156, 204, 101, 255), 3f, new Color32(46, 71, 19, 255));
        // Orange sporty wristbands
        c.DrawCapsule(66, 118, 74, 118, 8.5f, new Color32(255, 109, 0, 255), 2f, new Color32(90, 30, 0, 255));
        c.DrawCapsule(54, 134, 62, 134, 9f, new Color32(255, 109, 0, 255), 2f, new Color32(90, 30, 0, 255));

        // 7. Agile Zombie Head + Sporty Headband
        float hx = 128f;
        float hy = 180f;
        c.DrawRotatedEllipse(hx, hy, 35f, 33f, 8f, new Color32(139, 195, 74, 255), 3.5f, new Color32(46, 71, 19, 255));
        // Bucktooth bunny-zombie grin
        c.DrawCapsule(hx - 18f, hy - 16f, hx + 4f, hy - 17f, 4.5f, new Color32(30, 45, 15, 255));
        c.DrawRect(hx - 14f, hy - 22f, 6f, 8f, new Color32(255, 255, 255, 255), 1f, 1.5f, new Color32(60, 60, 60, 255));
        c.DrawRect(hx - 7f, hy - 22f, 6f, 8f, new Color32(255, 255, 255, 255), 1f, 1.5f, new Color32(60, 60, 60, 255));

        // Energetic wide eyes
        c.DrawCircle(hx - 14f, hy + 4f, 12f, new Color32(255, 255, 255, 255), 2.5f, new Color32(33, 33, 33, 255));
        c.DrawCircle(hx - 17f, hy + 4f, 5f, new Color32(20, 25, 20, 255));
        c.DrawCircle(hx + 12f, hy + 3f, 10f, new Color32(255, 255, 255, 255), 2.5f, new Color32(33, 33, 33, 255));
        c.DrawCircle(hx + 9f, hy + 3f, 4f, new Color32(20, 25, 20, 255));

        // Coral/White Sweatband around forehead
        c.DrawCapsule(hx - 30f, hy + 19f, hx + 28f, hy + 16f, 5.5f, new Color32(255, 87, 34, 255), 2.5f, new Color32(90, 20, 5, 255));

        // 8. Tall Expressive Bunny Ears on Top of Head!
        // Left Bunny Ear (tilted slightly forward)
        c.DrawRotatedEllipse(hx - 12f, hy + 48f, 10f, 28f, 12f, new Color32(248, 250, 252, 255), 3f, new Color32(71, 85, 105, 255));
        c.DrawRotatedEllipse(hx - 12f, hy + 47f, 5f, 20f, 12f, new Color32(244, 114, 182, 255));
        // Right Bunny Ear (tilted slightly back)
        c.DrawRotatedEllipse(hx + 14f, hy + 47f, 10f, 28f, -16f, new Color32(248, 250, 252, 255), 3f, new Color32(71, 85, 105, 255));
        c.DrawRotatedEllipse(hx + 14f, hy + 46f, 5f, 20f, -16f, new Color32(244, 114, 182, 255));
    }

    private static void GenerateMoonTexture(Canvas2D c)
    {
        // 1. Soft radiant celestial halo glow
        c.DrawGlow(128, 128, 110f, new Color32(255, 235, 120, 110));
        c.DrawGlow(128, 128, 75f, new Color32(255, 245, 157, 160));

        // 2. Crisp, elegant golden crescent moon
        float cx = 124f;
        float cy = 128f;
        float rOuter = 66f;
        float rInner = 53f;
        float offsetCutX = 18f;
        float offsetCutY = 8f;

        c.DrawCrescent(cx, cy, rOuter, rInner, offsetCutX, offsetCutY,
            new Color32(255, 214, 0, 255), 4.5f, new Color32(230, 81, 0, 255));

        // Inner glowing body
        c.DrawCrescent(cx - 2f, cy - 2f, rOuter - 5f, rInner, offsetCutX - 1f, offsetCutY - 1f,
            new Color32(255, 238, 88, 255));

        // Outer rim bright highlight curve
        c.DrawCrescent(cx - 4f, cy - 4f, rOuter - 9f, rInner + 2f, offsetCutX - 3f, offsetCutY - 3f,
            new Color32(255, 255, 200, 220));

        // 3. Celestial 4-point star sparkle nestled in the crescent bend
        c.DrawStar(142f, 146f, 5f, 18f, 4, new Color32(255, 255, 255, 255));
        c.DrawGlow(142f, 146f, 24f, new Color32(255, 255, 255, 150));
    }

    private static void GenerateRoseProjectileTexture(Canvas2D c)
    {
        // Soft ruby glow aura
        c.DrawGlow(64, 64, 52f, new Color32(255, 23, 68, 110));

        // Emerald stabilizer petal fins at rear
        c.DrawTriangle(new Vector2(28, 64), new Vector2(14, 82), new Vector2(36, 67), new Color32(46, 204, 113, 255), 1.5f, new Color32(20, 90, 50, 255));
        c.DrawTriangle(new Vector2(28, 64), new Vector2(14, 46), new Vector2(36, 61), new Color32(46, 204, 113, 255), 1.5f, new Color32(20, 90, 50, 255));

        // Aerodynamic Ruby Thorn Dart pointing to the RIGHT (+X)
        // Diamond crystal body from x=24 to x=108, y=64, half-height=16
        Vector2 tip = new Vector2(108, 64);
        Vector2 top = new Vector2(58, 80);
        Vector2 tail = new Vector2(24, 64);
        Vector2 bot = new Vector2(58, 48);

        c.DrawTriangle(tip, top, tail, new Color32(255, 23, 68, 255), 2.5f, new Color32(136, 14, 79, 255));
        c.DrawTriangle(tip, tail, bot, new Color32(213, 0, 0, 255), 2.5f, new Color32(136, 14, 79, 255));

        // Center spine crystal facet highlight
        c.DrawCapsule(30, 64, 102, 64, 3f, new Color32(255, 128, 171, 240));
        c.DrawCircle(102, 64, 3.5f, new Color32(255, 255, 255, 255));
    }

    // ==================== ANTI-ALIASED 2D CANVAS RASTERIZER ====================

    public class Canvas2D
    {
        public readonly int width;
        public readonly int height;
        public readonly Color32[] pixels;

        public Canvas2D(int w, int h)
        {
            width = w;
            height = h;
            pixels = new Color32[w * h];
        }

        public Texture2D ToTexture2D()
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        public void BlendPixel(int x, int y, Color32 src)
        {
            if (x < 0 || x >= width || y < 0 || y >= height || src.a == 0) return;
            int idx = y * width + x;
            Color32 dst = pixels[idx];

            if (dst.a == 0)
            {
                pixels[idx] = src;
                return;
            }

            float srcA = src.a / 255f;
            float dstA = dst.a / 255f;
            float outA = srcA + dstA * (1f - srcA);

            if (outA <= 0.001f)
            {
                pixels[idx] = new Color32(0, 0, 0, 0);
                return;
            }

            byte r = (byte)Mathf.Clamp(((src.r * srcA) + (dst.r * dstA * (1f - srcA))) / outA, 0f, 255f);
            byte g = (byte)Mathf.Clamp(((src.g * srcA) + (dst.g * dstA * (1f - srcA))) / outA, 0f, 255f);
            byte b = (byte)Mathf.Clamp(((src.b * srcA) + (dst.b * dstA * (1f - srcA))) / outA, 0f, 255f);
            byte a = (byte)Mathf.Clamp(outA * 255f, 0f, 255f);

            pixels[idx] = new Color32(r, g, b, a);
        }

        public void DrawCircle(float cx, float cy, float radius, Color32 fill, float outline = 0f, Color32 outlineCol = default)
        {
            float totalR = radius + outline;
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - totalR - 2));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + totalR + 2));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - totalR - 2));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(cy + totalR + 2));

            for (int y = minY; y <= maxY; y++)
            {
                float dy = y - cy;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > totalR + 0.5f) continue;

                    if (outline > 0f)
                    {
                        if (dist <= radius - 0.5f)
                        {
                            BlendPixel(x, y, fill);
                        }
                        else if (dist <= radius + 0.5f)
                        {
                            float t = dist - (radius - 0.5f);
                            Color32 c = Color32.Lerp(fill, outlineCol, t);
                            BlendPixel(x, y, c);
                        }
                        else if (dist <= totalR - 0.5f)
                        {
                            BlendPixel(x, y, outlineCol);
                        }
                        else
                        {
                            float alpha = Mathf.Clamp01(totalR + 0.5f - dist);
                            Color32 c = outlineCol;
                            c.a = (byte)(c.a * alpha);
                            BlendPixel(x, y, c);
                        }
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(radius + 0.5f - dist);
                        Color32 c = fill;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
            }
        }

        public void DrawEllipse(float cx, float cy, float rx, float ry, Color32 fill, float outline = 0f, Color32 outlineCol = default)
        {
            DrawRotatedEllipse(cx, cy, rx, ry, 0f, fill, outline, outlineCol);
        }

        public void DrawRotatedEllipse(float cx, float cy, float rx, float ry, float angleDeg, Color32 fill, float outline = 0f, Color32 outlineCol = default)
        {
            float maxR = Mathf.Max(rx, ry) + outline + 2f;
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - maxR));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + maxR));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - maxR));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(cy + maxR));

            float rad = -angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            float invRx = 1f / Mathf.Max(0.1f, rx);
            float invRy = 1f / Mathf.Max(0.1f, ry);
            float minDim = Mathf.Min(rx, ry);

            for (int y = minY; y <= maxY; y++)
            {
                float dy0 = y - cy;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx0 = x - cx;

                    // Rotate coordinate
                    float lx = dx0 * cos - dy0 * sin;
                    float ly = dx0 * sin + dy0 * cos;

                    float nx = lx * invRx;
                    float ny = ly * invRy;
                    float normDist = Mathf.Sqrt(nx * nx + ny * ny);

                    // Convert normalized ellipse distance to approximate pixel distance
                    float pixelDist = (normDist - 1.0f) * minDim;
                    float totalOutline = outline;

                    if (pixelDist > totalOutline + 0.5f) continue;

                    if (outline > 0f)
                    {
                        if (pixelDist <= -0.5f)
                        {
                            BlendPixel(x, y, fill);
                        }
                        else if (pixelDist <= 0.5f)
                        {
                            float t = pixelDist + 0.5f;
                            Color32 c = Color32.Lerp(fill, outlineCol, t);
                            BlendPixel(x, y, c);
                        }
                        else if (pixelDist <= totalOutline - 0.5f)
                        {
                            BlendPixel(x, y, outlineCol);
                        }
                        else
                        {
                            float alpha = Mathf.Clamp01(totalOutline + 0.5f - pixelDist);
                            Color32 c = outlineCol;
                            c.a = (byte)(c.a * alpha);
                            BlendPixel(x, y, c);
                        }
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(0.5f - pixelDist);
                        Color32 c = fill;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
            }
        }

        public void DrawCapsule(float x1, float y1, float x2, float y2, float radius, Color32 fill, float outline = 0f, Color32 outlineCol = default)
        {
            float totalR = radius + outline;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x1, x2) - totalR - 2));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(x1, x2) + totalR + 2));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y1, y2) - totalR - 2));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(y1, y2) + totalR + 2));

            Vector2 a = new Vector2(x1, y1);
            Vector2 b = new Vector2(x2, y2);
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x, y);
                    float t = lenSq > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq) : 0f;
                    Vector2 closest = a + ab * t;
                    float dist = Vector2.Distance(p, closest);

                    if (dist > totalR + 0.5f) continue;

                    if (outline > 0f)
                    {
                        if (dist <= radius - 0.5f)
                        {
                            BlendPixel(x, y, fill);
                        }
                        else if (dist <= radius + 0.5f)
                        {
                            float blend = dist - (radius - 0.5f);
                            Color32 c = Color32.Lerp(fill, outlineCol, blend);
                            BlendPixel(x, y, c);
                        }
                        else if (dist <= totalR - 0.5f)
                        {
                            BlendPixel(x, y, outlineCol);
                        }
                        else
                        {
                            float alpha = Mathf.Clamp01(totalR + 0.5f - dist);
                            Color32 c = outlineCol;
                            c.a = (byte)(c.a * alpha);
                            BlendPixel(x, y, c);
                        }
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(radius + 0.5f - dist);
                        Color32 c = fill;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
            }
        }

        public void DrawRect(float x, float y, float w, float h, Color32 fill, float cornerRadius = 0f, float outline = 0f, Color32 outlineCol = default)
        {
            float totalOutline = outline;
            int minX = Mathf.Max(0, Mathf.FloorToInt(x - totalOutline - 1));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(x + w + totalOutline + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(y - totalOutline - 1));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(y + h + totalOutline + 1));

            float r = Mathf.Min(cornerRadius, Mathf.Min(w, h) * 0.5f);
            float cx1 = x + r;
            float cx2 = x + w - r;
            float cy1 = y + r;
            float cy2 = y + h - r;

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float clX = Mathf.Clamp(px, cx1, cx2);
                    float clY = Mathf.Clamp(py, cy1, cy2);

                    float dx = px - clX;
                    float dy = py - clY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > r + totalOutline + 0.5f) continue;

                    if (outline > 0f)
                    {
                        if (dist <= r - 0.5f)
                        {
                            BlendPixel(px, py, fill);
                        }
                        else if (dist <= r + 0.5f)
                        {
                            float t = dist - (r - 0.5f);
                            BlendPixel(px, py, Color32.Lerp(fill, outlineCol, t));
                        }
                        else if (dist <= r + totalOutline - 0.5f)
                        {
                            BlendPixel(px, py, outlineCol);
                        }
                        else
                        {
                            float alpha = Mathf.Clamp01(r + totalOutline + 0.5f - dist);
                            Color32 c = outlineCol;
                            c.a = (byte)(c.a * alpha);
                            BlendPixel(px, py, c);
                        }
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(r + 0.5f - dist);
                        Color32 c = fill;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(px, py, c);
                    }
                }
            }
        }

        public void DrawVerticalGradientRect(float x, float y, float w, float h, Color32 topColor, Color32 botColor, float cornerRadius = 0f, float outline = 0f, Color32 outlineCol = default)
        {
            float totalOutline = outline;
            int minX = Mathf.Max(0, Mathf.FloorToInt(x - totalOutline - 1));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(x + w + totalOutline + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(y - totalOutline - 1));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(y + h + totalOutline + 1));

            float r = Mathf.Min(cornerRadius, Mathf.Min(w, h) * 0.5f);
            float cx1 = x + r;
            float cx2 = x + w - r;
            float cy1 = y + r;
            float cy2 = y + h - r;

            for (int py = minY; py <= maxY; py++)
            {
                float tGrad = Mathf.Clamp01((py - y) / Mathf.Max(1f, h));
                Color32 fill = Color32.Lerp(botColor, topColor, tGrad);

                for (int px = minX; px <= maxX; px++)
                {
                    float clX = Mathf.Clamp(px, cx1, cx2);
                    float clY = Mathf.Clamp(py, cy1, cy2);

                    float dx = px - clX;
                    float dy = py - clY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > r + totalOutline + 0.5f) continue;

                    if (outline > 0f)
                    {
                        if (dist <= r - 0.5f)
                        {
                            BlendPixel(px, py, fill);
                        }
                        else if (dist <= r + 0.5f)
                        {
                            float t = dist - (r - 0.5f);
                            BlendPixel(px, py, Color32.Lerp(fill, outlineCol, t));
                        }
                        else if (dist <= r + totalOutline - 0.5f)
                        {
                            BlendPixel(px, py, outlineCol);
                        }
                        else
                        {
                            float alpha = Mathf.Clamp01(r + totalOutline + 0.5f - dist);
                            Color32 c = outlineCol;
                            c.a = (byte)(c.a * alpha);
                            BlendPixel(px, py, c);
                        }
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(r + 0.5f - dist);
                        Color32 c = fill;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(px, py, c);
                    }
                }
            }
        }

        public void DrawCrescent(float cx, float cy, float rOuter, float rInner, float cutOffsetX, float cutOffsetY, Color32 fill, float outline = 0f, Color32 outlineCol = default)
        {
            float cutCx = cx + cutOffsetX;
            float cutCy = cy + cutOffsetY;

            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - rOuter - outline - 2));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + rOuter + outline + 2));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - rOuter - outline - 2));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(cy + rOuter + outline + 2));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dOuter = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    float dInner = Vector2.Distance(new Vector2(x, y), new Vector2(cutCx, cutCy));

                    float distOuterEdge = dOuter - rOuter;
                    float distInnerEdge = rInner - dInner; // positive inside inner cut circle
                    float sdf = Mathf.Max(distOuterEdge, distInnerEdge);

                    if (sdf > outline + 0.5f) continue;

                    if (outline > 0f)
                    {
                        if (sdf <= -0.5f)
                        {
                            BlendPixel(x, y, fill);
                        }
                        else if (sdf <= 0.5f)
                        {
                            float t = sdf + 0.5f;
                            BlendPixel(x, y, Color32.Lerp(fill, outlineCol, t));
                        }
                        else if (sdf <= outline - 0.5f)
                        {
                            BlendPixel(x, y, outlineCol);
                        }
                        else
                        {
                            float alpha = Mathf.Clamp01(outline + 0.5f - sdf);
                            Color32 c = outlineCol;
                            c.a = (byte)(c.a * alpha);
                            BlendPixel(x, y, c);
                        }
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(0.5f - sdf);
                        Color32 c = fill;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
            }
        }

        public void DrawTriangle(Vector2 p1, Vector2 p2, Vector2 p3, Color32 fill, float outline = 0f, Color32 outlineCol = default)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)) - outline - 2));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)) + outline + 2));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p1.y, Mathf.Min(p2.y, p3.y)) - outline - 2));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)) + outline + 2));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x, y);

                    // Barycentric point-in-triangle check
                    float d1 = Sign(p, p1, p2);
                    float d2 = Sign(p, p2, p3);
                    float d3 = Sign(p, p3, p1);

                    bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
                    bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
                    bool inside = !(hasNeg && hasPos);

                    // Distance to nearest edge
                    float distToEdge = Mathf.Min(
                        DistToSegment(p, p1, p2),
                        Mathf.Min(DistToSegment(p, p2, p3), DistToSegment(p, p3, p1))
                    );

                    float sdf = inside ? -distToEdge : distToEdge;

                    if (sdf > outline + 0.5f) continue;

                    if (outline > 0f)
                    {
                        if (sdf <= -0.5f)
                        {
                            BlendPixel(x, y, fill);
                        }
                        else if (sdf <= 0.5f)
                        {
                            float t = sdf + 0.5f;
                            BlendPixel(x, y, Color32.Lerp(fill, outlineCol, t));
                        }
                        else if (sdf <= outline - 0.5f)
                        {
                            BlendPixel(x, y, outlineCol);
                        }
                        else
                        {
                            float alpha = Mathf.Clamp01(outline + 0.5f - sdf);
                            Color32 c = outlineCol;
                            c.a = (byte)(c.a * alpha);
                            BlendPixel(x, y, c);
                        }
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(0.5f - sdf);
                        Color32 c = fill;
                        c.a = (byte)(c.a * alpha);
                        BlendPixel(x, y, c);
                    }
                }
            }
        }

        public void DrawStar(float cx, float cy, float rInner, float rOuter, int points, Color32 color)
        {
            Vector2[] verts = new Vector2[points * 2];
            for (int i = 0; i < points * 2; i++)
            {
                float r = (i % 2 == 0) ? rOuter : rInner;
                float ang = i * (Mathf.PI / points);
                verts[i] = new Vector2(cx + Mathf.Cos(ang) * r, cy + Mathf.Sin(ang) * r);
            }

            Vector2 center = new Vector2(cx, cy);
            for (int i = 0; i < points * 2; i++)
            {
                Vector2 pNext = verts[(i + 1) % (points * 2)];
                DrawTriangle(center, verts[i], pNext, color);
            }
        }

        public void DrawGlow(float cx, float cy, float radius, Color32 glowColor)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(cy + radius));

            for (int y = minY; y <= maxY; y++)
            {
                float dy = y - cy;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist >= radius) continue;

                    float falloff = Mathf.Pow(Mathf.Clamp01(1.0f - (dist / radius)), 2.2f);
                    Color32 c = glowColor;
                    c.a = (byte)(glowColor.a * falloff);
                    BlendPixel(x, y, c);
                }
            }
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
