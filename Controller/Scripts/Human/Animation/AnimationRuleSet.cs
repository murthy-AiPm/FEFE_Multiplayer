using System;
using System.Collections.Generic;
using UnityEngine;

public enum AnimLayer
{
    Base = 0,     // locomotion
    Action = 1,   // interactions: chest, ballista
    Attack = 2,   // attacks
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
    Sheathing,
    IsMounted,        // NEW: true when riding a mount
    IsTransitioning,  // NEW: true during mount/dismount animation
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