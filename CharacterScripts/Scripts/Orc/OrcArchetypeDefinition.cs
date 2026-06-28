using UnityEngine;

[CreateAssetMenu(menuName = "FEFE/Orc Archetype")]
public class OrcArchetypeDefinition : ScriptableObject
{
    [Header("Archetype")]
    [Tooltip("Gameplay identity used by archetype-specific targeting and combat rules.")]
    public OrcArchetype archetype = OrcArchetype.Grunt;

    [Header("Detection")]
    [Tooltip("Close-range awareness radius; targets inside it are noticed even outside the vision cone.")]
    public float closeDetectionRadius = 3f;
    [Tooltip("Maximum distance at which the orc can see a target.")]
    public float viewDistance = 15f;
    [Range(1f, 360f)]
    [Tooltip("Width in degrees of the orc's forward vision cone.")]
    public float viewAngle = 110f;
    [Tooltip("Seconds between target-detection scans. Lower values react faster but cost more CPU.")]
    public float detectionInterval = 0.25f;
    [Tooltip("How long a visible target must remain visible before patrol detection locks on.")]
    public float detectionTime = 0.35f;
    [Tooltip("How long a combat target may be unseen before the orc searches its last known position.")]
    public float loseSightGraceTime = 1.5f;
    [Tooltip("Maximum distance from home the orc normally pursues a target.")]
    public float pursueRadiusFromHome = 25f;
    [Tooltip("How long the orc searches the target's last known position after losing sight.")]
    public float searchDuration = 3f;
    [Tooltip("Height above the orc's pivot used as the origin of vision checks.")]
    public float eyeHeight = 1.7f;
    [Tooltip("Height above a target's pivot that the orc looks toward during vision checks.")]
    public float targetAimHeight = 1.2f;

    [Header("Ranged Alert")]
    [Tooltip("Maximum distance from home the orc commits to investigating a visible ranged attacker.")]
    public float investigateRadiusFromHome = 25f;
    [Tooltip("How long the orc guards while facing a ranged threat it cannot pursue.")]
    public float rangedHitGuardDuration = 1.5f;
    [Tooltip("When enabled, missed ranged shots landing nearby can alert the orc.")]
    public bool reactToNearbyRangedImpacts = true;
    [Tooltip("Seconds after a direct ranged hit during which nearby misses cannot replace the direct-hit direction.")]
    public float directRangedHitPriorityDuration = 1f;
    [Tooltip("Number of unseen ranged alerts within the alert window that makes the orc return home to warn camp.")]
    public int outOfViewRangedAlertReturnThreshold = 2;
    [Tooltip("Seconds allowed between unseen ranged alerts before their count resets.")]
    public float outOfViewRangedAlertWindow = 4f;
    [Tooltip("How long a ranged reaction suppresses the normal damage-driven stagger transition.")]
    public float rangedReactionStaggerSuppressTime = 0.25f;

    [Header("Movement")]
    [Tooltip("Use animation root motion for idle, walk, run, and reposition states. Leave off for in-place locomotion clips.")]
    public bool useLocomotionRootMotion;
    [Tooltip("Use animation root motion for committed combat clips such as attacks, parries, staggers, and death.")]
    public bool useCombatRootMotion = true;
    [Tooltip("NavMesh movement speed used while walking.")]
    public float walkSpeed = 2f;
    [Tooltip("NavMesh movement speed used for the wounded walk home at low health.")]
    public float damagedWalkSpeed = 1.2f;
    [Tooltip("NavMesh movement speed used while chasing or running.")]
    public float runSpeed = 6f;
    [Tooltip("How quickly the orc turns toward movement and combat targets.")]
    public float rotationSpeed = 6f;
    [Tooltip("Ideal standoff distance the orc keeps from its target while fighting; reposition spots are placed around this radius.")]
    public float preferredCombatDistance = 2.2f;
    [Tooltip("If the target gets closer than this, the orc sidesteps instead of standing still. Higher = repositions more eagerly.")]
    public float repositionDistance = 1.4f;
    [Tooltip("How long one reposition sidestep lasts before re-evaluating.")]
    public float repositionDuration = 0.7f;
    [Tooltip("How strongly a reposition move favors sideways motion around the target.")]
    public float repositionSideBias = 0.6f;

    [Header("Patrol")]
    [Tooltip("Maximum distance from home used when choosing random wander destinations.")]
    public float wanderRadius = 10f;
    [Tooltip("Shortest random idle pause between patrol movements.")]
    public float idleMinTime = 2f;
    [Tooltip("Longest random idle pause between patrol movements.")]
    public float idleMaxTime = 5f;
    [Tooltip("Minimum and maximum wait time at authored patrol points.")]
    public Vector2 patrolWaitTimeRange = new Vector2(1f, 3f);

