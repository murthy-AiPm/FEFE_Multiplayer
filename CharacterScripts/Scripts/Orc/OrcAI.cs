using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[System.Serializable]
public class OrcAttackOption
{
    public int attackIndex;
    public float minRange = 0f;
    public float maxRange = 2.5f;
    public float weight = 1f;
    public float duration = 1.1f;
    public float cooldown = 1.2f;
    public float hitboxEnableDelay = 0.25f;
    public float hitboxActiveTime = 0.3f;
    public bool isHeavy;

    [System.NonSerialized] public float cooldownTimer;
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
        Recover,
        Stagger,
        Dead
    }

    [Header("Detection")]
    [SerializeField] private float detectionRadius = 15f;
    [SerializeField] private float leashRadius = 25f;
    [SerializeField] private LayerMask playerLayer;

    [Header("Patrol")]
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private float idleMinTime = 2f;
    [SerializeField] private float idleMaxTime = 5f;
    [SerializeField] private float arrivalThreshold = 1.5f;

    [Header("Movement")]
    [SerializeField] private bool useRootMotion = true;
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
    [SerializeField] private HitboxController weaponHitbox;
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

    private static readonly Dictionary<Transform, int> _attackerCounts = new Dictionary<Transform, int>();

    private readonly Collider[] _detectionBuffer = new Collider[16];
    private readonly Collider[] _separationBuffer = new Collider[10];

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
    private bool hitboxActive;
    private bool registeredAsAttacker;
    private bool stateInitialized;
    private Vector3 repositionTarget;

    public OrcState State { get; private set; } = OrcState.Patrol;
    public OrcSubState SubState { get; private set; } = OrcSubState.Idle;

    public bool IsDefenseInvincible => false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        homePosition = transform.position;

        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (vitalManager == null) vitalManager = GetComponent<VitalManager>();
        if (damageReceiver == null) damageReceiver = GetComponent<DamageReceiver>();
        if (animalSoundPlayer == null) animalSoundPlayer = GetComponent<AnimalSoundPlayer>();

        if (weaponHitbox != null)
        {
            weaponHitbox.Initialize(GetComponent<NetworkObject>(), weaponData, weaponData == null);
            weaponHitbox.DisableHitbox();
        }

        if (damageReceiver != null)
        {
            damageReceiver.OnDamageReceived += HandleDamageReceived;
            damageReceiver.OnDamageBlocked += HandleDamageBlocked;
            damageReceiver.OnDamageParried += HandleDamageParried;
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
            agent.updatePosition = !useRootMotion;
            agent.updateRotation = false;
            agent.avoidancePriority = Random.Range(20, 80);
            agent.speed = walkSpeed;
        }

        if (vitalManager != null)
            vitalManager.OnDeath += OnDeath;

        SetState(OrcState.Patrol, OrcSubState.Idle);
    }

    public override void OnNetworkDespawn()
    {
        UnregisterAttacker();

        if (vitalManager != null)
            vitalManager.OnDeath -= OnDeath;

        if (damageReceiver != null)
        {
            damageReceiver.OnDamageReceived -= HandleDamageReceived;
            damageReceiver.OnDamageBlocked -= HandleDamageBlocked;
            damageReceiver.OnDamageParried -= HandleDamageParried;
        }

        base.OnNetworkDespawn();
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
        if (!IsServer || !useRootMotion || animator == null || agent == null) return;

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

        if (attacks != null)
        {
            for (int i = 0; i < attacks.Length; i++)
            {
                if (attacks[i] != null && attacks[i].cooldownTimer > 0f)
                    attacks[i].cooldownTimer -= Time.deltaTime;
            }
        }

        if (hitboxActive)
        {
            hitboxTimer -= Time.deltaTime;
            if (hitboxTimer <= 0f)
                DisableWeaponHitbox();
        }
    }

    private void UpdatePatrol()
    {
        bool canAcquireTarget = SubState != OrcSubState.Return ||
                                Vector3.Distance(transform.position, homePosition) < leashRadius;
        Transform target = canAcquireTarget ? FindNearestTarget() : null;
        if (target != null)
        {
            currentTarget = target;
            SetState(OrcState.Combat, OrcSubState.Approach);
            return;
        }

        switch (SubState)
        {
            case OrcSubState.Idle:
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    Vector3 wanderPoint = GetRandomWanderPoint();
                    if (wanderPoint != Vector3.zero)
                    {
                        agent.SetDestination(wanderPoint);
                        SetState(OrcState.Patrol, OrcSubState.Wander);
                    }
                    else
                    {
                        stateTimer = Random.Range(idleMinTime, idleMaxTime);
                    }
                }
                break;

            case OrcSubState.Wander:
                if (!agent.pathPending && agent.remainingDistance <= arrivalThreshold)
                    SetState(OrcState.Patrol, OrcSubState.Idle);
                RotateToward(agent.desiredVelocity);
                break;

            case OrcSubState.Return:
                agent.SetDestination(homePosition);
                RotateToward(agent.desiredVelocity);
                if (!agent.pathPending && agent.remainingDistance <= arrivalThreshold)
                    SetState(OrcState.Patrol, OrcSubState.Idle);
                break;
        }
    }

    private void UpdateCombat()
    {
        if (!ValidateCombatTarget())
            return;

        if (detectionTimer <= 0f)
        {
            Transform betterTarget = FindLessContestedTarget();
            if (betterTarget != null && betterTarget != currentTarget)
            {
                UnregisterAttacker();
                currentTarget = betterTarget;
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
            case OrcSubState.Recover:
                UpdateRecover();
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
            RotateToward(currentTarget.position - transform.position);

        if (stateTimer <= 0f)
        {
            DisableWeaponHitbox();
            if (activeAttack != null)
                activeAttack.cooldownTimer = activeAttack.cooldown;
            SetState(OrcState.Combat, OrcSubState.Recover);
        }
    }

    private void UpdateBlock()
    {
        stateTimer -= Time.deltaTime;
        if (currentTarget != null)
            RotateToward(currentTarget.position - transform.position);

        if (stateTimer <= 0f)
        {
            blockCooldownTimer = blockCooldown;
            SetState(OrcState.Combat, OrcSubState.Recover);
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
            SetState(OrcState.Combat, OrcSubState.Recover);
        }
    }

    private void UpdateRecover()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
            SetState(OrcState.Combat, OrcSubState.Approach);
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
                SetState(OrcState.Combat, OrcSubState.Block);
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
        if (stateInitialized && State == newState && SubState == newSubState && newSubState != OrcSubState.Attack)
            return;

        if (SubState == OrcSubState.Block && animator != null)
            animator.SetBool(blockHash, false);

        State = newState;
        SubState = newSubState;
        stateInitialized = true;

        if (animator != null)
            animator.SetInteger(combatStateHash, (int)newSubState);

        switch (newSubState)
        {
            case OrcSubState.Idle:
                stateTimer = Random.Range(idleMinTime, idleMaxTime);
                agent.ResetPath();
                break;

            case OrcSubState.Wander:
                break;

            case OrcSubState.Return:
                UnregisterAttacker();
                currentTarget = null;
                break;

            case OrcSubState.Approach:
                break;

            case OrcSubState.Reposition:
                stateTimer = repositionDuration;
                break;

            case OrcSubState.Attack:
                stateTimer = activeAttack != null ? activeAttack.duration : 1f;
                agent.ResetPath();
                if (animator != null)
                {
                    animator.SetInteger(attackIndexHash, activeAttack != null ? activeAttack.attackIndex : 0);
                    animator.SetTrigger(attackHash);
                }
                weaponHitbox?.SetAttackContext(activeAttack != null ? activeAttack.attackIndex : 0, activeAttack != null && activeAttack.isHeavy);
                animalSoundPlayer?.PlayAttackSound();
                Invoke(nameof(EnableWeaponHitbox), activeAttack != null ? activeAttack.hitboxEnableDelay : 0.25f);
                break;

            case OrcSubState.Block:
                stateTimer = blockDuration;
                agent.ResetPath();
                if (animator != null)
                    animator.SetBool(blockHash, true);
                break;

            case OrcSubState.Parry:
                stateTimer = parryDuration;
                parryActiveTimer = parryActiveWindow;
                agent.ResetPath();
                if (animator != null)
                    animator.SetTrigger(parryHash);
                break;

            case OrcSubState.Recover:
                stateTimer = 0.2f;
                agent.ResetPath();
                break;

            case OrcSubState.Stagger:
                stateTimer = 0.45f;
                agent.ResetPath();
                DisableWeaponHitbox();
                if (animator != null)
                    animator.SetTrigger(staggerHash);
                break;

            case OrcSubState.Dead:
                UnregisterAttacker();
                currentTarget = null;
                agent.ResetPath();
                agent.enabled = false;
                DisableWeaponHitbox();
                if (animator != null)
                    animator.SetBool(deadHash, true);
                animalSoundPlayer?.PlayDeathSound();
                DisableCollidersClientRpc();
                break;
        }
    }

    private bool ValidateCombatTarget()
    {
        if (currentTarget == null || !IsTargetAlive(currentTarget))
        {
            UnregisterAttacker();
            SetState(OrcState.Patrol, OrcSubState.Return);
            return false;
        }

        float distToHome = Vector3.Distance(transform.position, homePosition);
        if (distToHome > leashRadius)
        {
            UnregisterAttacker();
            SetState(OrcState.Patrol, OrcSubState.Return);
            return false;
        }

        return true;
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
                animSpeed = 0.5f;
                if (!useRootMotion && agent != null) agent.speed = walkSpeed;
                break;
            case OrcSubState.Approach:
                animSpeed = 1f;
                if (!useRootMotion && agent != null) agent.speed = runSpeed;
                break;
        }

        animator.SetFloat(speedHash, animSpeed, 0.15f, Time.deltaTime);
    }

    private void EnableWeaponHitbox()
    {
        if (weaponHitbox == null || SubState != OrcSubState.Attack) return;

        weaponHitbox.EnableHitbox();
        hitboxActive = true;
        hitboxTimer = activeAttack != null ? activeAttack.hitboxActiveTime : 0.3f;
    }

    private void DisableWeaponHitbox()
    {
        if (weaponHitbox != null)
            weaponHitbox.DisableHitbox();
        hitboxActive = false;
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

    private Transform FindNearestTarget()
    {
        if (detectionTimer > 0f) return currentTarget;
        detectionTimer = 0.25f;

        int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, _detectionBuffer, playerLayer);

        Transform best = null;
        int bestCount = int.MaxValue;
        float bestDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var col = _detectionBuffer[i];
            if (col == null) continue;

            var receiver = col.GetComponentInParent<DamageReceiver>();
            if (receiver == null) continue;

            Transform candidate = receiver.transform;
            if (!IsTargetAlive(candidate)) continue;

            _attackerCounts.TryGetValue(candidate, out int attackerCount);
            float dist = Vector3.Distance(transform.position, candidate.position);
            if (attackerCount < bestCount || (attackerCount == bestCount && dist < bestDist))
            {
                best = candidate;
                bestCount = attackerCount;
                bestDist = dist;
            }
        }

        return best;
    }

    private Transform FindLessContestedTarget()
    {
        detectionTimer = 0.25f;

        _attackerCounts.TryGetValue(currentTarget, out int currentCount);
        if (currentCount < maxAttackersPerTarget) return null;

        int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, _detectionBuffer, playerLayer);

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

            _attackerCounts.TryGetValue(candidate, out int attackerCount);
            if (attackerCount < bestCount)
            {
                best = candidate;
                bestCount = attackerCount;
            }
        }

        return best;
    }

    private bool IsTargetAlive(Transform target)
    {
        if (target == null) return false;

        var vitals = target.GetComponentInParent<VitalManager>();
        if (vitals == null) return true;

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
        if (SubState == OrcSubState.Attack || SubState == OrcSubState.Block || SubState == OrcSubState.Parry) return;
        SetState(OrcState.Stagger, OrcSubState.Stagger);
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

        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(center, detectionRadius);

        Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
        Gizmos.DrawWireSphere(center, leashRadius);

        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(center, wanderRadius);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, preferredCombatDistance);
    }
}
