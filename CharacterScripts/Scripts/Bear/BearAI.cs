using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Simple bear AI with leash-based behavior.
/// Server-authoritative: AI logic only runs on server.
/// Animations synced via NetworkAnimator.
/// 
/// States: Idle → Wander → Chase → Attack → Return → Dead
/// 
/// Setup:
///   - NavMeshAgent on bear (updatePosition=false, updateRotation=false for root motion)
///   - Animator with parameters: Speed (float), Attack (trigger), Dead (bool)
///   - VitalManager with Health vital
///   - DamageReceiver for taking hits
///   - HitboxController on bite/paw child for dealing damage
///   - CapsuleCollider for hit detection (being hit by players)
///   - NetworkAnimator for syncing animations
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkObject))]
public class BearAI : NetworkBehaviour
{
    public enum BearState
    {
        Idle,
        Wander,
        Chase,
        Attack,
        Return,
        Dead
    }

    [Header("Detection")]
    [Tooltip("Radius to detect players and start chasing")]
    [SerializeField] private float detectionRadius = 15f;
    [Tooltip("If player goes beyond this, bear stops chasing and returns home")]
    [SerializeField] private float leashRadius = 25f;
    [Tooltip("Distance to start attacking")]
    [SerializeField] private float attackRange = 3f;
    [SerializeField] private LayerMask playerLayer;

    [Header("Wander")]
    [Tooltip("How far from home the bear wanders")]
    [SerializeField] private float wanderRadius = 10f;
    [Tooltip("Time spent idle before wandering")]
    [SerializeField] private float idleMinTime = 2f;
    [SerializeField] private float idleMaxTime = 5f;
    [Tooltip("How close to wander target to consider arrived")]
    [SerializeField] private float arrivalThreshold = 1.5f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float rotationSpeed = 5f;

    [Header("Attack")]
    [SerializeField] private float attackDuration = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float attackDamage = 15f;

    [Header("Hitbox (for dealing damage)")]
    [SerializeField] private HitboxController biteHitbox;
    [Tooltip("Time after attack starts to enable hitbox")]
    [SerializeField] private float hitboxEnableDelay = 0.3f;
    [Tooltip("How long hitbox stays active")]
    [SerializeField] private float hitboxActiveDuration = 0.3f;

    [Header("Surround")]
    [Tooltip("Max enemies that can orbit a single target")]
    [SerializeField] private int maxAttackersPerTarget = 4;
    [Tooltip("Distance from target centre to orbit slot — tweak per enemy size")]
    [SerializeField] private float orbitRadius = 2.5f;

    [Header("Death")]
    [Tooltip("Colliders to disable when this enemy dies (drag the CapsuleCollider etc. here)")]
    [SerializeField] private Collider[] collidersToDisableOnDeath;

    [Header("References")]
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private AnimalSoundPlayer animalSoundPlayer;

    [Header("Debug")]
    [SerializeField] private bool showGizmos = true;

    // Animator hashes
    private int speedHash;
    private int attackHash;
    private int deadHash;

    // State
    public BearState CurrentState { get; private set; } = BearState.Idle;
    private Vector3 homePosition;
    private float stateTimer;
    private float attackCooldownTimer;
    private float hitboxTimer;
    private bool hitboxActive;
    private Transform currentTarget;

    // Detection cache
    private Collider[] _detectionBuffer = new Collider[10];
    private float _detectionInterval = 0.25f;
    private float _detectionTimer;

    // Surround
    private bool _slotRegistered;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        homePosition = transform.position;

        // Auto-wire
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (vitalManager == null) vitalManager = GetComponent<VitalManager>();
        if (biteHitbox != null)
            biteHitbox.Initialize(GetComponent<NetworkObject>(), null);
        if (animalSoundPlayer == null) animalSoundPlayer = GetComponent<AnimalSoundPlayer>();

        // Hit sound
        var damageReceiver = GetComponent<DamageReceiver>();
        if (damageReceiver != null)
            damageReceiver.OnDamageReceived += (damage, hitPoint) => animalSoundPlayer?.PlayHitSound();