    [Header("Utility")]
    [Tooltip("Seconds between combat decisions (attack/block/reposition). Lower = more reactive, more CPU.")]
    public float decisionInterval = 0.2f;
    [Tooltip("Max distance at which the orc will react (block/parry) to the target's incoming swing.")]
    public float targetAttackReactDistance = 3f;
    [Tooltip("Total cone (degrees) in front of the orc within which it can block/parry. Hits from outside this cone can't be defended.")]
    public float frontalDefenseAngle = 130f;

    [Header("Leash Threat")]
    [Tooltip("When enabled, the orc holds and taunts a visible target beyond its leash before returning home.")]
    public bool holdVisibleThreatAtLeash = true;
    [Tooltip("Multiplier applied to turning speed while facing a visible target at the leash boundary.")]
    public float leashThreatFaceSpeedMultiplier = 1.25f;
    [Tooltip("Max seconds the orc taunts a visible out-of-reach target at its leash boundary before returning home.")]
    public float leashThreatMaxDuration = 5f;
    [Range(0f, 1f)]
    [Tooltip("At/below this health fraction, a harassed orc stops ignoring its home leash and retreats.")]
    public float lowHealthPursueRevertRatio = 0.5f;
    [Tooltip("Crossfade time used when entering or restarting the leash-threat taunt animation.")]
    public float leashThreatTransitionDuration = 0.05f;

    [Header("Attacks")]
    [Tooltip("Weighted attack choices, including range, timing, cooldown, and weapon-hitbox selection for each move.")]
    public OrcAttackOption[] attacks =
    {
        new OrcAttackOption { attackIndex = 0, maxRange = 2.4f, weight = 1f }
    };
    [Tooltip("Extra distance beyond an attack's max range allowed before that attack is cancelled.")]
    public float attackCancelDistanceBuffer = 1.2f;
    [Tooltip("Seconds after a cancelled attack before the orc may choose another attack.")]
    public float attackCancelReengageDelay = 0.75f;
    [Tooltip("Allow animation-event attacks to be cancelled for distance before their first hitbox opens.")]
    public bool allowAnimationEventAttackCancelBeforeHitbox = true;
    [Tooltip("Allow animation-event attacks to be cancelled for distance after their hitbox has opened.")]
    public bool allowAnimationEventAttackCancelAfterHitbox = true;
    [Tooltip("Crossfade time used to return to locomotion when an attack is cancelled.")]
    public float attackCancelFade = 0.08f;
    [Tooltip("When enabled, the orc holds a real block while waiting for its attack cooldown.")]
    public bool blockDuringAttackCooldown = true;

    [Header("Block")]
    [Range(0f, 1f)]
    [Tooltip("Chance to choose a block when the orc reacts to an incoming attack.")]
    public float blockChance = 0.35f;
    [Tooltip("How long a chosen block remains active.")]
    public float blockDuration = 0.9f;
    [Tooltip("Minimum seconds after blocking before the orc may block again.")]
    public float blockCooldown = 1.4f;

    [Header("Parry")]
    [Range(0f, 1f)]
    [Tooltip("Chance to choose a parry when the orc reacts to an incoming attack.")]
    public float parryChance = 0.2f;
    [Tooltip("Total time the orc remains in its parry action.")]
    public float parryDuration = 0.65f;
    [Tooltip("Length of the successful-parry window within the parry action.")]
    public float parryActiveWindow = 0.22f;
    [Tooltip("Minimum seconds after parrying before the orc may parry again.")]
    public float parryCooldown = 2f;
    [Tooltip("Damage dealt back to an attacker when this orc successfully parries.")]
    public float parryDamage;

    [Header("Crowd Control")]
    [Tooltip("Max orcs in an attack slot on one target at once; extras hang back or pick a less-contested target.")]
    public int maxAttackersPerTarget = 4;

    [Header("Berserker Targeting")]
    [Tooltip("Seconds between target-ranking scans while a berserker is already fighting.")]
    public float berserkerTargetScanInterval = 0.35f;
    [Tooltip("Minimum seconds after switching targets before a berserker may switch again.")]
    public float berserkerRetargetCooldown = 1.25f;
    [Tooltip("How much better a new target's score must be before a berserker abandons its current target.")]
    public float berserkerSwitchScoreMargin = 0.75f;
    [Tooltip("How strongly distance affects berserker target choice (higher = favors closer targets more strongly).")]
    public float berserkerDistanceWeight = 1f;
    [Tooltip("How strongly a berserker favors wounded targets when choosing who to hit (higher = more drawn to low HP).")]
    public float berserkerLowHealthWeight = 6f;
    [Tooltip("Score penalty for targets already swarmed, so berserkers spread out.")]
    public float berserkerDogpilePenaltyWeight = 1.5f;
    [Tooltip("Preference for keeping the current target when alternatives have similar scores; higher reduces target ping-pong.")]
    public float berserkerCurrentTargetStickiness = 0.75f;

    [Header("Separation")]
    [Tooltip("Radius within which nearby orcs push away from one another.")]
    public float separationRadius = 1.2f;
    [Tooltip("Strength of the spacing push between nearby orcs.")]
    public float separationStrength = 2f;
}
