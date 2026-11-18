using UnityEngine;
using Stellar.Combat;

/// <summary>
/// Data asset defining stats, costs, 2D lane behavior, 3D arena abilities, and prefabs for a Zombie unit.
/// </summary>
[CreateAssetMenu(fileName = "ZombieDefinition", menuName = "Stellar/Data/Zombie Definition")]
public class ZombieDefinitionSO : UnitDefinitionSO
{
    [Header("Zombie Specifics")]
    public ZombieType zombieType = ZombieType.Normal;

    [Header("2D Lane Movement & Animation")]
    public float moveSpeed2D = 0.58f;
    public float bobFrequency2D = 5.2f;
    public float bobAmplitude2D = 0.08f;

    [Header("2D Ranged Settings (Hazmat)")]
    public float spitInterval2D = 2.2f;
    public float spitRange2D = 7.0f;
    public int spitAcidDamage2D = 30;

    [Header("3D Arena Modifiers & Durations")]
    public float arenaFovBonus = 8.0f;
    [Tooltip("Active duration for Parry window, Bunny Slide, or Feral Lunge.")]
    public float activeAbilityDuration = 1.0f;
    [Tooltip("Secondary duration such as Parry speed boost or Hazmat Bio-Disruptor lock.")]
    public float secondaryEffectDuration = 3.0f;

    [Header("3D Hitscan Configs")]
    public HitscanAttackConfig primaryHitscanConfig;
    public HitscanAttackConfig secondaryHitscanConfig;

    public static ZombieDefinitionSO CreateDefault(ZombieType type)
    {
        ZombieDefinitionSO def = CreateInstance<ZombieDefinitionSO>();
        def.zombieType = type;

        switch (type)
        {
            case ZombieType.Buckethead:
                def.name = "BucketheadZombieData";
                def.displayName = "Buckethead [5]";
                def.cost = 100;
                def.cooldown = 12.0f;
                def.maxHealth2D = 500;
                def.moveSpeed2D = 0.46f;
                def.bobFrequency2D = 4.2f;
                def.bobAmplitude2D = 0.08f;
                def.maxArenaHP = 400;
                def.arenaSpeedMultiplier = 0.86f;
                def.arenaFovBonus = 4f;
                def.buttonReadyTint = new Color(0.62f, 0.72f, 0.84f);
                def.primaryAbilityTitle = "Heavy Slash [LMB]\n30 DMG";
                def.primaryAbilityDamage = 30;
                def.primaryAbilityCooldown = 1.0f;
                def.secondaryAbilityTitle = "Bucket Parry [RMB/E]";
                def.secondaryAbilityDamage = 0;
                def.secondaryAbilityCooldown = 5.0f;
                def.activeAbilityDuration = 1.0f;
                def.secondaryEffectDuration = 3.0f;
                def.arenaAbilityHint = "[L-Click] Heavy Slash  •  [R-Click / E / Q] Iron Bucket Parry (1s Block -> 3s Speed Boost, 5s CD)";
                def.primaryHitscanConfig = new HitscanAttackConfig();
                def.secondaryHitscanConfig = new HitscanAttackConfig();
                break;

            case ZombieType.Bunny:
                def.name = "BunnyZombieData";
                def.displayName = "Bunny Speed [6]";
                def.cost = 75;
                def.cooldown = 7.5f;
                def.maxHealth2D = 80;
                def.moveSpeed2D = 1.08f;
                def.bobFrequency2D = 8.5f;
                def.bobAmplitude2D = 0.16f;
                def.maxArenaHP = 90;
                def.arenaSpeedMultiplier = 1.44f;
                def.arenaFovBonus = 14f;
                def.buttonReadyTint = new Color(0.22f, 0.86f, 0.84f);
                def.primaryAbilityTitle = "Bunny Claw [RMB/E]\n22 DMG";
                def.primaryAbilityDamage = 22;
                def.primaryAbilityCooldown = 0.65f;
                def.secondaryAbilityTitle = "Bunny Slide [LMB/Q]";
                def.secondaryAbilityDamage = 0;
                def.secondaryAbilityCooldown = 3.0f;
                def.activeAbilityDuration = 0.90f;
                def.secondaryEffectDuration = 0.65f;
                def.arenaAbilityHint = "[L-Click / Q / Ctrl] Bunny Slide  •  [R-Click / E] Quick Slash  •  [F / Slide+RMB] Feral Dive Lunge";
                def.primaryHitscanConfig = new HitscanAttackConfig();
                def.secondaryHitscanConfig = new HitscanAttackConfig();
                break;

            case ZombieType.Hazmat:
                def.name = "HazmatZombieData";
                def.displayName = "Hazmat [7]";
                def.cost = 100;
                def.cooldown = 9.0f;
                def.maxHealth2D = 125;
                def.moveSpeed2D = 0.50f;
                def.bobFrequency2D = 4.8f;
                def.bobAmplitude2D = 0.09f;
                def.spitInterval2D = 2.2f;
                def.spitRange2D = 7.0f;
                def.spitAcidDamage2D = 30;
                def.maxArenaHP = 135;
                def.arenaSpeedMultiplier = 1.12f;
                def.arenaFovBonus = 6f;
                def.buttonReadyTint = new Color(0.55f, 0.95f, 0.25f);
                def.primaryAbilityTitle = "Acid Shot [LMB]\n20+4% HP DMG";
                def.primaryAbilityDamage = 20;
                def.primaryAbilityCooldown = 0.75f;
                def.secondaryAbilityTitle = "Bio-Disruptor [RMB/E]\nCancel + 2s Lock";
                def.secondaryAbilityDamage = 0;
                def.secondaryAbilityCooldown = 5.0f;
                def.activeAbilityDuration = 0.28f;
                def.secondaryEffectDuration = 2.0f;
                def.arenaAbilityHint = "[L-Click] Corrosive Acid (20+4% Max HP)  •  [R-Click / E / Q] Bio-Disruptor (0 DMG, Cancels Roll/Charge/Mobility + 2s Lock + Hop)";
                def.primaryHitscanConfig = new HitscanAttackConfig("Hazmat Acid Shot", true, 65f);
                def.secondaryHitscanConfig = new HitscanAttackConfig("Hazmat Bio-Disruptor", true, 55f);
                break;

            case ZombieType.Normal:
            default:
                def.name = "NormalZombieData";
                def.displayName = "Zombie [4]";
                def.cost = 50;
                def.cooldown = 5.0f;
                def.maxHealth2D = 100;
                def.moveSpeed2D = 0.58f;
                def.bobFrequency2D = 5.2f;
                def.bobAmplitude2D = 0.08f;
                def.maxArenaHP = 150;
                def.arenaSpeedMultiplier = 1.15f;
                def.arenaFovBonus = 9f;
                def.buttonReadyTint = new Color(0.68f, 0.42f, 0.88f);
                def.primaryAbilityTitle = "Claw Slash [LMB/E]\n25 DMG";
                def.primaryAbilityDamage = 25;
                def.primaryAbilityCooldown = 0.85f;
                def.secondaryAbilityTitle = "Feral Lunge [RMB/Q]\n38 DMG";
                def.secondaryAbilityDamage = 38;
                def.secondaryAbilityCooldown = 3.5f;
                def.activeAbilityDuration = 0.65f;
                def.secondaryEffectDuration = 0f;
                def.arenaAbilityHint = "[L-Click / E] Claw Slash  •  [R-Click / Q] Feral Dive Lunge (Fast Pounce)";
                def.primaryHitscanConfig = new HitscanAttackConfig();
                def.secondaryHitscanConfig = new HitscanAttackConfig();
                break;
        }

        return def;
    }
}
