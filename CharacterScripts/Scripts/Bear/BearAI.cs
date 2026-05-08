using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>//
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

    [Header("Crowd Control")]
    [Tooltip("Max enemies allowed in melee range of a single target before others wait")]
    [SerializeField] private int maxAttackersPerTarget = 4;
    [Tooltip("Radius around this zombie checked for other zombies when deciding to attack")]
    [SerializeField] private float crowdCheckRadius = 1.5f;

    [Header("Separation")]
    [Tooltip("Radius to scan for neighbouring zombies to push away from")]
    [SerializeField] private float separationRadius = 1.2f;
    [Tooltip("How strongly this zombie pushes away from neighbours — tune alongside NavMeshAgent radius")]
    [SerializeField] private float separationStrength = 2f;

    [Header("Gravity")]
    [Tooltip("Downward acceleration when airborne — match to your world scale")]
    [SerializeField] private float gravityStrength = 20f;
    [Tooltip("Ray cast distance downward to detect ground")]
    [SerializeField] private float groundCheckDistance = 0.4f;
    [Tooltip("Layer mask for ground — should include terrain and any solid floor layers")]
    [SerializeField] private LayerMask groundLayer;

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

    // Detection / separation cache
    private Collider[] _detectionBuffer  = new Collider[10];
    private Collider[] _separationBuffer = new Collider[8];
    private float _detectionInterval = 0.25f;
    private float _detectionTimer;

    // Gravity
    private float _verticalVelocity;

    // Crowd control — server-only static tracker (target → attacker count)
    private static readonly Dictionary<Transform, int> _attackerCounts =
        new Dictionary<Transform, int>();
    private bool _registeredAsAttacker;

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
            // Randomise avoidance priority so RVO works properly across many agents
            agent.avoidancePriority = Random.Range(20, 80);
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
    /// Root motion: apply animation movement to NavMeshAgent position,
    /// then apply separation nudge and manual gravity.
    /// </summary>
    private void OnAnimatorMove()
    {
        if (animator == null || agent == null) return;
        if (!IsServer) return;

        // Apply root motion delta to agent
        // Set agent speed very high so it never clamps root motion
        agent.speed = 100f;
        Vector3 rootPosition = animator.rootPosition;

        // ── Gravity ──────────────────────────────────────────────
        // Cast downward from hip height (0.5 up) to avoid starting inside ground
        Vector3 rayOrigin = rootPosition + Vector3.up * 0.5f;
        bool grounded = Physics.Raycast(
            rayOrigin, Vector3.down,
            out RaycastHit groundHit,
            groundCheckDistance + 0.5f,
            groundLayer);

        if (grounded)
        {
            // Snap to ground surface and reset fall velocity
            rootPosition.y  = groundHit.point.y;
            _verticalVelocity = 0f;
        }
        else
        {
            // Accumulate downward velocity and apply it
            _verticalVelocity  -= gravityStrength * Time.deltaTime;
            rootPosition.y     += _verticalVelocity * Time.deltaTime;
        }

        // ── Separation ───────────────────────────────────────────
        Vector3 separation = Vector3.zero;
        int neighbourCount = Physics.OverlapSphereNonAlloc(
            rootPosition, separationRadius, _separationBuffer);
        for (int i = 0; i < neighbourCount; i++)
        {
            var col = _separationBuffer[i];
            if (col == null || col.transform == transform) continue;
            if (col.isTrigger) continue; // Skip trigger colliders (e.g. CritZoneMarker hit zones)
            if (col.GetComponentInParent<BearAI>() == null) continue;

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

        agent.nextPosition    = rootPosition;
        transform.position    = rootPosition;
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
            UnregisterAttacker();
            currentTarget = null;
            SetState(BearState.Return);
            return;
        }

        float distToTarget = Vector3.Distance(transform.position, currentTarget.position);
        float distToHome   = Vector3.Distance(transform.position, homePosition);

        // Leash check — too far from home
        if (distToHome > leashRadius)
        {
            UnregisterAttacker();
            currentTarget = null;
            SetState(BearState.Return);
            return;
        }

        // Re-evaluate target if current one is overwhelmed and a better one is nearby
        if (_detectionTimer <= 0f)
        {
            Transform betterTarget = FindLessContestedTarget();
            if (betterTarget != null && betterTarget != currentTarget)
            {
                UnregisterAttacker();
                currentTarget = betterTarget;
            }
        }

        // In attack range — register immediately so count blocks others this frame
        if (distToTarget <= attackRange && attackCooldownTimer <= 0f)
        {
            if (IsCrowded())
            {
                // Too many zombies already swinging — hold position and wait
                agent.ResetPath();
                return;
            }
            RegisterAttacker();
            SetState(BearState.Attack);
            return;
        }

        // Chase normally — NavMesh RVO handles separation during movement
        // Don't set agent.speed — root motion drives actual movement speed
        agent.SetDestination(currentTarget.position);
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
                {
                    UnregisterAttacker();
                    SetState(BearState.Chase);
                }
                else
                {
                    UnregisterAttacker();
                    SetState(BearState.Return);
                }
            }
            else
            {
                UnregisterAttacker();
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
                break;

            case BearState.Dead:
                UnregisterAttacker();
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

    // ─── Crowd Control ───

    private void RegisterAttacker()
    {
        if (_registeredAsAttacker || currentTarget == null) return;
        _attackerCounts.TryGetValue(currentTarget, out int c);
        _attackerCounts[currentTarget] = c + 1;
        _registeredAsAttacker = true;
    }

    private void UnregisterAttacker()
    {
        if (!_registeredAsAttacker || currentTarget == null) return;
        if (_attackerCounts.TryGetValue(currentTarget, out int c))
        {
            int next = c - 1;
            if (next <= 0) _attackerCounts.Remove(currentTarget);
            else           _attackerCounts[currentTarget] = next;
        }
        _registeredAsAttacker = false;
    }

    /// <summary>
    /// Returns true if too many enemies are already actively attacking the current target.
    /// </summary>
    private bool IsCrowded()
    {
        if (currentTarget == null) return false;
        _attackerCounts.TryGetValue(currentTarget, out int c);
        return c >= maxAttackersPerTarget;
    }

    /// <summary>
    /// If current target is at or over the attacker cap, find a living target in range
    /// with fewer attackers. Returns null if no better target exists.
    /// </summary>
    private Transform FindLessContestedTarget()
    {
        // Only bother switching if current target is already overwhelmed
        _attackerCounts.TryGetValue(currentTarget, out int currentCount);
        if (currentCount < maxAttackersPerTarget) return null;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, detectionRadius, _detectionBuffer, playerLayer);

        Transform best     = null;
        int       bestCount = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var col = _detectionBuffer[i];
            if (col == null) continue;

            var receiver = col.GetComponentInParent<DamageReceiver>();
            if (receiver == null) continue;
            if (receiver.IsDead) continue;

            Transform t = receiver.transform;
            if (t == currentTarget) continue;

            _attackerCounts.TryGetValue(t, out int c);
            if (c < bestCount)
            {
                bestCount = c;
                best      = t;
            }
        }

        return best;
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
            if (receiver.IsDead) continue;

            Transform targetRoot = receiver.transform;

            if (!candidates.Contains(targetRoot))
                candidates.Add(targetRoot);
        }

        if (candidates.Count == 0) return null;

        // Already chasing — target switching is handled in UpdateChase, just validate still alive
        if (currentTarget != null)
        {
            return candidates.Contains(currentTarget) ? currentTarget : candidates[0];
        }

        // First pick — prefer least-contested target so zombies spread across clients
        Transform picked    = null;
        int       bestCount = int.MaxValue;
        float     bestDist  = float.MaxValue;

        foreach (var t in candidates)
        {
            _attackerCounts.TryGetValue(t, out int c);
            float d = Vector3.Distance(transform.position, t.position);
            // Prefer fewest attackers; break ties by distance
            if (c < bestCount || (c == bestCount && d < bestDist))
            {
                bestCount = c;
                bestDist  = d;
                picked    = t;
            }
        }

        return picked;
    }

    private bool IsTargetAlive(Transform target)
    {
        if (target == null) return false;

        var receiver = target.GetComponentInParent<DamageReceiver>();
        if (receiver != null && receiver.IsDead) return false;

        var vitals = target.GetComponentInParent<VitalManager>();
        if (vitals == null) return true; // No vitals = assume alive
        if (vitals.IsDead) return false;

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

        // Ground raycast gizmo
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        float rayLength   = groundCheckDistance + 0.5f;
        bool rayHit = Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayLength, groundLayer);
        Gizmos.color = rayHit ? Color.green : Color.red;
        Gizmos.DrawLine(rayOrigin, rayOrigin + Vector3.down * rayLength);
        if (rayHit)
            Gizmos.DrawWireSphere(hit.point, 0.1f);

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
