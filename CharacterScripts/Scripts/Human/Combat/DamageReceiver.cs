using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Place on the player prefab (root or on a collider child).
/// Receives hit notifications from HitboxController and forwards to server for validation.
/// Also handles block absorption and invincibility checks.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class DamageReceiver : NetworkBehaviour
{
    [Header("Respawn")]
    [SerializeField] private bool isPlayer = true;

    [Header("Dependencies")]
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private CombatController combatController;
    [SerializeField] private WeaponManager weaponManager;

    [Header("Block Settings")]
    [Tooltip("Percentage of damage absorbed when blocking (0-1)")]
    [SerializeField] private float blockDamageReduction = 0.8f;
    [Tooltip("Angle in degrees — attacks from within this cone in front are blockable")]
    [SerializeField] private float blockAngle = 120f;
    [Tooltip("Stamina consumed per block = incomingDamage * this ratio")]
    [SerializeField] private float blockStaminaRatio = 0.5f;

    [Header("Hit Feedback")]
    [SerializeField] private float hitStunDuration = 0.2f;

    [Header("Stagger (Projectile Crit Zones)")]
    [Tooltip("Accumulated stagger damage from crit-zone projectile hits needed to trigger a hit reaction animation.")]
    [SerializeField] private float staggerThreshold = 200f;
    [Tooltip("Stagger damage drains at this rate per second after the decay delay expires.")]
    [SerializeField] private float staggerDecayRate = 50f;
    [Tooltip("Seconds after the last crit-zone hit before stagger starts decaying.")]
    [SerializeField] private float staggerDecayDelay = 2f;

    // Events
    public System.Action<float, Vector3> OnDamageReceived;
    public System.Action<float, Vector3> OnDamageBlocked;
    public System.Action OnDeath;

    /// <summary>Fired on all clients when a hit lands (not blocked). Subscriber plays hit reaction animation.</summary>
    public System.Action<Vector3> OnPlayHitAnimation;
    /// <summary>Fired on all clients when character dies. Subscriber plays death animation. Vector3 = last attacker position.</summary>
    public System.Action<Vector3> OnPlayDeathAnimation;

    private float _hitStunTimer;
    private Vector3 _lastAttackerPosition;

    // Stagger accumulation (server only)
    private float _staggerAccumulated;
    private float _timeSinceLastCritHit;

    public bool IsHitStunned => _hitStunTimer > 0f;

    private void Awake()
    {
        if (vitalManager == null) vitalManager = GetComponentInParent<VitalManager>();
        if (combatController == null) combatController = GetComponentInParent<CombatController>();
        if (weaponManager == null) weaponManager = GetComponentInParent<WeaponManager>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (vitalManager != null)
            vitalManager.OnDeath += HandleDeath;
    }

    public override void OnNetworkDespawn()
    {
        if (vitalManager != null)
            vitalManager.OnDeath -= HandleDeath;

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (_hitStunTimer > 0f)
            _hitStunTimer -= Time.deltaTime;

        // Stagger decay (server only)
        if (IsServer && _staggerAccumulated > 0f)
        {
            _timeSinceLastCritHit += Time.deltaTime;
            if (_timeSinceLastCritHit >= staggerDecayDelay)
            {
                _staggerAccumulated = Mathf.Max(0f, _staggerAccumulated - staggerDecayRate * Time.deltaTime);
            }
        }
    }

    public void OnHitLocal(HitInfo hitInfo)
    {
        if (combatController != null && combatController.IsInvincible)
            return;

        bool isBlocking = combatController != null &&
                          combatController.State == CombatController.CombatState.Blocking;

        bool attackFromFront = false;
        if (isBlocking && hitInfo.attackerNetObj != null)
        {
            Vector3 toAttacker = (hitInfo.attackerNetObj.transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, toAttacker);
            attackFromFront = angle < blockAngle * 0.5f;
        }

        if (isBlocking && attackFromFront)
        {
            float blockedAmount = hitInfo.GetDamage() * blockDamageReduction;
            OnDamageBlocked?.Invoke(blockedAmount, hitInfo.hitPoint);
        }
        else
        {
            OnDamageReceived?.Invoke(hitInfo.GetDamage(), hitInfo.hitPoint);
        }

        if (hitInfo.attackerNetObj != null)
        {
            RequestDamageServerRpc(
                hitInfo.attackerNetObj.NetworkObjectId,
                hitInfo.GetDamage(),
                hitInfo.hitPoint,
                isBlocking && attackFromFront
            );
        }
    }

    // ─── Server-Authoritative Damage ───

    /// <summary>
    /// Called from server-side projectiles (e.g. BallistaArrow) that bypass HitboxController.
    /// Applies damage via VitalManager. If critMultiplier > 0 (hit landed on a CritZoneMarker),
    /// accumulates stagger damage. When stagger crosses the threshold, fires the hit reaction
    /// animation and resets the accumulator.
    /// MUST be called on the server only.
    /// </summary>
    public void ApplyProjectileDamage(float damage, Vector3 attackerPosition, float critMultiplier = 0f)
    {
        if (!IsServer) return;

        // Crit multiplier scales both health damage and stagger accumulation
        float finalDamage = critMultiplier > 0f ? damage * critMultiplier : damage;

        if (vitalManager != null)
            vitalManager.ApplyDamage("health", finalDamage);

        // Stagger accumulation: only crit-zone hits (critMultiplier > 0) contribute.
        // Non-crit hits (critMultiplier == 0) deal base damage but never trigger hit anim.
        bool triggerHitAnimation = false;
        if (critMultiplier > 0f)
        {
            _staggerAccumulated += finalDamage;
            _timeSinceLastCritHit = 0f;

            if (_staggerAccumulated >= staggerThreshold)
            {
                triggerHitAnimation = true;
                _staggerAccumulated = 0f;
            }
        }

        NotifyHitClientRpc(finalDamage, attackerPosition, false, attackerPosition, triggerHitAnimation);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDamageServerRpc(
        ulong attackerNetId,
        float rawDamage,
        Vector3 hitPoint,
        bool wasBlocking,
        ServerRpcParams rpcParams = default)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerNetId, out var attackerObj))
            return;

        float dist = Vector3.Distance(attackerObj.transform.position, transform.position);
        float maxRange = 5f;
        if (dist > maxRange)
            return;

        float finalDamage = rawDamage;

        if (wasBlocking)
        {
            finalDamage *= (1f - blockDamageReduction);

            if (vitalManager != null)
            {
                float blockStaminaCost = rawDamage * blockStaminaRatio;
                vitalManager.TryConsumeStamina(blockStaminaCost);
            }
        }

        if (vitalManager != null)
            vitalManager.ApplyDamage("health", finalDamage);

        NotifyHitClientRpc(finalDamage, hitPoint, wasBlocking, attackerObj.transform.position);
    }

    [ClientRpc]
    private void NotifyHitClientRpc(float damage, Vector3 hitPoint, bool wasBlocked, Vector3 attackerPosition, bool triggerHitAnimation = true)
    {
        _hitStunTimer = hitStunDuration;

        _lastAttackerPosition = attackerPosition;

        if (wasBlocked)
            OnDamageBlocked?.Invoke(damage, hitPoint);
        else
        {
            OnDamageReceived?.Invoke(damage, hitPoint);
            if (triggerHitAnimation)
                OnPlayHitAnimation?.Invoke(attackerPosition);
        }
    }

    // ─── Death ───

    private void HandleDeath()
    {
        OnDeath?.Invoke();

        var ballistaOperator = GetComponent<BallistaOperator>();
        if (ballistaOperator != null && ballistaOperator.IsOperating)
        {
            var ballista = ballistaOperator.CurrentBallista;
            if (ballista != null)
                ballista.ForceDismount(GetComponent<NetworkObject>().OwnerClientId);
        }

        // Force dismount from horse if mounted
        var mountController = GetComponent<MountController>();
        if (mountController != null && (mountController.IsMounted || mountController.IsTransitioning))
        {
            var mount = mountController.CurrentMount;
            if (mount != null)
                mount.ForceServerDismount();
        }

        NotifyDeathClientRpc();

        if (IsServer && isPlayer)
        {
            var respawnController = GetComponent<RespawnController>();
            if (respawnController != null)
                respawnController.StartCorpseTimer();
        }
    }

    [ClientRpc]
    private void NotifyDeathClientRpc()
    {
        // Human controllers
        var input = GetComponentInChildren<InputController>();
        if (input != null) input.enabled = false;

        var tps = GetComponentInChildren<ThirdPersonController>();
        if (tps != null) tps.enabled = false;

        var combat = GetComponentInChildren<CombatController>();
        if (combat != null) combat.enabled = false;

        // Dragon/animal controllers
        var animalGround = GetComponentInChildren<AnimalGroundController>();
        if (animalGround != null) animalGround.enabled = false;

        var flightController = GetComponentInChildren<DragonFlightController>();
        if (flightController != null) flightController.enabled = false;

        OnPlayDeathAnimation?.Invoke(_lastAttackerPosition);

        if (IsOwner && isPlayer)
        {
            var deathScreen = FindObjectOfType<DeathScreen>();
            if (deathScreen != null)
                deathScreen.Show(GetComponent<NetworkObject>());
        }
    }

}
