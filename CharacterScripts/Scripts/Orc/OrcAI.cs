using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

[System.Serializable]
public class OrcAttackOption
{
    public string name = "";
    public int attackIndex;
    public float minRange = 0f;
    public float maxRange = 2.5f;
    public float weight = 1f;
    public float duration = 1.1f;
    public float cooldown = 1.2f;
    [Tooltip("Fallback only when useAnimationEventsForHitboxes is false.")]
    public float hitboxEnableDelay = 0.25f;
    [Tooltip("Fallback only when useAnimationEventsForHitboxes is false.")]
    public float hitboxActiveTime = 0.3f;
    public bool isHeavy;
    public OrcWeaponHitboxSelection hitboxSelection = OrcWeaponHitboxSelection.MainHand;

    [System.NonSerialized] public float cooldownTimer;
}

public enum OrcWeaponHitboxSelection
{
    MainHand,
    OffHand,
    Both
}

public enum OrcPatrolMode
{
    Wander,
    Loop,
    PingPong
}

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkObject))]
public class OrcAI : NetworkBehaviour, IDamageDefenseProvider
{
    public enum OrcState
    {
        Patrol,
        Combat,
        Stagger,
        Dead
    }

    public enum OrcSubState
    {
        Idle,
        Wander,
        Return,
        Approach,
        Reposition,
        Attack,
        Block,
        Parry,
        Stagger,
        Dead
    }

    [Header("Detection")]
    [Tooltip("Close-range awareness bubble. Targets inside this range are detected even outside the vision cone.")]
    [SerializeField] private float closeDetectionRadius = 3f;
    [Tooltip("Maximum distance for vision checks. Former detectionRadius value maps here for existing prefabs.")]
    [FormerlySerializedAs("detectionRadius")]
    [SerializeField] private float viewDistance = 15f;
    [Range(1f, 360f)]
    [SerializeField] private float viewAngle = 110f;
    [SerializeField] private float detectionInterval = 0.25f;
    [Tooltip("How long a visible target must stay visible before patrol detection locks on.")]
    [SerializeField] private float detectionTime = 0.35f;
    [Tooltip("Seconds a combat target can be out of sight before the orc searches the last known position.")]
    [SerializeField] private float loseSightGraceTime = 1.5f;
    [Tooltip("Maximum chase distance from home before the orc gives up and returns.")]
    [FormerlySerializedAs("leashRadius")]
    [SerializeField] private float pursueRadiusFromHome = 25f;
    [SerializeField] private float searchDuration = 3f;
    [SerializeField] private float eyeHeight = 1.7f;
    [SerializeField] private float targetAimHeight = 1.2f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask visionObstacleMask = ~0;

    [Header("Ranged Alert")]
    [Tooltip("How far from home the orc will commit to pursuing a visible ranged attacker.")]
    [SerializeField] private float investigateRadiusFromHome = 25f;
    [Tooltip("How long the orc guards while facing a ranged hit source it cannot pursue.")]
    [SerializeField] private float rangedHitGuardDuration = 1.5f;
    [Tooltip("Missed arrows that hit this close to the orc trigger investigation.")]
    [SerializeField] private bool reactToNearbyRangedImpacts = true;
    [Tooltip("Seconds after a direct arrow hit where nearby missed-arrow impacts cannot override the direct-hit direction.")]
    [SerializeField] private float directRangedHitPriorityDuration = 1f;
    [Tooltip("How many out-of-view ranged alerts within the window make the orc run home to alert camp.")]
    [SerializeField] private int outOfViewRangedAlertReturnThreshold = 2;
    [Tooltip("Seconds allowed between out-of-view ranged alerts before the count resets.")]
    [SerializeField] private float outOfViewRangedAlertWindow = 4f;

    [Header("Patrol")]
    [SerializeField] private OrcPatrolMode patrolMode = OrcPatrolMode.Wander;
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private bool randomizePatrolStartPoint = true;
    [SerializeField] private Vector2 patrolWaitTimeRange = new Vector2(1f, 3f);
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private float idleMinTime = 2f;
    [SerializeField] private float idleMaxTime = 5f;
    [SerializeField] private float arrivalThreshold = 1.5f;

    [Header("Movement")]
    [Tooltip("Use animation root motion for idle/walk/run/reposition states. Leave off when locomotion clips are in-place.")]
    [SerializeField] private bool useLocomotionRootMotion = false;
    [Tooltip("Use animation root motion for committed combat clips such as attacks, parries, staggers, and death.")]
    [SerializeField] private bool useCombatRootMotion = true;
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float rotationSpeed = 6f;
    [SerializeField] private float preferredCombatDistance = 2.2f;
    [SerializeField] private float repositionDistance = 1.4f;
    [SerializeField] private float repositionDuration = 0.7f;

    [Header("Utility")]
    [SerializeField] private float decisionInterval = 0.2f;
    [SerializeField] private float targetAttackReactDistance = 3f;
    [SerializeField] private float frontalDefenseAngle = 130f;

    [Header("Attacks")]
    [SerializeField] private OrcAttackOption[] attacks =
    {
        new OrcAttackOption { attackIndex = 0, maxRange = 2.4f, weight = 1f }
    };
    [Tooltip("How far beyond the active attack max range the target can move before the orc cancels the attack.")]
    [SerializeField] private float attackCancelDistanceBuffer = 1.2f;
    [Tooltip("Seconds after a cancelled attack before the orc may choose another attack.")]
    [SerializeField] private float attackCancelReengageDelay = 0.75f;
    [Tooltip("For animation-event attacks, allow distance cancel before the first hitbox enable event.")]
    [SerializeField] private bool allowAnimationEventAttackCancelBeforeHitbox = true;
    [Tooltip("For animation-event attacks, allow distance cancel after the hitbox has opened. Cancelling immediately disables weapon hitboxes.")]
    [SerializeField] private bool allowAnimationEventAttackCancelAfterHitbox = true;
    [Tooltip("Animator state path to fade to when an attack is cancelled. Existing controller uses the Locomtion typo.")]
    [SerializeField] private string locomotionStatePath = "Base Layer.Locomtion";
    [SerializeField] private float attackCancelFade = 0.08f;
    [Tooltip("After an attack finishes, hold real Block during the attack cooldown.")]
    [SerializeField] private bool blockDuringAttackCooldown = true;

    [Header("Block")]
    [Range(0f, 1f)]
    [SerializeField] private float blockChance = 0.35f;
    [SerializeField] private float blockDuration = 0.9f;
    [SerializeField] private float blockCooldown = 1.4f;

    [Header("Parry")]
    [Range(0f, 1f)]
    [SerializeField] private float parryChance = 0.2f;
    [SerializeField] private float parryDuration = 0.65f;
    [SerializeField] private float parryActiveWindow = 0.22f;
    [SerializeField] private float parryCooldown = 2f;

    [Header("Hitbox")]
    [Tooltip("When true, attack clips control hitbox windows through animation events. When false, OrcAI uses the attack option timing fields.")]
    [SerializeField] private bool useAnimationEventsForHitboxes = true;
    [Tooltip("Safety fallback only for animation-event mode. The hitbox disable animation event normally ends the attack.")]
    [SerializeField] private float animationEventAttackTimeout = 3f;
    [SerializeField] private HitboxController weaponHitbox;
    [SerializeField] private HitboxController offHandWeaponHitbox;
    [SerializeField] private WeaponData weaponData;

    [Header("Crowd Control")]
    [SerializeField] private int maxAttackersPerTarget = 4;

    [Header("Separation")]
    [SerializeField] private float separationRadius = 1.2f;
    [SerializeField] private float separationStrength = 2f;

    [Header("Gravity")]
    [SerializeField] private float gravityStrength = 20f;
    [SerializeField] private float groundCheckDistance = 0.4f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Death")]
    [SerializeField] private Collider[] collidersToDisableOnDeath;

    [Header("References")]
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private DamageReceiver damageReceiver;
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private AnimalSoundPlayer animalSoundPlayer;

