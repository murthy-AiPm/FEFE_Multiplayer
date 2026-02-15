using UnityEngine;

/// <summary>
/// Defines the weapon loadout for a character class.
/// Assign to CharacterData to lock loadout per class.
/// 
/// Examples:
///   Castellan: primary = 1H Sword, shield = Wooden Shield, secondary = Shortbow
///   Warden:    primary = 2H Greatsword, shield = null, secondary = Longbow
///   Ranger:    primary = 1H Dagger, shield = null, secondary = Longbow
/// </summary>
[CreateAssetMenu(fileName = "NewLoadout", menuName = "Game/Combat/Combat Loadout")]
public class CombatLoadout : ScriptableObject
{
    [Header("Slot 1 — Primary Melee")]
    public WeaponData primaryWeapon;   // 1H or 2H

    [Header("Off-Hand (1H only)")]
    public WeaponData shield;          // null if 2H or no shield

    [Header("Slot 2 — Ranged")]
    public WeaponData secondaryWeapon; // bow

    [Header("Fist Combat")]
    public WeaponData fistWeapon;      // optional override; null = use default fist data

    [Header("Vital Overrides (optional)")]
    [Tooltip("If set, override base health/stamina for this class")]
    public float healthOverride = -1;  // -1 = use VitalDefinition default
    public float staminaOverride = -1;

    /// <summary>
    /// Does this loadout include a shield?
    /// </summary>
    public bool HasShield => shield != null;

    /// <summary>
    /// Is the primary weapon two-handed?
    /// </summary>
    public bool IsTwoHanded => primaryWeapon != null && primaryWeapon.weaponType == WeaponType.TwoHanded;
}
