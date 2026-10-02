using UnityEngine;

/// <summary>
/// Tuning for one ship type. The single source of truth for ship stats: the owner moves with these values and
/// the host checks movement and cooldowns against the same values. One asset per ship in Assets/ShipConfigs,
/// assigned on the Player prefab's PlayerClass (menu: BombBattle > Create and Assign Ship Configs).
/// Ability fields only matter for the ship that has that ability.
/// </summary>
[CreateAssetMenu(fileName = "ShipConfig", menuName = "BombBattle/Ship Config")]
public class ShipConfig : ScriptableObject
{
    [Header("Movement")]
    [Tooltip("Forward speed (m/s). Ships always move.")]
    public float moveSpeed = 10f;
    [Tooltip("How fast the ship turns toward the cursor. Max turn rate ≈ 180°/s × this value.")]
    public float turnSpeed = 1f;

    [Header("Bomb trail")]
    [Tooltip("Seconds between trail bombs.")]
    public float bombInterval = 0.5f;

    [Header("Sprint (Space)")]
    public float sprintMultiplier = 3f;
    public float sprintDuration = 2f;
    public float sprintCooldown = 15f;

    [Header("Ability (E)")]
    public float abilityCooldown = 30f;

    [Header("Galleon: bomb shot")]
    public float shotSpeed = 20f;
    [Tooltip("How far ahead of the ship the shot appears.")]
    public float shotSpawnDistance = 5f;

    [Header("Sloop: bomb frenzy")]
    public float frenzyDuration = 3f;
    [Tooltip("Trail bombs drop this many times faster during the frenzy.")]
    public float frenzyBombRateMultiplier = 3f;

    [Header("Caravel: anchor + slide")]
    public float anchorHeight = 2f;
    public float slideSpeed = 30f;
    public float slideTurnSpeed = 360f;
    [Tooltip("Invincibility kept after the slide ends (s).")]
    public float invincibilityAfterSlide = 1f;

    private static ShipConfig defaults;

    /// <summary>Today's built-in values, used if a ship has no config asset assigned.</summary>
    public static ShipConfig Defaults
    {
        get
        {
            if (defaults == null)
            {
                defaults = CreateInstance<ShipConfig>();
                defaults.name = "ShipConfig (built-in defaults)";
                defaults.hideFlags = HideFlags.HideAndDontSave;
            }
            return defaults;
        }
    }
}