        // Cache hashes
        speedHash = Animator.StringToHash("Speed");
        attackHash = Animator.StringToHash("Attack");
        deadHash = Animator.StringToHash("Dead");

        // Root motion setup — NavMeshAgent provides pathfinding, root motion drives position
        if (agent != null)
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
        }

        // Listen for death
        if (vitalManager != null)
            vitalManager.OnDeath += OnDeath;
    }

    public override void OnNetworkDespawn()
    {
        if (vitalManager != null)
            vitalManager.OnDeath -= OnDeath;

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        // AI logic only runs on server
        if (!IsServer) return;
        if (CurrentState == BearState.Dead) return;

        // Timers
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;

        _detectionTimer -= Time.deltaTime;

        // Hitbox timing during attack
        if (hitboxActive)
        {
            hitboxTimer -= Time.deltaTime;
            if (hitboxTimer <= 0f)
            {
                DisableBiteHitbox();
            }
        }

        // State machine
        switch (CurrentState)
        {
            case BearState.Idle:
                UpdateIdle();
                break;
            case BearState.Wander:
                UpdateWander();
                break;
            case BearState.Chase:
                UpdateChase();
                break;
            case BearState.Attack:
                UpdateAttack();
                break;
            case BearState.Return:
                UpdateReturn();
                break;
        }

        // Sync animator speed
        // Replace the speed sync line at the end of Update() with:
        float animSpeed = 0f;
        switch (CurrentState)
        {
            case BearState.Idle:
                animSpeed = 0f;
                break;
            case BearState.Wander:
            case BearState.Return:
                animSpeed = 0.5f; // Walk
                break;
            case BearState.Chase:
                animSpeed = 1f; // Run
                break;
            case BearState.Attack:
                animSpeed = 0f;
                break;
        }
        animator.SetFloat(speedHash, animSpeed, 0.15f, Time.deltaTime); // Smooth transition
    }

    /// <summary>
    /// Root motion: apply animation movement to NavMeshAgent position.
    /// </summary>
    private void OnAnimatorMove()
    {
        if (animator == null || agent == null) return;
        if (!IsServer) return;

        // Apply root motion delta to agent
        Vector3 rootPosition = animator.rootPosition;
        rootPosition.y = agent.nextPosition.y; // Keep NavMesh Y to avoid floating
        agent.nextPosition = rootPosition;
        transform.position = rootPosition;
    }

    // ─── State Updates ───

    private void UpdateIdle()
    {
        stateTimer -= Time.deltaTime;

        // Check for player
        Transform target = FindNearestPlayer();
        if (target != null)
        {
            currentTarget = target;
            SetState(BearState.Chase);
            return;
        }

        // Timer expired → wander
        if (stateTimer <= 0f)
        {
            Vector3 wanderPoint = GetRandomWanderPoint();
            if (wanderPoint != Vector3.zero)
            {
                agent.SetDestination(wanderPoint);
                agent.speed = walkSpeed;
                SetState(BearState.Wander);
            }
            else
            {
                // Couldn't find valid point, reset idle timer
                stateTimer = Random.Range(idleMinTime, idleMaxTime);
            }
        }
    }

    private void UpdateWander()
    {
        // Check for player
        Transform target = FindNearestPlayer();
        if (target != null)
        {
            currentTarget = target;
            SetState(BearState.Chase);
            return;
        }

        // Arrived at wander point
        if (!agent.pathPending && agent.remainingDistance <= arrivalThreshold)
        {
            SetState(BearState.Idle);
        }

        // Face movement direction
        RotateToward(agent.desiredVelocity);
    }

    private void UpdateChase()
    {
        // Target lost or dead
        if (currentTarget == null || !IsTargetAlive(currentTarget))
        {
            ReleaseSlot();
            currentTarget = null;
            SetState(BearState.Return);
            return;
        }

        float distToTarget = Vector3.Distance(transform.position, currentTarget.position);
        float distToHome   = Vector3.Distance(transform.position, homePosition);

        // Leash check — too far from home
        if (distToHome > leashRadius)
        {
            ReleaseSlot();
            currentTarget = null;
            SetState(BearState.Return);
            return;
        }

        // Ensure we have a slot; try to claim one if not yet assigned
        if (!_slotRegistered)
        {
            _slotRegistered = EnemySurroundCoordinator.Register(
                currentTarget, transform, maxAttackersPerTarget);
            // If full, stay put and wait — don't advance toward the target
            if (!_slotRegistered)
            {
                agent.ResetPath();
                return;
            }
        }

        // Path to our assigned orbit slot instead of the target's feet
        Vector3 slotPos = EnemySurroundCoordinator.GetSlotPosition(
            currentTarget, transform, orbitRadius, maxAttackersPerTarget);

        float distToSlot = Vector3.Distance(transform.position, slotPos);

        // In attack range of slot (close enough to the target)
        if (distToTarget <= attackRange && attackCooldownTimer <= 0f)
        {
            SetState(BearState.Attack);
            return;
        }

        agent.SetDestination(slotPos);
        agent.speed = runSpeed;
        RotateToward(currentTarget.position - transform.position);
    }

    private void UpdateAttack()
    {
        stateTimer -= Time.deltaTime;

        // Face target during attack
        if (currentTarget != null)
        {
            Vector3 dir = (currentTarget.position - transform.position);
            dir.y = 0f;
            RotateToward(dir);
        }

        // Attack finished
        if (stateTimer <= 0f)
        {
            DisableBiteHitbox();
            attackCooldownTimer = attackCooldown;

            // Check if target still in range
            if (currentTarget != null && IsTargetAlive(currentTarget))
            {
                float dist = Vector3.Distance(transform.position, currentTarget.position);
                if (dist <= attackRange)
                    SetState(BearState.Attack); // Attack again
                else if (dist <= leashRadius)
                    SetState(BearState.Chase);
                else
                {
                    ReleaseSlot();
                    SetState(BearState.Return);
                }
            }
            else
            {
                ReleaseSlot();
                currentTarget = null;
                SetState(BearState.Return);
            }
        }
    }

    private void UpdateReturn()
    {
        // Check for player on the way back
        Transform target = FindNearestPlayer();
        if (target != null)
        {
            float distToHome = Vector3.Distance(transform.position, homePosition);
            if (distToHome < leashRadius)
            {
                currentTarget = target;
                SetState(BearState.Chase);
                return;
            }
        }

        agent.SetDestination(homePosition);
        agent.speed = walkSpeed;
        RotateToward(agent.desiredVelocity);

        // Arrived home
        if (!agent.pathPending && agent.remainingDistance <= arrivalThreshold)
        {
            SetState(BearState.Idle);
        }
    }

    // ─── State Transitions ───

    private void SetState(BearState newState)
    {
        if (CurrentState == newState && newState != BearState.Attack) return;

        CurrentState = newState;

        switch (newState)
        {
            case BearState.Idle:
                stateTimer = Random.Range(idleMinTime, idleMaxTime);
                agent.ResetPath();
                break;

            case BearState.Wander:
                break;

            case BearState.Chase:
                break;

            case BearState.Attack:
                stateTimer = attackDuration;
                agent.ResetPath();
                animator.SetTrigger(attackHash);
                animalSoundPlayer?.PlayAttackSound();
                // Schedule hitbox enable
                Invoke(nameof(EnableBiteHitbox), hitboxEnableDelay);
                break;

            case BearState.Return:
                agent.speed = walkSpeed;
                break;

            case BearState.Dead:
                ReleaseSlot();
                agent.ResetPath();
                agent.enabled = false;
                animator.SetBool(deadHash, true);
                DisableBiteHitbox();
                animalSoundPlayer?.PlayDeathSound();
                DisableCollidersClientRpc();
                break;
        }
    }

    // ─── Colliders ───

    [ClientRpc]
    private void DisableCollidersClientRpc()
    {
        foreach (var col in collidersToDisableOnDeath)
            if (col != null) col.enabled = false;
    }

    // ─── Slot ───

    private void ReleaseSlot()
    {
        if (_slotRegistered && currentTarget != null)
        {
            EnemySurroundCoordinator.Unregister(currentTarget, transform);
            _slotRegistered = false;
        }
    }

    // ─── Hitbox ───

    private void EnableBiteHitbox()
    {
        if (biteHitbox != null)
        {
            biteHitbox.EnableHitbox();
            hitboxActive = true;
            hitboxTimer = hitboxActiveDuration;
        }
    }

    private void DisableBiteHitbox()
    {
        if (biteHitbox != null)
            biteHitbox.DisableHitbox();
        hitboxActive = false;
    }

    // ─── Detection ───

    /// <summary>
    /// On first detection, returns the least-contested living player within range.
    /// On subsequent calls (already chasing), returns nearest living player as before.
    /// </summary>
    private Transform FindNearestPlayer()
    {
        if (_detectionTimer > 0f) return currentTarget;
        _detectionTimer = _detectionInterval;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, detectionRadius, _detectionBuffer, playerLayer);

        // Collect all valid candidates
        var candidates = new List<Transform>();

        for (int i = 0; i < count; i++)
        {
            var col = _detectionBuffer[i];
            if (col == null) continue;

            var receiver = col.GetComponentInParent<DamageReceiver>();
            if (receiver == null) continue;

            Transform targetRoot = receiver.transform;

            var vitals = col.GetComponentInParent<VitalManager>();
            if (vitals != null)
            {
                var health = vitals.GetVital("health");
                if (health != null && health.Current <= 0f) continue;
            }

            if (!candidates.Contains(targetRoot))
                candidates.Add(targetRoot);
        }

        if (candidates.Count == 0) return null;

        // Already chasing someone — just keep nearest alive candidate
        if (currentTarget != null)
        {
            Transform nearest    = null;
            float     nearestDist = float.MaxValue;
            foreach (var t in candidates)
            {
                float d = Vector3.Distance(transform.position, t.position);
                if (d < nearestDist) { nearestDist = d; nearest = t; }
            }
            return nearest;
        }

        // First pick — use coordinator to choose least-contested target
        Transform picked = EnemySurroundCoordinator.FindLeastContestedTarget(
            candidates, maxAttackersPerTarget);

        // Fallback: all targets full — pick nearest anyway (will wait for slot in UpdateChase)
        if (picked == null)
        {
            float nearestDist = float.MaxValue;
            foreach (var t in candidates)
            {
                float d = Vector3.Distance(transform.position, t.position);
                if (d < nearestDist) { nearestDist = d; picked = t; }
            }
        }

        return picked;
    }

    private bool IsTargetAlive(Transform target)
    {
        if (target == null) return false;

        var vitals = target.GetComponentInParent<VitalManager>();
        if (vitals == null) return true; // No vitals = assume alive

        var health = vitals.GetVital("health");
        return health == null || health.Current > 0f;
    }

    // ─── Helpers ───

    private void RotateToward(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion targetRot = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(
            transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
    }

    private Vector3 GetRandomWanderPoint()
    {
        for (int i = 0; i < 10; i++) // Try 10 times to find valid point
        {
            Vector3 randomDir = Random.insideUnitSphere * wanderRadius;
            randomDir.y = 0f;
            Vector3 candidate = homePosition + randomDir;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius * 0.5f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        return Vector3.zero; // Failed to find valid point
    }

    // ─── Events ───

    private void OnDeath()
    {
        SetState(BearState.Dead);
    }

    // ─── Gizmos ───

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        Vector3 center = Application.isPlaying ? homePosition : transform.position;

        // Detection radius (yellow)
        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(center, detectionRadius);

        // Leash radius (red)
        Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
        Gizmos.DrawWireSphere(center, leashRadius);

        // Wander radius (green)
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(center, wanderRadius);

        // Attack range (orange)
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Home position
        if (Application.isPlaying)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(homePosition, 0.3f);
        }
    }
}