    [Header("Debug")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color viewDistanceGizmoColor = new Color(1f, 1f, 0f, 0.2f);
    [SerializeField] private Color closeDetectionGizmoColor = new Color(0f, 0.75f, 1f, 0.35f);
    [SerializeField] private Color pursueRadiusGizmoColor = new Color(1f, 0f, 0f, 0.2f);
    [SerializeField] private Color investigateRadiusGizmoColor = new Color(0.75f, 0f, 1f, 0.2f);
    [SerializeField] private Color wanderRadiusGizmoColor = new Color(0f, 1f, 0f, 0.2f);
    [SerializeField] private Color preferredCombatDistanceGizmoColor = new Color(1f, 0.5f, 0f, 0.3f);
    [SerializeField] private Color patrolRouteGizmoColor = new Color(0.25f, 0.8f, 1f, 0.8f);

    private static readonly Dictionary<Transform, int> _attackerCounts = new Dictionary<Transform, int>();
    private static readonly List<OrcAI> _serverOrcs = new List<OrcAI>();

    private readonly Collider[] _detectionBuffer = new Collider[16];
    private readonly Collider[] _separationBuffer = new Collider[10];
    private readonly RaycastHit[] _lineOfSightHits = new RaycastHit[8];

    private int speedHash;
    private int deadHash;
    private int combatStateHash;
    private int attackIndexHash;
    private int attackHash;
    private int blockHash;
    private int parryHash;
    private int staggerHash;

    private Vector3 homePosition;
    private Transform currentTarget;
    private OrcAttackOption activeAttack;

    private float stateTimer;
    private float decisionTimer;
    private float detectionTimer;
    private float blockCooldownTimer;
    private float parryCooldownTimer;
    private float parryActiveTimer;
    private float hitboxTimer;
    private float verticalVelocity;
    private float nextBlockDuration = -1f;
    private float targetAwareness;
    private float loseSightTimer;
    private float suppressDamageReceivedStateChangeTimer;
    private float directRangedHitPriorityTimer;
    private float outOfViewRangedAlertTimer;
    private int outOfViewRangedAlertCount;
    private int currentPatrolPointIndex;
    private int patrolDirection = 1;
    private bool hitboxActive;
    private bool attackHitboxWindowStarted;
    private bool mainHandHitboxActive;
    private bool offHandHitboxActive;
    private bool postAttackBlockActive;
    private bool registeredAsAttacker;
    private bool stateInitialized;
    private bool movingToLastKnownPosition;
    private bool searchingLastKnownPosition;
    private bool rangedHitGuardActive;
    private bool rangedInvestigationActive;
    private bool boundedRangedInvestigationActive;
    private bool alertReturnHomeActive;
    private Vector3 repositionTarget;
    private Vector3 lastKnownTargetPosition;
    private Vector3 rangedThreatPosition;
    private Transform awarenessTarget;

    public OrcState State { get; private set; } = OrcState.Patrol;
    public OrcSubState SubState { get; private set; } = OrcSubState.Idle;

    public bool IsDefenseInvincible => false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        homePosition = transform.position;
        if (IsServer && !_serverOrcs.Contains(this))
            _serverOrcs.Add(this);

        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (vitalManager == null) vitalManager = GetComponent<VitalManager>();
        if (damageReceiver == null) damageReceiver = GetComponent<DamageReceiver>();
        if (animalSoundPlayer == null) animalSoundPlayer = GetComponent<AnimalSoundPlayer>();

        InitializeWeaponHitbox(weaponHitbox);
        InitializeWeaponHitbox(offHandWeaponHitbox);

        if (damageReceiver != null)
        {
            damageReceiver.OnDamageReceived += HandleDamageReceived;
            damageReceiver.OnDamageBlocked += HandleDamageBlocked;
            damageReceiver.OnDamageParried += HandleDamageParried;
            damageReceiver.OnRangedDamageReceived += HandleRangedDamageReceived;
        }

        speedHash = Animator.StringToHash("Speed");
        deadHash = Animator.StringToHash("Dead");
        combatStateHash = Animator.StringToHash("CombatState");
        attackIndexHash = Animator.StringToHash("AttackIndex");
        attackHash = Animator.StringToHash("Attack");
        blockHash = Animator.StringToHash("Block");
        parryHash = Animator.StringToHash("Parry");
        staggerHash = Animator.StringToHash("Stagger");

        if (agent != null)
        {
            agent.updatePosition = !ShouldUseRootMotionForState(OrcState.Patrol, OrcSubState.Idle);
            agent.updateRotation = false;
            agent.avoidancePriority = Random.Range(20, 80);
            agent.speed = walkSpeed;
        }

        if (vitalManager != null)
            vitalManager.OnDeath += OnDeath;

        InitializePatrolRoute();
        SetState(OrcState.Patrol, OrcSubState.Idle);
    }

    public override void OnNetworkDespawn()
    {
        UnregisterAttacker();
        _serverOrcs.Remove(this);

        if (vitalManager != null)
            vitalManager.OnDeath -= OnDeath;

        if (damageReceiver != null)
        {
            damageReceiver.OnDamageReceived -= HandleDamageReceived;
            damageReceiver.OnDamageBlocked -= HandleDamageBlocked;
            damageReceiver.OnDamageParried -= HandleDamageParried;
            damageReceiver.OnRangedDamageReceived -= HandleRangedDamageReceived;
        }

        base.OnNetworkDespawn();
    }

    public static void NotifyNearbyRangedImpact(Vector3 impactPosition, Vector3 sourcePosition, NetworkObject attackerObject)
    {
        for (int i = _serverOrcs.Count - 1; i >= 0; i--)
        {
            OrcAI orc = _serverOrcs[i];
            if (orc == null)
            {
                _serverOrcs.RemoveAt(i);
                continue;
            }

            orc.HandleNearbyRangedImpact(impactPosition, sourcePosition, attackerObject);
        }
    }

    private void Update()
    {
        if (!IsServer) return;
        if (State == OrcState.Dead) return;

        TickTimers();

        switch (State)
        {
            case OrcState.Patrol:
                UpdatePatrol();
                break;
            case OrcState.Combat:
                UpdateCombat();
                break;
            case OrcState.Stagger:
                UpdateStagger();
                break;
        }

        UpdateAnimatorSpeed();
    }

    private void OnAnimatorMove()
    {
        if (!IsServer || !ShouldUseRootMotionForCurrentState() || animator == null || agent == null) return;

        agent.speed = 100f;
        Vector3 rootPosition = animator.rootPosition;

        Vector3 rayOrigin = rootPosition + Vector3.up * 0.5f;
        bool grounded = Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit groundHit,
            groundCheckDistance + 0.5f,
            groundLayer);

        if (grounded)
        {
            rootPosition.y = groundHit.point.y;
            verticalVelocity = 0f;
        }
        else
        {
            verticalVelocity -= gravityStrength * Time.deltaTime;
            rootPosition.y += verticalVelocity * Time.deltaTime;
        }

        Vector3 separation = Vector3.zero;
        int neighbourCount = Physics.OverlapSphereNonAlloc(rootPosition, separationRadius, _separationBuffer);
        for (int i = 0; i < neighbourCount; i++)
        {
            var col = _separationBuffer[i];
            if (col == null || col.transform == transform || col.isTrigger) continue;
            if (col.GetComponentInParent<OrcAI>() == null && col.GetComponentInParent<BearAI>() == null) continue;

            Vector3 away = rootPosition - col.transform.position;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist < 0.001f)
            {
                away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                dist = 1f;
            }

