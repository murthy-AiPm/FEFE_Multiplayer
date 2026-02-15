using UnityEngine;

/// <summary>
/// Place this on the same GameObject as the Animator.
/// Unity Animation Events call methods on components attached to the Animator's GameObject.
/// This relay forwards those calls to CombatController, WeaponManager, and HitboxController.
/// 
/// In your animation clips, add events that call these method names:
///   - HitboxEnable        → turns on weapon hitbox (start of damage window)
///   - HitboxDisable       → turns off weapon hitbox (end of damage window)
///   - ComboWindowOpen     → player can input next attack
///   - AttackEnd           → attack animation finished
///   - DodgeEnd            → dodge animation finished
///   - WeaponToHand        → reparent weapon from holster to hand
///   - WeaponToHolster     → reparent weapon from hand to holster
///   - EquipComplete       → equip transition done
///   - HolsterComplete     → holster transition done
///   - FootstepLeft        → left foot hits ground (for sound)
///   - FootstepRight       → right foot hits ground (for sound)
/// </summary>
public class AnimationEventRelay : MonoBehaviour
{
    [Header("References (auto-found if null)")]
    [SerializeField] private CombatController combatController;
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private HitboxController activeHitbox; // set dynamically by WeaponManager

    // Events for external listeners (sound, VFX, etc.)
    public System.Action OnFootstepLeft;
    public System.Action OnFootstepRight;
    public System.Action<string> OnGenericEvent; // catch-all for custom events

    private void Awake()
    {
        if (combatController == null)
            combatController = GetComponentInParent<CombatController>();
        if (weaponManager == null)
            weaponManager = GetComponentInParent<WeaponManager>();
    }

    /// <summary>
    /// Call this when the active weapon changes to update the hitbox reference.
    /// </summary>
    public void SetActiveHitbox(HitboxController hitbox)
    {
        activeHitbox = hitbox;
    }

    // ─── Hitbox Events ───

    public void HitboxEnable()
    {
        if (activeHitbox != null)
            activeHitbox.EnableHitbox();
    }

    public void HitboxDisable()
    {
        if (activeHitbox != null)
            activeHitbox.DisableHitbox();
    }

    // ─── Combo Events ───

    public void ComboWindowOpen()
    {
        if (combatController != null)
            combatController.OnAnimEvent_ComboWindowOpen();
    }

    public void AttackEnd()
    {
        if (combatController != null)
            combatController.OnAnimEvent_AttackEnd();
    }

    // ─── Dodge Events ───

    public void DodgeEnd()
    {
        if (combatController != null)
            combatController.OnAnimEvent_DodgeEnd();
    }

    // ─── Weapon Equip/Holster Events ───

    public void WeaponToHand()
    {
        // Visual only — WeaponManager handles the actual reparenting
        // This is called mid-animation when the hand reaches the holster
    }

    public void WeaponToHolster()
    {
        // Visual only — paired with WeaponToHand
    }

    public void EquipComplete()
    {
        if (weaponManager != null)
            weaponManager.OnAnimEvent_EquipComplete();
    }

    public void HolsterComplete()
    {
        if (weaponManager != null)
            weaponManager.OnAnimEvent_HolsterComplete();
    }

    // ─── Footstep Events ───

    public void FootstepLeft()
    {
        OnFootstepLeft?.Invoke();
    }

    public void FootstepRight()
    {
        OnFootstepRight?.Invoke();
    }

    // ─── Generic ───

    /// <summary>
    /// Catch-all for custom animation events. 
    /// Add an event in Unity with string parameter matching your custom event name.
    /// </summary>
    public void OnCustomEvent(string eventName)
    {
        OnGenericEvent?.Invoke(eventName);
    }
}
