using UnityEngine;

public enum WeaponType
{
    None,       // fists
    OneHanded,
    TwoHanded,
    Bow,
    Shield
}

public enum WeaponSlot
{
    Primary,    // slot 1 — melee weapon
    Secondary   // slot 2 — bow
}

/// <summary>
/// Defines a weapon's stats and visuals.
/// Animation is handled entirely by AnimationSetBase + RuleAnimancerDriver.
/// The weaponProfileName must match a WeaponAttackProfile.weaponName in RuleAnimancerDriver.
/// 
/// Create one asset per weapon (e.g. "Iron Sword", "Longbow", "Wooden Shield").
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "Game/Combat/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Identity")]
    public string weaponName = "Weapon";
    public WeaponType weaponType = WeaponType.OneHanded;
    public GameObject weaponPrefab;  // visual mesh to instantiate

    [Header("Animation Profile Link")]
    [Tooltip("Must match a WeaponAttackProfile.weaponName in RuleAnimancerDriver")]
    public string weaponProfileName = "Sword";

    [Header("Combat Stats")]
    public float baseDamage = 10f;
    public float heavyDamageMultiplier = 2f;
    public float attackRange = 2f;
    public float staminaCostLight = 10f;
    public float staminaCostHeavy = 25f;
    public float blockStaminaCost = 5f;

    [Header("Bow-Specific")]
    public GameObject arrowPrefab;
    public float arrowSpeed = 40f;
    public float drawTime = 0.8f;

    [Header("Holster Points")]
    [Tooltip("Name of the Transform on the character where this sits when holstered")]
    public string holsterBone = "Spine_Holster";
    public Vector3 holsterLocalPos;
    public Vector3 holsterLocalRot;

    [Header("Hitbox")]
    public float hitboxLength = 1f;
    public float hitboxRadius = 0.15f;
    public Vector3 hitboxOffset;
}
