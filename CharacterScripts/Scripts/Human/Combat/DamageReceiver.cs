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
    [SerializeField] private bool isPlayer = true; // ← add this
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float respawnDelay = 3f;

    [Header("Dependencies")]
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private CombatController combatController;
    [SerializeField] private WeaponManager weaponManager;

    [Header("Block Settings")]
    [Tooltip("Percentage of damage absorbed when blocking (0-1)")]
    [SerializeField] private float blockDamageReduction = 0.8f;
    [Tooltip("Angle in degrees — attacks from within this cone in front are blockable")]
    [SerializeField] private float blockAngle = 120f;

    [Header("Hit Feedback")]
    [SerializeField] private float hitStunDuration = 0.2f; // brief speed reduction on hit

    //[Header("Respawn")]
    //[SerializeField] private Transform spawnPoint; // assign in inspector or find via SpawnManager
    //[SerializeField] private float respawnDelay = 3f;

    [Header("Animation")]
    [SerializeField] private RuleAnimancerDriver animancerDriver;

    // Events
    public System.Action<float, Vector3> OnDamageReceived;     // (damage, hitPoint)
    public System.Action<float, Vector3> OnDamageBlocked;      // (blockedDmg, hitPoint)
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

    /// <summary>
    /// Called locally when HitboxController detects a hit on this receiver.
    /// Client-side: plays visual/audio feedback immediately.
    /// Then sends to server for authoritative damage.
    /// </summary>
    public void OnHitLocal(HitInfo hitInfo)
    {
        // Skip if invincible (dodge i-frames)
        if (combatController != null && combatController.IsInvincible)
            return;

        // Check if we're blocking
        bool isBlocking = combatController != null &&
                          combatController.State == CombatController.CombatState.Blocking;

        bool attackFromFront = false;
        if (isBlocking && hitInfo.attackerNetObj != null)
        {
            Vector3 toAttacker = (hitInfo.attackerNetObj.transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, toAttacker);
            attackFromFront = angle < blockAngle * 0.5f;
        }

        // Local feedback (immediate, client-side — feels responsive)
        if (isBlocking && attackFromFront)
        {
            // Block feedback (sparks, sound, etc.)
            float blockedAmount = hitInfo.GetDamage() * blockDamageReduction;
            OnDamageBlocked?.Invoke(blockedAmount, hitInfo.hitPoint);
        }
        else
        {
            // Hit feedback (blood, sound, screen shake, etc.)
            OnDamageReceived?.Invoke(hitInfo.GetDamage(), hitInfo.hitPoint);
        }

        // Send to server for authoritative validation
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
        // Server validation
        // 1. Check attacker exists and is in attack state
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerNetId, out var attackerObj))
            return;

        // 2. Range check (prevent cheating)
        float dist = Vector3.Distance(attackerObj.transform.position, transform.position);
        float maxRange = 5f; // generous range for validation (actual hitbox is tighter)
        if (dist > maxRange)
            return;

        // 3. Calculate final damage
        float finalDamage = rawDamage;

        if (wasBlocking)
        {
            finalDamage *= (1f - blockDamageReduction);

            // Consume block stamina
            if (vitalManager != null)
            {
                var weapon = weaponManager != null ? weaponManager.ActiveWeapon : null;
                float blockCost = weapon != null ? weapon.blockStaminaCost : 5f;
                vitalManager.TryConsumeStamina(blockCost);
            }
        }

        // 4. Apply damage
        if (vitalManager != null)
        {
            vitalManager.ApplyDamage("health", finalDamage);
        }

        // 5. Notify all clients of the hit for effects
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

            // Play hit reaction animation on all clients (owner + remote)
            var driver = GetComponentInChildren<RuleAnimancerDriver>();
            if (driver != null)
                driver.PlayHitReaction(attackerPosition);
        }

        // Remove the IsOwner early return — owner should also play the reaction
    }
    private System.Collections.IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);

        if (vitalManager != null)
            vitalManager.ResetAllVitals();

        // Use the SpawnPoint system instead of the serialized Transform
        Vector3 spawnPos = SpawnPoint.GetRandomSpawnPos();
        Quaternion spawnRot = Quaternion.identity;

        NotifyRespawnClientRpc(spawnPos, spawnRot);
    }

    [ClientRpc]
    private void NotifyRespawnClientRpc(Vector3 spawnPos, Quaternion spawnRot)
    {
        // Disable CharacterController before teleporting or it fights the position change
        var cc = GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = spawnPos;
        transform.rotation = spawnRot;

        if (cc != null) cc.enabled = IsOwner; // only owner needs it enabled

        if (IsOwner)
        {
            var input = GetComponentInChildren<InputController>();
            if (input != null) input.enabled = true;

            var tps = GetComponentInChildren<ThirdPersonController>();
            if (tps != null) tps.enabled = true;

            var combat = GetComponentInChildren<CombatController>();
            if (combat != null)
            {
                combat.enabled = true;
                combat.ResetState();
            }
        }

        if (animancerDriver != null)
            animancerDriver.PlayRespawn();
    }
    private void HandleDeath()
    {
        OnDeath?.Invoke();
        NotifyDeathClientRpc();
        // Dismount ballista if operating one
        var ballistaOperator = GetComponent<BallistaOperator>();
        if (ballistaOperator != null && ballistaOperator.IsOperating)
        {
            var ballista = ballistaOperator.CurrentBallista;
            if (ballista != null)
                ballista.ForceDismount(GetComponent<NetworkObject>().OwnerClientId);
        }

        if (IsServer && isPlayer)  // ← add isPlayer check
            StartCoroutine(RespawnAfterDelay());
    }

    [ClientRpc]
    private void NotifyDeathClientRpc()
    {
        // Disable input and movement so the corpse can't walk
        var input = GetComponentInChildren<InputController>();
        if (input != null) input.enabled = false;

        var tps = GetComponentInChildren<ThirdPersonController>();
        if (tps != null) tps.enabled = false;

        var combat = GetComponentInChildren<CombatController>();
        if (combat != null) combat.enabled = false;

        // Play death animation on all clients
        if (animancerDriver != null)
            animancerDriver.PlayDeath();
    }
}
