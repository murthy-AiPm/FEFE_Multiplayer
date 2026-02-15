using UnityEngine;

public struct AnimationContext
{
    public ThirdPersonController tps;
    public InputController input;
    public InputSnapshot snapshot;
    public MountController mountController; // NEW: for mounted state
    public CombatController combatController;
    public WeaponManager weaponManager;

    // Humanoid states
    public bool Moving => GetMoving();
    public bool CombatMode => input != null && input.isCombatMode;
    public bool Modified => input != null && input.isModified;
    public bool SecondaryHeld => input != null && input.isSecondaryAttack;
    public bool HoverMode => input != null && input.isHoverMode;
    public bool Grounded => tps != null && tps.isgrounded;
    public bool FreeFall => tps != null && tps.isfreeFall;
    public bool Sheathing => input != null && input.isSheating;
    public bool Crouching => input != null && input.isCrouch;

    public bool Dodging => combatController != null && combatController.IsDodging;
    public bool Blocking => combatController != null && combatController.IsBlocking;
    public bool BowDrawing => combatController != null && combatController.IsBowDrawing;
    public bool BowAiming => combatController != null && combatController.IsBowAiming;
    public int ActiveWeaponSlot => weaponManager != null ? weaponManager.ActiveSlot : 0;

    // Mounting states (NEW)
    public bool IsMounted => mountController != null && mountController.IsMounted;
    public bool IsTransitioning => mountController != null && mountController.IsTransitioning;

    public string DirString => input != null ? input.directions : "None";

    // Action system hook (set by your interaction code)
    public int ActionId;      // 0 = none. 1 = chest, 2 = ballista, etc.
    public bool ActionStart;  // edge trigger for starting the action (optional)

    /// <summary>
    /// Returns true if humanoid is moving OR if mounted and horse is moving.
    /// This allows existing "Moving" rules to work for both grounded and mounted states.
    /// </summary>
    private bool GetMoving()
    {
        // If mounted, check if the mount is moving
        if (IsMounted && mountController.CurrentMount != null)
        {
            return mountController.CurrentMount.IsMoving;
        }

        // Normal humanoid movement
        return input != null && input.isMoving;
    }
}