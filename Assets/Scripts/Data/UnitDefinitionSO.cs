using UnityEngine;

/// <summary>
/// Abstract base ScriptableObject for all playable 2D/3D units (Plants and Zombies).
/// Centralizes economy costs, cooldowns, 2D wall stats, 3D arena stats, prefabs, and ability metadata.
/// </summary>
public abstract class UnitDefinitionSO : ScriptableObject
{
    [Header("Identity & Economy")]
    public string displayName;
    public int cost = 50;
    public float cooldown = 5.0f;
    public Color buttonReadyTint = Color.white;

    [Header("2D Wall Stats")]
    public int maxHealth2D = 100;

    [Header("3D Arena Combat Stats")]
    public int maxArenaHP = 125;
    public float arenaSpeedMultiplier = 1.0f;

    [Header("Prefabs & Visuals")]
    public Sprite unitSprite;
    public GameObject prefab2D;
    public GameObject arenaVisualPrefab3D;

    [Header("3D Arena Abilities")]
    public string primaryAbilityTitle;
    public int primaryAbilityDamage = 25;
    public float primaryAbilityCooldown = 0.85f;
    public string secondaryAbilityTitle;
    public int secondaryAbilityDamage = 0;
    public float secondaryAbilityCooldown = 5.0f;
    [TextArea(2, 4)]
    public string arenaAbilityHint;
}