            separation += (away / dist) * (1f - dist / separationRadius);
        }

        if (separation != Vector3.zero)
            rootPosition += separation * (separationStrength * Time.deltaTime);

        agent.nextPosition = rootPosition;
        transform.position = rootPosition;
    }

    private void TickTimers()
    {
        decisionTimer -= Time.deltaTime;
        detectionTimer -= Time.deltaTime;

        if (blockCooldownTimer > 0f) blockCooldownTimer -= Time.deltaTime;
        if (parryCooldownTimer > 0f) parryCooldownTimer -= Time.deltaTime;
        if (parryActiveTimer > 0f) parryActiveTimer -= Time.deltaTime;
        if (suppressDamageReceivedStateChangeTimer > 0f) suppressDamageReceivedStateChangeTimer -= Time.deltaTime;
        if (directRangedHitPriorityTimer > 0f) directRangedHitPriorityTimer -= Time.deltaTime;
        if (outOfViewRangedAlertTimer > 0f)
        {
            outOfViewRangedAlertTimer -= Time.deltaTime;
            if (outOfViewRangedAlertTimer <= 0f)
                outOfViewRangedAlertCount = 0;
        }

        if (attacks != null)
        {
            for (int i = 0; i < attacks.Length; i++)
            {
                if (attacks[i] != null && attacks[i].cooldownTimer > 0f)
                    attacks[i].cooldownTimer -= Time.deltaTime;
            }
        }

        if (hitboxActive && !useAnimationEventsForHitboxes)
        {
            hitboxTimer -= Time.deltaTime;
            if (hitboxTimer <= 0f)
                DisableWeaponHitbox();
        }
    }

    private void UpdatePatrol()
    {
        bool canAcquireTarget = SubState != OrcSubState.Return ||
                                movingToLastKnownPosition ||
                                searchingLastKnownPosition ||
                                rangedInvestigationActive ||
                                alertReturnHomeActive ||
                                Vector3.Distance(transform.position, homePosition) < pursueRadiusFromHome;
        Transform target = canAcquireTarget ? FindNearestDetectedTarget() : null;
        if (target != null)
        {
            bool targetInsideInvestigationRadius =
                Vector3.Distance(homePosition, target.position) <= investigateRadiusFromHome;
            bool targetInsidePursueRadius =
                Vector3.Distance(homePosition, target.position) <= pursueRadiusFromHome;
            bool targetRequiresAlertReturn =
                (rangedInvestigationActive || alertReturnHomeActive) &&
                (!targetInsidePursueRadius || !targetInsideInvestigationRadius);

            if (targetRequiresAlertReturn)
            {
                BeginAlertReturnHome(target.position);
                return;
            }

            currentTarget = target;
            outOfViewRangedAlertCount = 0;
            outOfViewRangedAlertTimer = 0f;
            movingToLastKnownPosition = false;
            searchingLastKnownPosition = false;
            rangedInvestigationActive = false;
            boundedRangedInvestigationActive = false;
            alertReturnHomeActive = false;
            lastKnownTargetPosition = target.position;
            loseSightTimer = loseSightGraceTime;
            SetState(OrcState.Combat, OrcSubState.Approach);
            return;
        }

        switch (SubState)
        {
            case OrcSubState.Idle:
                stateTimer -= Time.deltaTime;
                if (searchingLastKnownPosition)
                    RotateToward(rangedThreatPosition - transform.position);

                if (stateTimer <= 0f)
                {
                    if (searchingLastKnownPosition)
                    {
                        searchingLastKnownPosition = false;
                        rangedInvestigationActive = false;
                        boundedRangedInvestigationActive = false;
                        SetState(OrcState.Patrol, OrcSubState.Return);
                        return;
                    }

                    if (TryGetNextPatrolDestination(out Vector3 patrolDestination))
                    {
                        agent.SetDestination(patrolDestination);
                        SetState(OrcState.Patrol, OrcSubState.Wander);
                    }
                    else
                    {
                        stateTimer = GetPatrolWaitDuration();
                    }
                }
                break;

            case OrcSubState.Wander:
                if (!agent.pathPending && agent.remainingDistance <= arrivalThreshold)
                    SetState(OrcState.Patrol, OrcSubState.Idle);
                RotateToward(agent.desiredVelocity);
                break;

            case OrcSubState.Return:
                Vector3 destination = movingToLastKnownPosition ? lastKnownTargetPosition : homePosition;
                agent.SetDestination(destination);
                RotateToward(agent.desiredVelocity);
                if (!agent.pathPending && agent.remainingDistance <= arrivalThreshold)
                {
                    if (movingToLastKnownPosition)
                    {
                        movingToLastKnownPosition = false;
                        searchingLastKnownPosition = true;
                        SetState(OrcState.Patrol, OrcSubState.Idle);
                        stateTimer = searchDuration;
                    }
                    else
                    {
                        alertReturnHomeActive = false;
                        outOfViewRangedAlertCount = 0;
                        outOfViewRangedAlertTimer = 0f;
                        SetState(OrcState.Patrol, OrcSubState.Idle);
                    }
                }
                break;
        }
    }

    private void UpdateCombat()
    {
        if (rangedHitGuardActive && currentTarget == null)
        {
            Transform target = FindNearestDetectedTarget();
            if (target != null)
            {
                rangedHitGuardActive = false;
                currentTarget = target;
                lastKnownTargetPosition = target.position;
                loseSightTimer = loseSightGraceTime;
                SetState(OrcState.Combat, OrcSubState.Approach);
                return;
            }

            UpdateBlock();
            return;
        }

        if (!ValidateCombatTarget())
            return;

        if (!UpdateCurrentTargetVisibility())
            return;

        if (detectionTimer <= 0f)
        {
            Transform betterTarget = FindLessContestedVisibleTarget();
            if (betterTarget != null && betterTarget != currentTarget)
            {
                UnregisterAttacker();
                currentTarget = betterTarget;
                lastKnownTargetPosition = betterTarget.position;
                loseSightTimer = loseSightGraceTime;
            }
        }

        Vector3 toTarget = currentTarget.position - transform.position;
        float distToTarget = toTarget.magnitude;

        switch (SubState)
        {
            case OrcSubState.Approach:
                UpdateApproach(distToTarget);
                break;
            case OrcSubState.Reposition:
                UpdateReposition();
                break;
            case OrcSubState.Attack:
                UpdateAttack();
                break;
            case OrcSubState.Block:
                UpdateBlock();
                break;
            case OrcSubState.Parry:
                UpdateParry();
                break;
        }
    }

    private void UpdateApproach(float distToTarget)
    {
        if (decisionTimer <= 0f)
        {
            decisionTimer = decisionInterval;
            if (TryChooseCombatAction(distToTarget))
                return;
        }

        agent.SetDestination(currentTarget.position);
        RotateToward(currentTarget.position - transform.position);
    }

    private void UpdateReposition()
    {
        stateTimer -= Time.deltaTime;
        agent.SetDestination(repositionTarget);
        RotateToward(currentTarget.position - transform.position);

        if (stateTimer <= 0f || (!agent.pathPending && agent.remainingDistance <= arrivalThreshold))
            SetState(OrcState.Combat, OrcSubState.Approach);
    }

    private void UpdateAttack()
    {
        stateTimer -= Time.deltaTime;
        if (currentTarget != null)
        {
            if (ShouldCancelAttackForDistance())
            {
                CancelAttackAndApproach();
                return;
            }

            RotateToward(currentTarget.position - transform.position);
        }

        if (stateTimer <= 0f)
            CompleteAttack();
    }

    private void UpdateBlock()
    {
        stateTimer -= Time.deltaTime;
        if (currentTarget != null)
            RotateToward(currentTarget.position - transform.position);
        else if (rangedHitGuardActive)
            RotateToward(rangedThreatPosition - transform.position);

        if (stateTimer <= 0f)
        {
            if (rangedHitGuardActive)
            {
                rangedHitGuardActive = false;
                blockCooldownTimer = blockCooldown;
                SetState(OrcState.Patrol, OrcSubState.Return);
                return;
            }

            if (postAttackBlockActive)
                postAttackBlockActive = false;
            else
                blockCooldownTimer = blockCooldown;

            SetState(currentTarget != null ? OrcState.Combat : OrcState.Patrol,
                currentTarget != null ? OrcSubState.Approach : OrcSubState.Return);
        }
    }

    private void UpdateParry()
    {
        stateTimer -= Time.deltaTime;
        if (currentTarget != null)
            RotateToward(currentTarget.position - transform.position);

        if (stateTimer <= 0f)
        {
            parryCooldownTimer = parryCooldown;
            parryActiveTimer = 0f;
            BeginBlock(blockDuration, true);
        }
    }

    private void UpdateStagger()
    {
        stateTimer -= Time.deltaTime;
        agent.ResetPath();
        if (stateTimer <= 0f)
            SetState(currentTarget != null ? OrcState.Combat : OrcState.Patrol,
                currentTarget != null ? OrcSubState.Approach : OrcSubState.Return);
    }

    private bool TryChooseCombatAction(float distToTarget)
    {
        bool targetAttacking = IsTargetAttacking();
        bool targetInFront = IsAttackerInDefenseCone(currentTarget);

        if (targetAttacking && targetInFront && distToTarget <= targetAttackReactDistance)
        {
            if (parryCooldownTimer <= 0f && Random.value <= parryChance)
            {
                SetState(OrcState.Combat, OrcSubState.Parry);
                return true;
            }

            if (blockCooldownTimer <= 0f && Random.value <= blockChance)
            {
                BeginBlock(blockDuration, false);
                return true;
            }
        }

        OrcAttackOption attack = ChooseAttack(distToTarget);
        if (attack != null && !IsCrowded())
        {
            RegisterAttacker();
            BeginAttack(attack);
            return true;
        }

        if (distToTarget < repositionDistance)
        {
            BeginReposition();
            return true;
        }

        return false;
    }

    private OrcAttackOption ChooseAttack(float distance)
    {
        if (attacks == null || attacks.Length == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < attacks.Length; i++)
        {
            var attack = attacks[i];
            if (!IsAttackValid(attack, distance)) continue;
            totalWeight += Mathf.Max(0f, attack.weight);
        }

        if (totalWeight <= 0f)
            return null;

        float pick = Random.value * totalWeight;
        for (int i = 0; i < attacks.Length; i++)
        {
            var attack = attacks[i];
            if (!IsAttackValid(attack, distance)) continue;

            pick -= Mathf.Max(0f, attack.weight);
            if (pick <= 0f)
                return attack;
        }

        return null;
    }

    private bool IsAttackValid(OrcAttackOption attack, float distance)
    {
        if (attack == null) return false;
        if (attack.cooldownTimer > 0f) return false;
        return distance >= attack.minRange && distance <= attack.maxRange;
    }

    private void BeginAttack(OrcAttackOption attack)
    {
        activeAttack = attack;
        SetState(OrcState.Combat, OrcSubState.Attack);
    }

    private void BeginBlock(float duration, bool fromAttackCooldown)
    {
        nextBlockDuration = Mathf.Max(0f, duration);
        postAttackBlockActive = fromAttackCooldown;
        SetState(OrcState.Combat, OrcSubState.Block);
    }

    private void BeginReposition()
    {
        Vector3 away = transform.position - currentTarget.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
            away = -transform.forward;

        Vector3 side = Vector3.Cross(Vector3.up, away.normalized);
        if (Random.value < 0.5f)
            side = -side;

        Vector3 desired = transform.position + (away.normalized + side * 0.6f).normalized * preferredCombatDistance;
        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, preferredCombatDistance, NavMesh.AllAreas))
            repositionTarget = hit.position;
        else
            repositionTarget = transform.position + away.normalized * preferredCombatDistance;

        SetState(OrcState.Combat, OrcSubState.Reposition);
    }

    private void SetState(OrcState newState, OrcSubState newSubState)
    {
        if (stateInitialized &&
            State == newState &&
            SubState == newSubState &&
            newSubState != OrcSubState.Attack &&
            newSubState != OrcSubState.Block)
            return;

        if (SubState == OrcSubState.Block && animator != null)
            animator.SetBool(blockHash, false);

        State = newState;
        SubState = newSubState;
        stateInitialized = true;

        if (agent != null && agent.enabled)
            agent.updatePosition = !ShouldUseRootMotionForCurrentState();

        if (animator != null)
            animator.SetInteger(combatStateHash, (int)newSubState);

        switch (newSubState)
        {
            case OrcSubState.Idle:
                stateTimer = GetPatrolWaitDuration();
                StopAgent();
                break;

            case OrcSubState.Wander:
                ResumeAgent();
                break;

            case OrcSubState.Return:
                UnregisterAttacker();
                currentTarget = null;
                ResumeAgent();
                break;

            case OrcSubState.Approach:
                ResumeAgent();
                break;

            case OrcSubState.Reposition:
                stateTimer = repositionDuration;
                ResumeAgent();
                break;

            case OrcSubState.Attack:
                stateTimer = useAnimationEventsForHitboxes
                    ? Mathf.Max(0.1f, animationEventAttackTimeout)
                    : activeAttack != null ? activeAttack.duration : 1f;
                StopAgent();
                attackHitboxWindowStarted = false;
                if (animator != null)
                {
                    animator.SetFloat(attackIndexHash, activeAttack != null ? activeAttack.attackIndex : 0f);
                    animator.SetTrigger(attackHash);
                }
                SetWeaponHitboxContext();
                animalSoundPlayer?.PlayAttackSound();
                if (!useAnimationEventsForHitboxes)
                    Invoke(nameof(EnableSelectedWeaponHitbox), activeAttack != null ? activeAttack.hitboxEnableDelay : 0.25f);
                break;

            case OrcSubState.Block:
                stateTimer = nextBlockDuration >= 0f ? nextBlockDuration : blockDuration;
                nextBlockDuration = -1f;
                StopAgent();
                if (animator != null)
                    animator.SetBool(blockHash, true);
                break;

            case OrcSubState.Parry:
                stateTimer = parryDuration;
                parryActiveTimer = parryActiveWindow;
                StopAgent();
                if (animator != null)
                    animator.SetTrigger(parryHash);
                break;

            case OrcSubState.Stagger:
                stateTimer = 0.45f;
                StopAgent();
                DisableWeaponHitbox();
                if (animator != null)
                    animator.SetTrigger(staggerHash);
                break;

            case OrcSubState.Dead:
                UnregisterAttacker();
                currentTarget = null;
                StopAgent();
                agent.enabled = false;
                DisableWeaponHitbox();
                if (animator != null)
                    animator.SetBool(deadHash, true);
                animalSoundPlayer?.PlayDeathSound();
                DisableCollidersClientRpc();
                break;
        }
    }

    private void StopAgent()
    {
        if (agent == null || !agent.enabled) return;

        agent.isStopped = true;
        agent.ResetPath();
        agent.velocity = Vector3.zero;
        agent.nextPosition = transform.position;
    }

    private void ResumeAgent()
    {
        if (agent == null || !agent.enabled) return;

        agent.isStopped = false;
        agent.nextPosition = transform.position;
    }

    private bool UpdateCurrentTargetVisibility()
    {
        if (currentTarget == null)
            return false;

        if (CanPerceiveTarget(currentTarget, out bool closeDetected))
        {
            lastKnownTargetPosition = currentTarget.position;
            loseSightTimer = loseSightGraceTime;
            return true;
        }

        if (closeDetected)
            return true;

        loseSightTimer -= Time.deltaTime;
        if (loseSightTimer > 0f)
            return true;

        BeginSearchLastKnownPosition();
        return false;
    }

    private bool ValidateCombatTarget()
    {
        if (currentTarget == null || !IsTargetAlive(currentTarget))
        {
            HandleInvalidCombatTarget();
            return false;
        }

        float distToHome = Vector3.Distance(transform.position, homePosition);
        if (distToHome > pursueRadiusFromHome)
        {
            HandleInvalidCombatTarget();
            return false;
        }

        return true;
    }

    private void BeginSearchLastKnownPosition()
    {
        CancelInvoke(nameof(EnableSelectedWeaponHitbox));
        DisableWeaponHitbox();

        activeAttack = null;
        attackHitboxWindowStarted = false;
        postAttackBlockActive = false;
        nextBlockDuration = -1f;
        targetAwareness = 0f;
        awarenessTarget = null;

        if (animator != null)
        {
            animator.ResetTrigger(attackHash);
            animator.SetBool(blockHash, false);

            if (SubState == OrcSubState.Attack && !string.IsNullOrEmpty(locomotionStatePath))
                animator.CrossFade(locomotionStatePath, attackCancelFade, 0);
        }

        UnregisterAttacker();
        currentTarget = null;
        movingToLastKnownPosition = true;
        searchingLastKnownPosition = false;
        rangedHitGuardActive = false;
        rangedInvestigationActive = false;
        boundedRangedInvestigationActive = false;
        alertReturnHomeActive = false;
        outOfViewRangedAlertCount = 0;
        outOfViewRangedAlertTimer = 0f;
        SetState(OrcState.Patrol, OrcSubState.Return);
    }

    private void HandleInvalidCombatTarget()
    {
        CancelInvoke(nameof(EnableSelectedWeaponHitbox));
        DisableWeaponHitbox();

        activeAttack = null;
        attackHitboxWindowStarted = false;
        postAttackBlockActive = false;
        nextBlockDuration = -1f;
        targetAwareness = 0f;
        awarenessTarget = null;
        movingToLastKnownPosition = false;
        searchingLastKnownPosition = false;
        rangedHitGuardActive = false;
        rangedInvestigationActive = false;
        boundedRangedInvestigationActive = false;
        alertReturnHomeActive = false;

        if (animator != null)
        {
            animator.ResetTrigger(attackHash);
            animator.SetBool(blockHash, false);

            if (SubState == OrcSubState.Attack && !string.IsNullOrEmpty(locomotionStatePath))
                animator.CrossFade(locomotionStatePath, attackCancelFade, 0);
        }

        UnregisterAttacker();
        SetState(OrcState.Patrol, OrcSubState.Return);
    }

    private void BeginPursueRangedAttacker(Transform attacker)
    {
        if (attacker == null)
            return;

        CancelInvoke(nameof(EnableSelectedWeaponHitbox));
        DisableWeaponHitbox();
        UnregisterAttacker();

        activeAttack = null;
        attackHitboxWindowStarted = false;
        postAttackBlockActive = false;
        nextBlockDuration = -1f;
        movingToLastKnownPosition = false;
        searchingLastKnownPosition = false;
        rangedHitGuardActive = false;
        rangedInvestigationActive = false;
        boundedRangedInvestigationActive = false;
        alertReturnHomeActive = false;
        targetAwareness = detectionTime;
        awarenessTarget = attacker;
        currentTarget = attacker;
        lastKnownTargetPosition = attacker.position;
        loseSightTimer = loseSightGraceTime;

        if (animator != null)
        {
            animator.ResetTrigger(attackHash);
            animator.SetBool(blockHash, false);

            if (SubState == OrcSubState.Attack && !string.IsNullOrEmpty(locomotionStatePath))
                animator.CrossFade(locomotionStatePath, attackCancelFade, 0);
        }

        SetState(OrcState.Combat, OrcSubState.Approach);
    }

    private void BeginRangedHitGuard(Vector3 sourcePosition)
    {
        CancelInvoke(nameof(EnableSelectedWeaponHitbox));
        DisableWeaponHitbox();
        UnregisterAttacker();

        activeAttack = null;
        attackHitboxWindowStarted = false;
        postAttackBlockActive = false;
        nextBlockDuration = -1f;
        currentTarget = null;
        awarenessTarget = null;
        targetAwareness = 0f;
        movingToLastKnownPosition = false;
        searchingLastKnownPosition = false;
        rangedHitGuardActive = true;
        rangedInvestigationActive = false;
        boundedRangedInvestigationActive = false;
        alertReturnHomeActive = false;
        rangedThreatPosition = sourcePosition;

        if (animator != null)
        {
            animator.ResetTrigger(attackHash);
            animator.SetBool(blockHash, false);

            if (SubState == OrcSubState.Attack && !string.IsNullOrEmpty(locomotionStatePath))
                animator.CrossFade(locomotionStatePath, attackCancelFade, 0);
        }

        RotateToward(sourcePosition - transform.position);
        BeginBlock(rangedHitGuardDuration, false);
    }

    private void BeginInvestigateRangedSource(Vector3 sourcePosition)
    {
        CancelInvoke(nameof(EnableSelectedWeaponHitbox));
        DisableWeaponHitbox();
        UnregisterAttacker();

        activeAttack = null;
        attackHitboxWindowStarted = false;
        postAttackBlockActive = false;
        nextBlockDuration = -1f;
        currentTarget = null;
        awarenessTarget = null;
        targetAwareness = 0f;
        searchingLastKnownPosition = false;
        rangedHitGuardActive = false;
        rangedThreatPosition = sourcePosition;
        lastKnownTargetPosition = GetRangedInvestigationDestination(sourcePosition);
        movingToLastKnownPosition = true;
        rangedInvestigationActive = true;
        boundedRangedInvestigationActive = IsRangedSourceOutsideInvestigationRadius(sourcePosition);
        alertReturnHomeActive = false;

        if (animator != null)
        {
            animator.ResetTrigger(attackHash);
            animator.SetBool(blockHash, false);

            if (SubState == OrcSubState.Attack && !string.IsNullOrEmpty(locomotionStatePath))
                animator.CrossFade(locomotionStatePath, attackCancelFade, 0);
        }

        RotateToward(sourcePosition - transform.position);
        SetState(OrcState.Patrol, OrcSubState.Return);
    }

    private void BeginAlertReturnHome(Vector3 spottedTargetPosition)
    {
        CancelInvoke(nameof(EnableSelectedWeaponHitbox));
        DisableWeaponHitbox();
        UnregisterAttacker();

        activeAttack = null;
        attackHitboxWindowStarted = false;
        postAttackBlockActive = false;
        nextBlockDuration = -1f;
        currentTarget = null;
        awarenessTarget = null;
        targetAwareness = 0f;
        movingToLastKnownPosition = false;
        searchingLastKnownPosition = false;
        rangedHitGuardActive = false;
        rangedInvestigationActive = false;
        boundedRangedInvestigationActive = false;
        alertReturnHomeActive = true;
        rangedThreatPosition = spottedTargetPosition;
        outOfViewRangedAlertCount = 0;
        outOfViewRangedAlertTimer = 0f;

        if (animator != null)
        {
            animator.ResetTrigger(attackHash);
            animator.SetBool(blockHash, false);
        }

        SetState(OrcState.Patrol, OrcSubState.Return);
    }

    private Vector3 GetRangedInvestigationDestination(Vector3 sourcePosition)
    {
        float radius = Mathf.Max(0f, investigateRadiusFromHome);
        Vector3 fromHome = sourcePosition - homePosition;
        fromHome.y = 0f;

        Vector3 destination = sourcePosition;
        if (radius > 0f && fromHome.sqrMagnitude > radius * radius)
            destination = homePosition + fromHome.normalized * radius;

        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, arrivalThreshold * 2f, NavMesh.AllAreas))
            return hit.position;

        return destination;
    }

    private bool IsRangedSourceOutsideInvestigationRadius(Vector3 sourcePosition)
    {
        float radius = Mathf.Max(0f, investigateRadiusFromHome);
        if (radius <= 0f)
            return true;

        Vector3 fromHome = sourcePosition - homePosition;
        fromHome.y = 0f;
        return fromHome.sqrMagnitude > radius * radius;
    }

    private bool IsRangedSourceOutsideViewDistance(Vector3 sourcePosition)
    {
        float distance = Mathf.Max(0f, viewDistance);
        if (distance <= 0f)
            return true;

        Vector3 toSource = sourcePosition - transform.position;
        toSource.y = 0f;
        return toSource.sqrMagnitude > distance * distance;
    }

    private bool RegisterOutOfViewRangedAlert()
    {
        if (outOfViewRangedAlertReturnThreshold <= 1)
            return true;

        if (outOfViewRangedAlertTimer <= 0f)
            outOfViewRangedAlertCount = 0;

        outOfViewRangedAlertCount++;
        outOfViewRangedAlertTimer = Mathf.Max(0.1f, outOfViewRangedAlertWindow);
        return outOfViewRangedAlertCount >= outOfViewRangedAlertReturnThreshold;
    }

    private void UpdateAnimatorSpeed()
    {
        if (animator == null) return;

        float animSpeed = 0f;
        switch (SubState)
        {
            case OrcSubState.Wander:
            case OrcSubState.Return:
            case OrcSubState.Reposition:
                bool shouldRunPatrolMovement =
                    SubState == OrcSubState.Return &&
                    (alertReturnHomeActive || (movingToLastKnownPosition && rangedInvestigationActive));
                animSpeed = shouldRunPatrolMovement ? 1f : 0.5f;
                if (!ShouldUseRootMotionForCurrentState() && agent != null)
                    agent.speed = shouldRunPatrolMovement ? runSpeed : walkSpeed;
                break;
            case OrcSubState.Approach:
                animSpeed = 1f;
                if (!ShouldUseRootMotionForCurrentState() && agent != null) agent.speed = runSpeed;
                break;
        }

        animator.SetFloat(speedHash, animSpeed, 0.15f, Time.deltaTime);
    }

    private bool ShouldUseRootMotionForCurrentState()
    {
        return ShouldUseRootMotionForState(State, SubState);
    }

    private bool ShouldUseRootMotionForState(OrcState state, OrcSubState subState)
    {
        if (state == OrcState.Dead)
            return useCombatRootMotion;

        switch (subState)
        {
            case OrcSubState.Idle:
            case OrcSubState.Wander:
            case OrcSubState.Return:
            case OrcSubState.Approach:
            case OrcSubState.Reposition:
                return useLocomotionRootMotion;

            case OrcSubState.Attack:
            case OrcSubState.Parry:
            case OrcSubState.Stagger:
            case OrcSubState.Dead:
                return useCombatRootMotion;

            case OrcSubState.Block:
            default:
                return false;
        }
    }

    public void HitboxEnable()
    {
        EnableSelectedWeaponHitbox();
    }

    public void HitboxDisable()
    {
        DisableWeaponHitbox();
        TryCompleteAnimationEventAttack();
    }

    public void MainHandHitboxEnable()
    {
        EnableSpecificWeaponHitbox(weaponHitbox);
    }

    public void MainHandHitboxDisable()
    {
        if (!IsServer) return;
        DisableSpecificWeaponHitbox(weaponHitbox);
        TryCompleteAnimationEventAttack();
    }

    public void OffHandHitboxEnable()
    {
        EnableSpecificWeaponHitbox(offHandWeaponHitbox);
    }

    public void OffHandHitboxDisable()
    {
        if (!IsServer) return;
        DisableSpecificWeaponHitbox(offHandWeaponHitbox);
        TryCompleteAnimationEventAttack();
    }

    public void BothHitboxesEnable()
    {
        EnableSpecificWeaponHitbox(weaponHitbox);
        EnableSpecificWeaponHitbox(offHandWeaponHitbox);
    }

    public void BothHitboxesDisable()
    {
        DisableWeaponHitbox();
        TryCompleteAnimationEventAttack();
    }

    private void EnableSelectedWeaponHitbox()
    {
        if (!IsServer) return;
        if (SubState != OrcSubState.Attack) return;

        foreach (var hitbox in GetActiveWeaponHitboxes())
        {
            EnableSpecificWeaponHitbox(hitbox);
        }
    }

    private void DisableWeaponHitbox()
    {
        if (!IsServer) return;

        DisableSpecificWeaponHitbox(weaponHitbox);
        DisableSpecificWeaponHitbox(offHandWeaponHitbox);
        mainHandHitboxActive = false;
        offHandHitboxActive = false;
        hitboxActive = false;
    }

    private void EnableSpecificWeaponHitbox(HitboxController hitbox)
    {
        if (!IsServer || hitbox == null || SubState != OrcSubState.Attack) return;

        hitbox.EnableHitbox();
        attackHitboxWindowStarted = true;
        SetHitboxActiveFlag(hitbox, true);

        if (!useAnimationEventsForHitboxes)
            hitboxTimer = activeAttack != null ? activeAttack.hitboxActiveTime : 0.3f;
    }

    private void DisableSpecificWeaponHitbox(HitboxController hitbox)
    {
        if (hitbox != null)
            hitbox.DisableHitbox();

        SetHitboxActiveFlag(hitbox, false);
    }

    private void SetHitboxActiveFlag(HitboxController hitbox, bool active)
    {
        if (hitbox == null) return;

        if (hitbox == weaponHitbox)
            mainHandHitboxActive = active;
        if (hitbox == offHandWeaponHitbox)
            offHandHitboxActive = active;

        hitboxActive = mainHandHitboxActive || offHandHitboxActive;
    }

    private void TryCompleteAnimationEventAttack()
    {
        if (!IsServer || !useAnimationEventsForHitboxes || SubState != OrcSubState.Attack)
            return;

        if (hitboxActive)
            return;

        CompleteAttack();
    }

    private void CompleteAttack()
    {
        if (!IsServer || SubState != OrcSubState.Attack)
            return;

        CancelInvoke(nameof(EnableSelectedWeaponHitbox));
        DisableWeaponHitbox();

        if (activeAttack != null)
            activeAttack.cooldownTimer = activeAttack.cooldown;

        float cooldownDuration = activeAttack != null ? activeAttack.cooldown : 0f;
        activeAttack = null;
        attackHitboxWindowStarted = false;

        if (blockDuringAttackCooldown && cooldownDuration > 0f)
            BeginBlock(cooldownDuration, true);
        else
            SetState(OrcState.Combat, OrcSubState.Approach);
    }

    private void InitializeWeaponHitbox(HitboxController hitbox)
    {
        if (hitbox == null) return;

        hitbox.Initialize(GetComponent<NetworkObject>(), weaponData, weaponData == null);
        hitbox.DisableHitbox();
    }

    private void SetWeaponHitboxContext()
    {
        int attackIndex = activeAttack != null ? activeAttack.attackIndex : 0;
        bool isHeavy = activeAttack != null && activeAttack.isHeavy;

        if (weaponHitbox != null)
            weaponHitbox.SetAttackContext(attackIndex, isHeavy);
        if (offHandWeaponHitbox != null)
            offHandWeaponHitbox.SetAttackContext(attackIndex, isHeavy);
    }

    private IEnumerable<HitboxController> GetActiveWeaponHitboxes()
    {
        OrcWeaponHitboxSelection selection = activeAttack != null
            ? activeAttack.hitboxSelection
            : OrcWeaponHitboxSelection.MainHand;

        if (selection == OrcWeaponHitboxSelection.MainHand || selection == OrcWeaponHitboxSelection.Both)
            yield return weaponHitbox;
        if (selection == OrcWeaponHitboxSelection.OffHand || selection == OrcWeaponHitboxSelection.Both)
            yield return offHandWeaponHitbox;
    }

    private bool ShouldCancelAttackForDistance()
    {
        if (currentTarget == null || activeAttack == null)
            return false;

        if (useAnimationEventsForHitboxes && attackHitboxWindowStarted && !allowAnimationEventAttackCancelAfterHitbox)
        {
            return false;
        }

        if (useAnimationEventsForHitboxes && !attackHitboxWindowStarted && !allowAnimationEventAttackCancelBeforeHitbox)
            return false;

        if (!useAnimationEventsForHitboxes && hitboxActive)
            return false;

        float cancelDistance = activeAttack.maxRange + attackCancelDistanceBuffer;
        float distance = Vector3.Distance(transform.position, currentTarget.position);
        return distance > cancelDistance;
    }

    private void CancelAttackAndApproach()
    {
        CancelInvoke(nameof(EnableSelectedWeaponHitbox));
        DisableWeaponHitbox();
        UnregisterAttacker();

        if (activeAttack != null)
            activeAttack.cooldownTimer = Mathf.Max(activeAttack.cooldownTimer, attackCancelReengageDelay);

        decisionTimer = Mathf.Max(decisionTimer, attackCancelReengageDelay);

        if (animator != null)
        {
            animator.ResetTrigger(attackHash);
            if (!string.IsNullOrEmpty(locomotionStatePath))
                animator.CrossFade(locomotionStatePath, attackCancelFade, 0);
        }

        activeAttack = null;
        attackHitboxWindowStarted = false;
        SetState(OrcState.Combat, OrcSubState.Approach);
    }

    public DamageDefenseResult EvaluateDefense(NetworkObject attacker, Vector3 hitPoint, float rawDamage)
    {
        if (attacker == null) return DamageDefenseResult.None;
        if (!IsAttackerInDefenseCone(attacker.transform)) return DamageDefenseResult.None;

        if (State == OrcState.Combat && SubState == OrcSubState.Parry && parryActiveTimer > 0f)
            return DamageDefenseResult.Parry;

        if (State == OrcState.Combat && SubState == OrcSubState.Block)
            return DamageDefenseResult.Block;

        return DamageDefenseResult.None;
    }

    public void OnServerDefenseResolved(
        DamageDefenseResult result,
        float rawDamage,
        float finalDamage,
        Vector3 hitPoint,
        NetworkObject attacker)
    {
        if (result == DamageDefenseResult.Parry)
        {
            parryCooldownTimer = parryCooldown;
            parryActiveTimer = 0f;
        }
        else if (result == DamageDefenseResult.Block)
        {
            blockCooldownTimer = blockCooldown;
        }
    }

    private bool IsTargetAttacking()
    {
        if (currentTarget == null) return false;

        var animancerDriver = currentTarget.GetComponentInChildren<RuleAnimancerDriver>();
        if (animancerDriver != null)
            return animancerDriver.IsLocked;

        var combat = currentTarget.GetComponentInParent<CombatController>();
        if (combat != null)
        {
            return combat.State != CombatController.CombatState.None &&
                   combat.State != CombatController.CombatState.Dead;
        }

        Vector3 toOrc = transform.position - currentTarget.position;
        toOrc.y = 0f;
        if (toOrc.sqrMagnitude > targetAttackReactDistance * targetAttackReactDistance)
            return false;

        float facingAngle = Vector3.Angle(currentTarget.forward, toOrc.normalized);
        return facingAngle < 45f;
    }

    private bool IsAttackerInDefenseCone(Transform attacker)
    {
        if (attacker == null) return false;

        Vector3 toAttacker = attacker.position - transform.position;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude < 0.001f) return true;

        float angle = Vector3.Angle(transform.forward, toAttacker.normalized);
        return angle <= frontalDefenseAngle * 0.5f;
    }

    private Transform FindNearestDetectedTarget()
    {
        if (detectionTimer > 0f) return currentTarget;
        detectionTimer = detectionInterval;

        Transform perceived = FindBestPerceivedTarget(out bool closeDetected);
        if (perceived == null)
        {
            targetAwareness = Mathf.Max(0f, targetAwareness - detectionInterval);
            if (targetAwareness <= 0f)
                awarenessTarget = null;

            return null;
        }

        if (closeDetected || detectionTime <= 0f)
        {
            awarenessTarget = perceived;
            targetAwareness = detectionTime;
            return perceived;
        }

        if (awarenessTarget != perceived)
        {
            awarenessTarget = perceived;
            targetAwareness = 0f;
        }

        targetAwareness += detectionInterval;
        return targetAwareness >= detectionTime ? perceived : null;
    }

    private Transform FindBestPerceivedTarget(out bool closeDetected)
    {
        closeDetected = false;
        int count = Physics.OverlapSphereNonAlloc(transform.position, viewDistance, _detectionBuffer, playerLayer);

        Transform best = null;
        int bestCount = int.MaxValue;
        float bestDist = float.MaxValue;
        bool bestCloseDetected = false;

        for (int i = 0; i < count; i++)
        {
            var col = _detectionBuffer[i];
            if (col == null) continue;

            var receiver = col.GetComponentInParent<DamageReceiver>();
            if (receiver == null) continue;

            Transform candidate = receiver.transform;
            if (!IsTargetAlive(candidate)) continue;
            if (!CanPerceiveTarget(candidate, out bool candidateCloseDetected)) continue;

            _attackerCounts.TryGetValue(candidate, out int attackerCount);
            float dist = Vector3.Distance(transform.position, candidate.position);
            if (attackerCount < bestCount || (attackerCount == bestCount && dist < bestDist))
            {
                best = candidate;
                bestCount = attackerCount;
                bestDist = dist;
                bestCloseDetected = candidateCloseDetected;
            }
        }

        closeDetected = bestCloseDetected;
        return best;
    }

    private Transform FindLessContestedVisibleTarget()
    {
        detectionTimer = detectionInterval;

        _attackerCounts.TryGetValue(currentTarget, out int currentCount);
        if (currentCount < maxAttackersPerTarget) return null;

        int count = Physics.OverlapSphereNonAlloc(transform.position, viewDistance, _detectionBuffer, playerLayer);

        Transform best = null;
        int bestCount = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var col = _detectionBuffer[i];
            if (col == null) continue;

            var receiver = col.GetComponentInParent<DamageReceiver>();
            if (receiver == null) continue;

            Transform candidate = receiver.transform;
            if (candidate == currentTarget || !IsTargetAlive(candidate)) continue;
            if (!CanPerceiveTarget(candidate, out _)) continue;

            _attackerCounts.TryGetValue(candidate, out int attackerCount);
            if (attackerCount < bestCount)
            {
                best = candidate;
                bestCount = attackerCount;
            }
        }

        return best;
    }

    private bool CanPerceiveTarget(Transform target, out bool closeDetected)
    {
        closeDetected = false;
        if (target == null) return false;

        Vector3 eye = GetEyePosition();
        Vector3 targetPoint = GetTargetAimPoint(target);
        Vector3 toTarget = targetPoint - eye;
        float sqrDistance = toTarget.sqrMagnitude;

        float closeRadius = Mathf.Max(0f, closeDetectionRadius);
        if (closeRadius > 0f && sqrDistance <= closeRadius * closeRadius)
        {
            closeDetected = HasLineOfSight(eye, targetPoint, target);
            return closeDetected;
        }

        float maxViewDistance = Mathf.Max(0f, viewDistance);
        if (maxViewDistance <= 0f || sqrDistance > maxViewDistance * maxViewDistance)
            return false;

        Vector3 flatToTarget = target.position - transform.position;
        flatToTarget.y = 0f;
        if (flatToTarget.sqrMagnitude < 0.001f)
            return HasLineOfSight(eye, targetPoint, target);

        float halfAngle = Mathf.Clamp(viewAngle, 1f, 360f) * 0.5f;
        float angle = Vector3.Angle(transform.forward, flatToTarget.normalized);
        if (angle > halfAngle)
            return false;

        return HasLineOfSight(eye, targetPoint, target);
    }

    private bool HasLineOfSight(Vector3 eye, Vector3 targetPoint, Transform target)
    {
        Vector3 toTarget = targetPoint - eye;
        float distance = toTarget.magnitude;
        if (distance <= 0.001f)
            return true;

        int hitCount = Physics.RaycastNonAlloc(
            eye,
            toTarget / distance,
            _lineOfSightHits,
            distance,
            visionObstacleMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount == 0)
            return true;

        RaycastHit nearestHit = default;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _lineOfSightHits[i];
            if (hit.collider == null) continue;
            if (hit.collider.GetComponentInParent<OrcAI>() == this) continue;

            if (hit.distance < nearestDistance)
            {
                nearestHit = hit;
                nearestDistance = hit.distance;
            }
        }

        if (nearestHit.collider == null)
            return true;

        return nearestHit.collider.GetComponentInParent<DamageReceiver>()?.transform == target;
    }

    private Vector3 GetEyePosition()
    {
        return transform.position + Vector3.up * eyeHeight;
    }

    private Vector3 GetTargetAimPoint(Transform target)
    {
        return target.position + Vector3.up * targetAimHeight;
    }

    private bool IsTargetAlive(Transform target)
    {
        if (target == null) return false;

        var receiver = target.GetComponentInParent<DamageReceiver>();
        if (receiver != null && receiver.IsDead) return false;

        var vitals = target.GetComponentInParent<VitalManager>();
        if (vitals == null) return true;
        if (vitals.IsDead) return false;

        var health = vitals.GetVital("health");
        return health == null || health.Current > 0f;
    }

    private void RegisterAttacker()
    {
        if (registeredAsAttacker || currentTarget == null) return;
        _attackerCounts.TryGetValue(currentTarget, out int count);
        _attackerCounts[currentTarget] = count + 1;
        registeredAsAttacker = true;
    }

    private void UnregisterAttacker()
    {
        if (!registeredAsAttacker || currentTarget == null) return;

        if (_attackerCounts.TryGetValue(currentTarget, out int count))
        {
            int next = count - 1;
            if (next <= 0) _attackerCounts.Remove(currentTarget);
            else _attackerCounts[currentTarget] = next;
        }

        registeredAsAttacker = false;
    }

    private bool IsCrowded()
    {
        if (currentTarget == null) return false;
        _attackerCounts.TryGetValue(currentTarget, out int count);
        return count >= maxAttackersPerTarget;
    }

    private void InitializePatrolRoute()
    {
        patrolDirection = 1;
        if (!HasPatrolPoints())
        {
            currentPatrolPointIndex = 0;
            return;
        }

        currentPatrolPointIndex = randomizePatrolStartPoint
            ? Random.Range(0, patrolPoints.Length)
            : 0;

        if (patrolMode == OrcPatrolMode.PingPong &&
            currentPatrolPointIndex >= patrolPoints.Length - 1)
        {
            patrolDirection = -1;
        }
    }

    private bool TryGetNextPatrolDestination(out Vector3 destination)
    {
        destination = Vector3.zero;

        if (patrolMode == OrcPatrolMode.Wander || !HasPatrolPoints())
        {
            destination = GetRandomWanderPoint();
            return destination != Vector3.zero;
        }

        for (int i = 0; i < patrolPoints.Length; i++)
        {
            currentPatrolPointIndex = Mathf.Clamp(currentPatrolPointIndex, 0, patrolPoints.Length - 1);
            Transform point = patrolPoints[currentPatrolPointIndex];
            AdvancePatrolPointIndex();
            if (point == null) continue;

            destination = GetPatrolPointPosition(point);
            return true;
        }

        return false;
    }

    private Vector3 GetPatrolPointPosition(Transform point)
    {
        if (NavMesh.SamplePosition(point.position, out NavMeshHit hit, arrivalThreshold * 2f, NavMesh.AllAreas))
            return hit.position;

        return point.position;
    }

    private void AdvancePatrolPointIndex()
    {
        if (!HasPatrolPoints())
            return;

        if (patrolMode == OrcPatrolMode.Loop)
        {
            currentPatrolPointIndex = (currentPatrolPointIndex + 1) % patrolPoints.Length;
            return;
        }

        if (patrolMode == OrcPatrolMode.PingPong)
        {
            if (patrolPoints.Length <= 1)
                return;

            if (currentPatrolPointIndex >= patrolPoints.Length - 1)
                patrolDirection = -1;
            else if (currentPatrolPointIndex <= 0)
                patrolDirection = 1;

            currentPatrolPointIndex = Mathf.Clamp(
                currentPatrolPointIndex + patrolDirection,
                0,
                patrolPoints.Length - 1);
        }
    }

    private bool HasPatrolPoints()
    {
        return patrolPoints != null &&
               patrolPoints.Length > 0 &&
               patrolMode != OrcPatrolMode.Wander;
    }

    private float GetPatrolWaitDuration()
    {
        if (HasPatrolPoints())
        {
            float min = Mathf.Min(patrolWaitTimeRange.x, patrolWaitTimeRange.y);
            float max = Mathf.Max(patrolWaitTimeRange.x, patrolWaitTimeRange.y);
            return Random.Range(Mathf.Max(0f, min), Mathf.Max(0f, max));
        }

        return Random.Range(idleMinTime, idleMaxTime);
    }

    private Vector3 GetRandomWanderPoint()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 randomDir = Random.insideUnitSphere * wanderRadius;
            randomDir.y = 0f;
            Vector3 candidate = homePosition + randomDir;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius * 0.5f, NavMesh.AllAreas))
                return hit.position;
        }

        return Vector3.zero;
    }

    private void RotateToward(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion targetRot = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
    }

    private void HandleDamageReceived(float damage, Vector3 hitPoint)
    {
        animalSoundPlayer?.PlayHitSound();
        if (!IsServer || State == OrcState.Dead) return;
        if (suppressDamageReceivedStateChangeTimer > 0f)
        {
            suppressDamageReceivedStateChangeTimer = 0f;
            return;
        }

        if (SubState == OrcSubState.Attack || SubState == OrcSubState.Block || SubState == OrcSubState.Parry) return;
        SetState(OrcState.Stagger, OrcSubState.Stagger);
    }

    private void HandleRangedDamageReceived(
        float damage,
        Vector3 hitPoint,
        Vector3 sourcePosition,
        NetworkObject attackerObject)
    {
        HandleRangedAlert(damage, hitPoint, sourcePosition, attackerObject, true);
    }

    private void HandleRangedAlert(
        float damage,
        Vector3 hitPoint,
        Vector3 sourcePosition,
        NetworkObject attackerObject,
        bool directHit)
    {
        if (!IsServer || State == OrcState.Dead)
            return;

        if (alertReturnHomeActive)
        {
            if (!directHit)
                return;

            Transform directAttacker = attackerObject != null ? attackerObject.transform : null;
            bool directAttackerVisible =
                directAttacker != null &&
                IsTargetAlive(directAttacker) &&
                CanPerceiveTarget(directAttacker, out _);
            bool directAttackerInsideInvestigationRadius =
                directAttacker != null &&
                Vector3.Distance(homePosition, directAttacker.position) <= investigateRadiusFromHome;
            bool directAttackerInsidePursueRadius =
                directAttacker != null &&
                Vector3.Distance(homePosition, directAttacker.position) <= pursueRadiusFromHome;

            if (directAttackerVisible &&
                directAttackerInsideInvestigationRadius &&
                directAttackerInsidePursueRadius)
            {
                directRangedHitPriorityTimer = directRangedHitPriorityDuration;
                suppressDamageReceivedStateChangeTimer = 0.25f;
                BeginPursueRangedAttacker(directAttacker);
            }

            return;
        }

        if (!directHit && directRangedHitPriorityTimer > 0f)
            return;

        if (directHit)
            directRangedHitPriorityTimer = directRangedHitPriorityDuration;

        suppressDamageReceivedStateChangeTimer = 0.25f;

        Transform attacker = attackerObject != null ? attackerObject.transform : null;
        bool attackerVisible =
            attacker != null &&
            IsTargetAlive(attacker) &&
            CanPerceiveTarget(attacker, out _);
        bool sourceOutsideViewDistance = IsRangedSourceOutsideViewDistance(sourcePosition);

        if (!attackerVisible && sourceOutsideViewDistance && RegisterOutOfViewRangedAlert())
        {
            BeginAlertReturnHome(sourcePosition);
            return;
        }

        bool sourceInsideInvestigateRadius =
            Vector3.Distance(homePosition, sourcePosition) <= investigateRadiusFromHome;
        bool attackerInsidePursueRadius =
            attacker != null &&
            Vector3.Distance(homePosition, attacker.position) <= pursueRadiusFromHome;

        if (sourceInsideInvestigateRadius &&
            attackerVisible &&
            attackerInsidePursueRadius)
        {
            BeginPursueRangedAttacker(attacker);
            return;
        }

        if (sourceInsideInvestigateRadius &&
            attackerVisible)
        {
            BeginAlertReturnHome(attacker.position);
            return;
        }

        if (sourceInsideInvestigateRadius)
        {
            BeginInvestigateRangedSource(sourcePosition);
            return;
        }

        if (investigateRadiusFromHome > 0f)
        {
            BeginInvestigateRangedSource(sourcePosition);
            return;
        }

        BeginRangedHitGuard(sourcePosition);
    }

    private void HandleNearbyRangedImpact(
        Vector3 impactPosition,
        Vector3 sourcePosition,
        NetworkObject attackerObject)
    {
        if (!IsServer || State == OrcState.Dead || !reactToNearbyRangedImpacts)
            return;

        float radius = Mathf.Max(0f, closeDetectionRadius);
        if (radius <= 0f)
            return;

        Vector3 toImpact = impactPosition - transform.position;
        if (toImpact.sqrMagnitude > radius * radius)
            return;

        HandleRangedAlert(0f, impactPosition, sourcePosition, attackerObject, false);
    }

    private void HandleDamageBlocked(float damage, Vector3 hitPoint)
    {
        if (!IsServer) return;
        animalSoundPlayer?.PlayHitSound();
    }

    private void HandleDamageParried(float damage, Vector3 hitPoint)
    {
        if (!IsServer) return;
        animalSoundPlayer?.PlayAttackSound();
    }

    private void OnDeath()
    {
        SetState(OrcState.Dead, OrcSubState.Dead);
    }

    [ClientRpc]
    private void DisableCollidersClientRpc()
    {
        foreach (var col in collidersToDisableOnDeath)
        {
            if (col != null)
                col.enabled = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        Vector3 center = Application.isPlaying ? homePosition : transform.position;

        Gizmos.color = viewDistanceGizmoColor;
        Gizmos.DrawWireSphere(transform.position, viewDistance);

        Gizmos.color = closeDetectionGizmoColor;
        Gizmos.DrawWireSphere(transform.position, closeDetectionRadius);

        Vector3 left = Quaternion.AngleAxis(-viewAngle * 0.5f, Vector3.up) * transform.forward;
        Vector3 right = Quaternion.AngleAxis(viewAngle * 0.5f, Vector3.up) * transform.forward;
        Gizmos.DrawLine(GetEyePosition(), GetEyePosition() + left * viewDistance);
        Gizmos.DrawLine(GetEyePosition(), GetEyePosition() + right * viewDistance);

        Gizmos.color = pursueRadiusGizmoColor;
        Gizmos.DrawWireSphere(center, pursueRadiusFromHome);

        Gizmos.color = investigateRadiusGizmoColor;
        Gizmos.DrawWireSphere(center, investigateRadiusFromHome);

        Gizmos.color = wanderRadiusGizmoColor;
        Gizmos.DrawWireSphere(center, wanderRadius);

        Gizmos.color = preferredCombatDistanceGizmoColor;
        Gizmos.DrawWireSphere(transform.position, preferredCombatDistance);

        DrawPatrolRouteGizmos();
    }

    private void DrawPatrolRouteGizmos()
    {
        if (patrolPoints == null || patrolPoints.Length == 0 || patrolMode == OrcPatrolMode.Wander)
            return;

        Gizmos.color = patrolRouteGizmoColor;
        Transform firstPoint = null;
        Transform previousPoint = null;

        for (int i = 0; i < patrolPoints.Length; i++)
        {
            Transform point = patrolPoints[i];
            if (point == null) continue;

            Gizmos.DrawSphere(point.position, 0.2f);
            if (firstPoint == null)
                firstPoint = point;

            if (previousPoint != null)
                Gizmos.DrawLine(previousPoint.position, point.position);

            previousPoint = point;
        }

        if (patrolMode == OrcPatrolMode.Loop &&
            firstPoint != null &&
            previousPoint != null &&
            firstPoint != previousPoint)
        {
            Gizmos.DrawLine(previousPoint.position, firstPoint.position);
        }
    }
}
