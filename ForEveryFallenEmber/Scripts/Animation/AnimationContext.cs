using UnityEngine;

public struct AnimationContext
{
    public ThirdPersonController tps;
    public InputController input;
    public InputSnapshot snapshot;

    public bool Moving => input != null && input.isMoving;
    public bool CombatMode => input != null && input.isCombatMode;
    public bool Modified => input != null && input.isModified;
    public bool SecondaryHeld => input != null && input.isSecondaryAttack;
    public bool HoverMode => input != null && input.isHoverMode;
    public bool Grounded => tps != null && tps.isgrounded;
    public bool FreeFall => tps != null && tps.isfreeFall;
    public bool Sheathing => input != null && input.isSheating;

    public string DirString => input != null ? input.directions : "None";

    // Action system hook (set by your interaction code)
    public int ActionId;      // 0 = none. 1 = chest, 2 = ballista, etc.
    public bool ActionStart;  // edge trigger for starting the action (optional)
}
