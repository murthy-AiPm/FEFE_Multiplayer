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

    [Header("Animation")]
    [SerializeField] private RuleAnimancerDriver animancerDriver;

    // Events
    public System.Action<float, Vector3> OnDamageReceived;
    public System.Action<float, Vector3> OnDamageBlocked;
    public System.Action OnDeath;

    private float _hitStunTimer;

    public bool IsHitStunned => _hitStunTimer > 0f;

    private void Awake()
    {
        if (vitalManager == null) vitalManager = GetComponentInParent<VitalManager>();
        if (combatController == null) combatController = GetComponentInParent<CombatController>();
        if (weaponManager == null) weaponManager = GetComponentInParent<WeaponManager>();
        if (animancerDriver == null) animancerDriver = GetComponentInChildren<RuleAnimancerDriver>();
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
    private void NotifyHitClientRpc(float damage, Vector3 hitPoint, bool wasBlocked, Vector3 attackerPosition)
    {
        _hitStunTimer = hitStunDuration;

        if (wasBlocked)
            OnDamageBlocked?.Invoke(damage, hitPoint);
        else
        {
            OnDamageReceived?.Invoke(damage, hitPoint);

            var driver = GetComponentInChildren<RuleAnimancerDriver>();
            if (driver != null)
                driver.PlayHitReaction(attackerPosition);
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

        if (animancerDriver != null)
            animancerDriver.PlayDeath();

        if (IsOwner && isPlayer)
        {
            var deathScreen = FindObjectOfType<DeathScreen>();
            if (deathScreen != null)
                deathScreen.Show(GetComponent<NetworkObject>());
        }
    }

}
