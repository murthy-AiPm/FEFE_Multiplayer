using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages dragon melee hitboxes (paws, bite, tail).
/// Each hitbox is a HitboxController on a bone child. This script initializes
/// them with the correct WeaponData and provides Enable/Disable methods
/// that animation events can call.
///
/// Setup:
///   1. Add HitboxController components to paw bones (as children or directly)
///   2. Assign them and their WeaponData in the Inspector
///   3. In melee attack animation clips, add events:
///      - OnAnimEvent_EnablePawHitbox / OnAnimEvent_DisablePawHitbox
///
/// Future: Add bite/tail entries the same way.
/// </summary>
public class DragonHitboxManager : NetworkBehaviour
{
    [Header("Dragon Network Object")]
    [SerializeField] private NetworkObject dragonNetObj;

    [Header("Paw Hitboxes")]
    [SerializeField] private HitboxController leftPawHitbox;
    [SerializeField] private HitboxController rightPawHitbox;
    [SerializeField] private WeaponData pawWeaponData;

    [Header("Debug")]
    [SerializeField] private bool debugLogging;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (dragonNetObj == null)
            dragonNetObj = GetComponentInParent<NetworkObject>();

        InitializeHitbox(leftPawHitbox, pawWeaponData);
        InitializeHitbox(rightPawHitbox, pawWeaponData);
    }

    private void InitializeHitbox(HitboxController hitbox, WeaponData weapon)
    {
        if (hitbox == null) return;

        hitbox.Initialize(dragonNetObj, weapon, useInspectorShape: true);
        hitbox.DisableHitbox();

        if (debugLogging)
            Debug.Log($"[DragonHitboxManager] Initialized hitbox on {hitbox.gameObject.name} with {weapon?.weaponName ?? "null"}");
    }

    // ═══════════════════════════════════════════════════════════════
    // ANIMATION EVENT METHODS
    // Called from melee attack animation clips via AnimationEvent
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Enable both paw hitboxes (attack window starts).</summary>
    public void OnAnimEvent_EnablePawHitbox()
    {
        if (!IsOwner) return;

        if (leftPawHitbox != null) leftPawHitbox.EnableHitbox();
        if (rightPawHitbox != null) rightPawHitbox.EnableHitbox();

        if (debugLogging)
            Debug.Log("[DragonHitboxManager] Paw hitboxes ENABLED");
    }

    /// <summary>Disable both paw hitboxes (attack window ends).</summary>
    public void OnAnimEvent_DisablePawHitbox()
    {
        if (!IsOwner) return;

        if (leftPawHitbox != null) leftPawHitbox.DisableHitbox();
        if (rightPawHitbox != null) rightPawHitbox.DisableHitbox();

        if (debugLogging)
            Debug.Log("[DragonHitboxManager] Paw hitboxes DISABLED");
    }

    /// <summary>Enable only the left paw hitbox.</summary>
    public void OnAnimEvent_EnableLeftPaw()
    {
        if (!IsOwner) return;
        if (leftPawHitbox != null) leftPawHitbox.EnableHitbox();
    }

    /// <summary>Disable only the left paw hitbox.</summary>
    public void OnAnimEvent_DisableLeftPaw()
    {
        if (!IsOwner) return;
        if (leftPawHitbox != null) leftPawHitbox.DisableHitbox();
    }

    /// <summary>Enable only the right paw hitbox.</summary>
    public void OnAnimEvent_EnableRightPaw()
    {
        if (!IsOwner) return;
        if (rightPawHitbox != null) rightPawHitbox.EnableHitbox();
    }

    /// <summary>Disable only the right paw hitbox.</summary>
    public void OnAnimEvent_DisableRightPaw()
    {
        if (!IsOwner) return;
        if (rightPawHitbox != null) rightPawHitbox.DisableHitbox();
    }
}
