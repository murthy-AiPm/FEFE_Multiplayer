using Unity.Netcode;
using Unity.Cinemachine;
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
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float corpseVisibleTime = 3f;

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
    private Coroutine _hideCorpseCoroutine;

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

        NotifyDeathClientRpc();

        if (IsServer && isPlayer)
        {
            if (_hideCorpseCoroutine != null)
                StopCoroutine(_hideCorpseCoroutine);
            _hideCorpseCoroutine = StartCoroutine(HideCorpseAfterDelay());
        }
    }

    [ClientRpc]
    private void NotifyDeathClientRpc()
    {
        var input = GetComponentInChildren<InputController>();
        if (input != null) input.enabled = false;

        var tps = GetComponentInChildren<ThirdPersonController>();
        if (tps != null) tps.enabled = false;

        var combat = GetComponentInChildren<CombatController>();
        if (combat != null) combat.enabled = false;

        if (animancerDriver != null)
            animancerDriver.PlayDeath();

        if (IsOwner && isPlayer)
        {
            var deathScreen = FindObjectOfType<DeathScreen>();
            if (deathScreen != null)
                deathScreen.Show(GetComponent<NetworkObject>());
        }
    }

    private System.Collections.IEnumerator HideCorpseAfterDelay()
    {
        yield return new WaitForSeconds(corpseVisibleTime);
        HideCorpseClientRpc();
        _hideCorpseCoroutine = null;
    }

    [ClientRpc]
    private void HideCorpseClientRpc()
    {
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;
    }

    // ─── Respawn ───

    [ServerRpc(RequireOwnership = false)]
    public void RequestRespawnServerRpc(int spawnPointIndex)
    {
        if (_hideCorpseCoroutine != null)
        {
            StopCoroutine(_hideCorpseCoroutine);
            _hideCorpseCoroutine = null;
        }

        var points = SpawnPoint.GetAllSpawnPoints();

        Vector3 spawnPos;
        Quaternion spawnRot;

        if (points != null && spawnPointIndex >= 0 && spawnPointIndex < points.Count)
        {
            spawnPos = points[spawnPointIndex].transform.position;
            spawnRot = points[spawnPointIndex].transform.rotation;
        }
        else
        {
            spawnPos = SpawnPoint.GetRandomSpawnPos();
            spawnRot = Quaternion.identity;
        }

        if (vitalManager != null)
            vitalManager.ResetAllVitals();

        NotifyRespawnClientRpc(spawnPos, spawnRot);
    }

    [ClientRpc]
    private void NotifyRespawnClientRpc(Vector3 spawnPos, Quaternion spawnRot)
    {
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = true;

        // Force dismount if mounted before teleporting
        if (IsOwner)
        {
            var mountController = GetComponentInChildren<MountController>();
            if (mountController != null && (mountController.IsMounted || mountController.IsTransitioning))
                mountController.ForceDismount();
        }

        // Cache vcam FOV before any state changes that might reset it
        CinemachineCamera vcam = GetComponentInChildren<CinemachineCamera>(true);
        float cachedFOV = vcam != null ? vcam.Lens.FieldOfView : 0f;

        var cc = GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = spawnPos;
        transform.rotation = spawnRot;

        if (cc != null) cc.enabled = IsOwner;

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

            var deathScreen = FindObjectOfType<DeathScreen>();
            if (deathScreen != null)
                deathScreen.Hide();
        }

        if (animancerDriver != null)
            animancerDriver.PlayRespawn();

        // Restore FOV in case anything reset it
        if (vcam != null && cachedFOV > 0f)
        {
            var lens = vcam.Lens;
            lens.FieldOfView = cachedFOV;
            vcam.Lens = lens;
        }
    }
}
