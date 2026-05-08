using System.Collections.Generic;
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

    [Header("Damage Routing")]
    [Tooltip("Vital ID used when an incoming hit has no zone routing. Humans = \"health\". Dragon = \"torso\".")]
    [SerializeField] private string defaultVitalID = "health";
    [Tooltip("Vital ID drained by critical hits (proportional to final damage). Empty = disabled. Dragon uses \"stamina\".")]
    [SerializeField] private string critStaminaVitalID = "";
    [Tooltip("Stamina drained per unit of final crit damage. e.g. 1.0 = 1:1, 0.5 = half.")]
    [SerializeField] private float critToStaminaRatio = 1f;
    [Tooltip("If true, a crit landing while critStaminaVital is at zero kills this entity (exhaustion kill).")]
    [SerializeField] private bool exhaustionKillEnabled = false;

    [Header("Block Settings")]
    [Tooltip("Percentage of damage absorbed when blocking (0-1)")]
    [SerializeField] private float blockDamageReduction = 0.8f;
    [Tooltip("Angle in degrees — attacks from within this cone in front are blockable")]
    [SerializeField] private float blockAngle = 120f;
    [Tooltip("Stamina consumed per block = incomingDamage * this ratio")]
    [SerializeField] private float blockStaminaRatio = 0.5f;

    [Header("Hit Feedback")]
    [SerializeField] private float hitStunDuration = 0.2f;

    [Header("Death Collision")]
    [Tooltip("Disable physical hit colliders while dead so NPCs and weapons stop targeting the corpse.")]
    [SerializeField] private bool disableCollidersOnDeath = true;
    [Tooltip("Optional explicit colliders to disable on death. Leave empty to auto-disable enabled non-trigger child colliders.")]
    [SerializeField] private Collider[] collidersToDisableOnDeath;
    [SerializeField] private bool autoFindCollidersToDisableOnDeath = true;
    [SerializeField] private bool keepTriggerCollidersOnDeath = true;

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
    public System.Action<float, Vector3> OnDamageParried;
    /// <summary>Server-only hook for physical ranged hits that should alert AI.</summary>
    public System.Action<float, Vector3, Vector3, NetworkObject> OnRangedDamageReceived;
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
    private readonly List<Collider> _disabledDeathColliders = new List<Collider>();

    public bool IsHitStunned => _hitStunTimer > 0f;
    public bool IsDead => vitalManager != null && vitalManager.IsDead;

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
        if (IsDead)
            return;

        if (combatController != null && combatController.IsInvincible)
            return;

        var defenseProvider = GetComponentInParent<IDamageDefenseProvider>();
        if (defenseProvider != null && defenseProvider.IsDefenseInvincible)
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

        // NPC defense is resolved authoritatively on the server. Human blocking
        // keeps the existing local feedback path for responsiveness.
        if (defenseProvider == null && isBlocking && attackFromFront)
        {
            float blockedAmount = hitInfo.GetDamage() * blockDamageReduction;
            OnDamageBlocked?.Invoke(blockedAmount, hitInfo.hitPoint);
        }
        else if (defenseProvider == null)
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
    ///
    /// zoneVitalID: optional per-zone vital routing key (e.g. "head", "wings"). Null/empty
    /// falls back to defaultVitalID. Crit-zone projectiles supply this from CritZoneMarker.ZoneName.
    /// </summary>
    public void ApplyProjectileDamage(
        float damage,
        Vector3 attackerPosition,
        float critMultiplier = 0f,
        string zoneVitalID = null,
        Vector3? hitPoint = null,
        NetworkObject attackerObject = null,
        bool triggerRangedAlert = false)
    {
        if (!IsServer) return;
        if (IsDead) return;

        // Crit multiplier scales both health damage and stagger accumulation
        float finalDamage = critMultiplier > 0f ? damage * critMultiplier : damage;
        Vector3 damagePoint = hitPoint ?? attackerPosition;

        string targetVitalID = string.IsNullOrEmpty(zoneVitalID) ? defaultVitalID : zoneVitalID;

        if (vitalManager != null)
            vitalManager.ApplyDamage(targetVitalID, finalDamage);

        if (triggerRangedAlert)
            OnRangedDamageReceived?.Invoke(finalDamage, damagePoint, attackerPosition, attackerObject);

        // Crit also drains stamina (exhaustion kill setup).
        if (critMultiplier > 0f && !string.IsNullOrEmpty(critStaminaVitalID) && vitalManager != null)
        {
            vitalManager.ApplyDamage(critStaminaVitalID, finalDamage * critToStaminaRatio);

            // Exhaustion kill: crit landed while stamina at zero (after drain).
            if (exhaustionKillEnabled)
            {
                var staminaVital = vitalManager.GetVital(critStaminaVitalID);
                if (staminaVital != null && staminaVital.IsDepleted)
                    vitalManager.TriggerDeath();
            }
        }

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

        NotifyHitWithDefenseClientRpc(
            finalDamage,
            damagePoint,
            (byte)DamageDefenseResult.None,
            attackerPosition,
            triggerHitAnimation);
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

        if (IsDead)
            return;

        float dist = Vector3.Distance(attackerObj.transform.position, transform.position);
        float maxRange = 5f;
        if (dist > maxRange)
            return;

        float finalDamage = rawDamage;
        DamageDefenseResult defenseResult = EvaluateServerDefense(attackerObj, rawDamage, hitPoint, wasBlocking);

        if (defenseResult == DamageDefenseResult.Parry)
        {
            finalDamage = 0f;
        }
        else if (defenseResult == DamageDefenseResult.Block)
        {
            finalDamage *= (1f - blockDamageReduction);

            if (vitalManager != null)
            {
                float blockStaminaCost = rawDamage * blockStaminaRatio;
                vitalManager.TryConsumeStamina(blockStaminaCost);
            }
        }

        var defenseProvider = GetComponentInParent<IDamageDefenseProvider>();
        defenseProvider?.OnServerDefenseResolved(defenseResult, rawDamage, finalDamage, hitPoint, attackerObj);

        if (vitalManager != null && finalDamage > 0f)
            vitalManager.ApplyDamage(defaultVitalID, finalDamage);

        NotifyHitWithDefenseClientRpc(finalDamage, hitPoint, (byte)defenseResult, attackerObj.transform.position);
    }

    [ClientRpc]
    private void NotifyHitWithDefenseClientRpc(float damage, Vector3 hitPoint, byte defenseResultValue, Vector3 attackerPosition, bool triggerHitAnimation = true)
    {
        _hitStunTimer = hitStunDuration;

        _lastAttackerPosition = attackerPosition;

        DamageDefenseResult defenseResult = (DamageDefenseResult)defenseResultValue;

        if (defenseResult == DamageDefenseResult.Parry)
            OnDamageParried?.Invoke(damage, hitPoint);
        else if (defenseResult == DamageDefenseResult.Block)
            OnDamageBlocked?.Invoke(damage, hitPoint);
        else
        {
            OnDamageReceived?.Invoke(damage, hitPoint);
            if (triggerHitAnimation)
                OnPlayHitAnimation?.Invoke(attackerPosition);
        }
    }

    private DamageDefenseResult EvaluateServerDefense(NetworkObject attackerObj, float rawDamage, Vector3 hitPoint, bool clientBlockingHint)
    {
        var defenseProvider = GetComponentInParent<IDamageDefenseProvider>();
        if (defenseProvider != null)
        {
            return defenseProvider.EvaluateDefense(attackerObj, hitPoint, rawDamage);
        }

        bool isBlocking = combatController != null &&
                          combatController.State == CombatController.CombatState.Blocking;

        // Preserve the existing human block path for remote-owner sync latency,
        // but still recompute the frontal cone on the server.
        if (!isBlocking && !clientBlockingHint)
            return DamageDefenseResult.None;

        Vector3 toAttacker = (attackerObj.transform.position - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, toAttacker);
        return angle < blockAngle * 0.5f ? DamageDefenseResult.Block : DamageDefenseResult.None;
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

        SetDeathCollidersEnabled(false);
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
        SetDeathCollidersEnabled(false);

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

    public void RestoreAfterRespawn()
    {
        SetDeathCollidersEnabled(true);
    }

    private void SetDeathCollidersEnabled(bool enabled)
    {
        if (!disableCollidersOnDeath)
            return;

        if (enabled)
        {
            for (int i = 0; i < _disabledDeathColliders.Count; i++)
            {
                if (_disabledDeathColliders[i] != null)
                    _disabledDeathColliders[i].enabled = true;
            }

            _disabledDeathColliders.Clear();
            return;
        }

        foreach (var col in GetDeathColliders())
        {
            if (col == null || !col.enabled || ShouldKeepColliderOnDeath(col))
                continue;

            col.enabled = false;
            if (!_disabledDeathColliders.Contains(col))
                _disabledDeathColliders.Add(col);
        }
    }

    private IEnumerable<Collider> GetDeathColliders()
    {
        if (collidersToDisableOnDeath != null && collidersToDisableOnDeath.Length > 0)
            return collidersToDisableOnDeath;

        if (autoFindCollidersToDisableOnDeath)
            return GetComponentsInChildren<Collider>(true);

        return System.Array.Empty<Collider>();
    }

    private bool ShouldKeepColliderOnDeath(Collider col)
    {
        return keepTriggerCollidersOnDeath && col.isTrigger;
    }

}
