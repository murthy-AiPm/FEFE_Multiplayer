using System;
using System.Collections.Generic;
using UnityEngine;

public enum AnimLayer
{
    Base = 0,     // locomotion (full body, Animancer layer 0)
    Action = 1,   // interactions: chest, ballista, sheathe (Animancer layer 1 w/ upper body mask)
    Attack = 2,   // attacks (Animancer layer 2, optionally masked)
}

public enum TriggerMode
{
    None,
    Down,
    Held,
    Up,
}

public enum BoolParam
{
    Moving,
    CombatMode,
    Modified,
    SecondaryHeld,
    HoverMode,
    Grounded,
    FreeFall,
    IsMounted,
    IsTransitioning,
    Crouching,
    Dodging,
    IsDodgeStep,
    Blocking,
    BowDrawing,
    BowAiming,
    Equipping,
    Holstering,
    WeaponSlot0,   // fists
    WeaponSlot1,   // primary melee
    WeaponSlot2,   // bow
    PendingSlot1,
    PendingSlot2,
}

public enum InputEdge
{
    PrimaryDown,
    JumpDown,
    ActionHeld,
}

public enum Direction4
{
    Any,
    W,
    A,
    S,
    D
}

[Serializable]
public class RuleCondition
{
    [Header("Bool checks (optional)")]
    public bool useBool;
    public BoolParam boolParam;
    public bool boolValue;

    [Header("Input edge / held (optional)")]
    public bool useInput;
    public InputEdge input;
    public TriggerMode triggerMode; // Down/Held/Up

    [Header("Direction (optional)")]
    public bool useDirection;
    public Direction4 direction;

    [Header("Action id (optional)")]
    public bool useActionId;
    public int actionIdEquals;
}

[Serializable]
public class AnimationRule
{
    public string name;

    [Header("Decision")]
    public AnimLayer layer = AnimLayer.Base;
    public int priority = 0;              // higher wins
    public bool lockUntilEnd = false;     // attacks/actions
    public bool allowInterruptSameLayer = true;

    [Header("Layer Masking Override (optional)")]
    [Tooltip("If set, overrides the driver's default mask for this rule's layer. " +
             "Leave null to use the driver's per-layer default mask.")]
    public AvatarMask maskOverride;

    [Header("What to play")]
    public string animationKey;           // points to AnimationSetBase key

    [Header("When to play")]
    public List<RuleCondition> all = new List<RuleCondition>();
}

[Serializable]
public class ComboStep
{
    public string animationKey; // e.g. "Attack/Light1"
    public float bufferWindow = 0.25f; // how long before end you can queue next
}

[Serializable]
public class ComboProfile
{
    public string name; // e.g. "SwordLight"
    public List<ComboStep> steps = new List<ComboStep>();
}

[CreateAssetMenu(menuName = "Animancer/Rule System/Animation Rule Set")]
public class AnimationRuleSet : ScriptableObject
{
    [Header("Rules (evaluated every frame; highest priority match wins per layer)")]
    public List<AnimationRule> rules = new List<AnimationRule>();

    [Header("Combos (optional)")]
    public List<ComboProfile> combos = new List<ComboProfile>();
}