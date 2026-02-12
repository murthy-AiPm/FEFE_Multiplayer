using UnityEngine;

public enum WeaponType
{
    None,       // fists
    OneHanded,
    TwoHanded,
    Bow,
    Shield      // not a weapon per se, but occupies off-hand
}

public enum WeaponSlot
{
    Primary,    // slot 1 — melee weapon
    Secondary   // slot 2 — bow
}

/// <summary>
/// Defines a weapon's stats, visuals, and animation clips.
/// Create one asset per weapon (e.g. "Iron Sword", "Longbow", "Wooden Shield").
/// </summary>
[CreateAssetMenu(fileName = "NewWeapon", menuName = "Game/Combat/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Identity")]
    public string weaponName = "Weapon";
    public WeaponType weaponType = WeaponType.OneHanded;
    public GameObject weaponPrefab;  // visual mesh to instantiate

    [Header("Combat Stats")]
    public float baseDamage = 10f;
    public float attackSpeed = 1f;    // multiplier on animation speed
    public float attackRange = 2f;    // for validation
    public float staminaCostLight = 10f;
    public float staminaCostHeavy = 25f;
    public float blockStaminaCost = 5f; // per hit blocked

    [Header("Combo Chain")]
    [Tooltip("Number of attacks in the light combo chain (1-4)")]
    [Range(1, 4)]
    public int comboLength = 3;
    public float comboWindowDuration = 0.5f; // seconds to input next attack

    [Header("Animation Clips (Animancer)")]
    [Tooltip("Clips played in sequence for light combo chain")]
    public AnimationClip[] lightAttackClips;
    public AnimationClip heavyAttackClip;
    public AnimationClip blockClip;       // 1H only
    public AnimationClip equipClip;       // unholster
    public AnimationClip holsterClip;     // holster

    [Header("Bow-Specific")]
    public AnimationClip drawClip;
    public AnimationClip aimIdleClip;
    public AnimationClip releaseClip;
    public GameObject arrowPrefab;
    public float arrowSpeed = 40f;
    public float drawTime = 0.8f;         // how long to fully draw

    [Header("Holster Points")]
    [Tooltip("Name of the Transform on the character model where this weapon sits when holstered")]
    public string holsterBone = "Spine_Holster";
    [Tooltip("Local offset when holstered")]
    public Vector3 holsterLocalPos;
    public Vector3 holsterLocalRot;

    [Header("Hitbox")]
    public float hitboxLength = 1f;    // for melee weapons
    public float hitboxRadius = 0.15f;
    public Vector3 hitboxOffset;       // offset from weapon root

    /// <summary>
    /// Get the animation clip for a specific combo index.
    /// </summary>
    public AnimationClip GetLightAttackClip(int comboIndex)
    {
        if (lightAttackClips == null || lightAttackClips.Length == 0) return null;
        return lightAttackClips[Mathf.Clamp(comboIndex, 0, lightAttackClips.Length - 1)];
    }
}
