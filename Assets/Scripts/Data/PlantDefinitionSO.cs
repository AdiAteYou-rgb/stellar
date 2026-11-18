using UnityEngine;
using Stellar.Combat;

/// <summary>
/// Data asset defining stats, costs, 2D behavior, 3D arena abilities, and prefabs for a Plant unit.
/// </summary>
[CreateAssetMenu(fileName = "PlantDefinition", menuName = "Stellar/Data/Plant Definition")]
public class PlantDefinitionSO : UnitDefinitionSO
{
    [Header("Plant Specifics")]
    public PlantType plantType = PlantType.Moonflower;

    [Header("2D Behavior Settings")]
    [Tooltip("Moon production interval for Moonflower, or shoot interval for Rose.")]
    public float actionInterval2D = 15.0f;
    public int moonProduceAmount = 50;

    [Header("3D Arena Special Parameters")]
    public float chargeDuration = 0.25f;
    public float burstRadius = 4.6f;
    public float rollDuration = 2.6f;
    public float stunDuration = 2.0f;

    [Header("3D Hitscan Configs")]
    public HitscanAttackConfig primaryHitscanConfig;
    public HitscanAttackConfig secondaryHitscanConfig;

    public static PlantDefinitionSO CreateDefault(PlantType type)
    {
        PlantDefinitionSO def = CreateInstance<PlantDefinitionSO>();
        def.plantType = type;

        switch (type)
        {
            case PlantType.Moonflower:
                def.name = "MoonflowerData";
                def.displayName = "Moonflower [1]";
                def.cost = 50;
                def.cooldown = 5.0f;
                def.maxHealth2D = 100;
                def.maxArenaHP = 125;
                def.arenaSpeedMultiplier = 1.0f;
                def.buttonReadyTint = new Color(0.4f, 0.85f, 1.0f);
                def.actionInterval2D = 15.0f;
                def.moonProduceAmount = 50;
                def.burstRadius = 4.6f;
                def.primaryAbilityTitle = "Solar Bolt [LMB]\n18 DMG";
                def.primaryAbilityDamage = 18;
                def.primaryAbilityCooldown = 0.80f;
                def.secondaryAbilityTitle = "Lunar Outburst [RMB/E]\n20 DMG • Knockback";
                def.secondaryAbilityDamage = 20;
                def.secondaryAbilityCooldown = 10.0f;
                def.arenaAbilityHint = "[L-Click] Solar Bolt (18 DMG)  •  [R-Click / E] Lunar Outburst (20 DMG Blast + Self/Zombie Knockback)";
                def.primaryHitscanConfig = new HitscanAttackConfig("Solar Bolt", true, 60f);
                def.secondaryHitscanConfig = new HitscanAttackConfig();
                break;

            case PlantType.Rose:
                def.name = "RoseData";
                def.displayName = "Rose [2]";
                def.cost = 100;
                def.cooldown = 7.5f;
                def.maxHealth2D = 100;
                def.maxArenaHP = 125;
                def.arenaSpeedMultiplier = 1.0f;
                def.buttonReadyTint = new Color(1.0f, 0.4f, 0.5f);
                def.actionInterval2D = 1.8f;
                def.chargeDuration = 0.25f;
                def.primaryAbilityTitle = "Thorn Shot [LMB]\n25 DMG";
                def.primaryAbilityDamage = 25;
                def.primaryAbilityCooldown = 0.90f;
                def.secondaryAbilityTitle = "Thorn Volley [RMB/E]\n45 DMG";
                def.secondaryAbilityDamage = 45;
                def.secondaryAbilityCooldown = 4.5f;
                def.arenaAbilityHint = "[L-Click] Hitscan Thorn (25 DMG)  •  [R-Click / E] Heavy Thorn Volley (45 DMG)";
                def.primaryHitscanConfig = new HitscanAttackConfig("Rose Thorn", true, 65f);
                def.secondaryHitscanConfig = new HitscanAttackConfig("Rose Heavy Volley", true, 65f);
                break;

            case PlantType.Durian:
            default:
                def.name = "DurianData";
                def.displayName = "Durian [3]";
                def.cost = 50;
                def.cooldown = 20.0f;
                def.maxHealth2D = 400;
                def.maxArenaHP = 400;
                def.arenaSpeedMultiplier = 0.92f;
                def.buttonReadyTint = new Color(0.9f, 0.78f, 0.3f);
                def.rollDuration = 2.6f;
                def.stunDuration = 2.0f;
                def.primaryAbilityTitle = "Spike Bash [LMB]\n28 DMG";
                def.primaryAbilityDamage = 28;
                def.primaryAbilityCooldown = 1.0f;
                def.secondaryAbilityTitle = "Durian Roll [RMB/E]\n50 DMG + Stun";
                def.secondaryAbilityDamage = 50;
                def.secondaryAbilityCooldown = 6.0f;
                def.arenaAbilityHint = "[L-Click] Spike Bash (28 DMG)  •  [R-Click / E] Durian Roll (50 DMG + 2.0s Stun)";
                def.primaryHitscanConfig = new HitscanAttackConfig();
                def.secondaryHitscanConfig = new HitscanAttackConfig();
                break;
        }

        return def;
    }
}
