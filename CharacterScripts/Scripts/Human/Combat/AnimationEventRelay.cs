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
///   - FootSteps           → generic footstep (use this if clips don't distinguish left/right)
///   - FootstepLeft        → left foot hits ground
///   - FootstepRight       → right foot hits ground
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

    // ─── Combo / Attack Events ───

    public void ComboWindowOpen() { }
    public void AttackEnd() { }

    // ─── Dodge Events ───

    public void DodgeEnd()
    {
        // CombatController handles dodge end via its own timer.
        // This exists so animation events don't throw errors.
    }

    // ─── Weapon Equip/Holster Events ───

    public void WeaponToHand() { }
    public void WeaponToHolster() { }

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

    /// <summary>
    /// Generic footstep — use this if your animation clips don't distinguish left/right foot.
    /// Fires both Left and Right listeners so FootstepSoundPlayer receives it regardless of mode.
    /// </summary>
    public void FootSteps()
    {
        OnFootstepLeft?.Invoke();
    }

    /// <summary>
    /// Left foot contact. Use FootstepLeft in animation clip Function field.
    /// </summary>
    public void FootstepLeft()
    {
        OnFootstepLeft?.Invoke();
    }

    /// <summary>
    /// Right foot contact. Use FootstepRight in animation clip Function field.
    /// </summary>
    public void FootstepRight()
    {
        OnFootstepRight?.Invoke();
    }

    // ─── Generic ───

    public void OnCustomEvent(string eventName)
    {
        OnGenericEvent?.Invoke(eventName);
    }
}
