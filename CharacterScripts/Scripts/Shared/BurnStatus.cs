using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Burn-over-time status for any character that can catch fire (humans, NPCs, dragons).
/// Server-authoritative: tracks burn timer, applies DOT via VitalManager, spawns/despawns
/// fire VFX on all clients. Time spent in a fire source (e.g. fire breath cone) is added
/// to the burn timer via Ignite(). Burn continues after leaving the source until timer expires.
/// Re-entering refreshes (adds time on top, clamped to maxBurnDuration).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class BurnStatus : NetworkBehaviour
{
    [Header("Dependencies")]
    [Tooltip("VitalManager that receives DOT damage. Auto-found in parent if null.")]
    [SerializeField] private VitalManager vitalManager;
    [Tooltip("DamageReceiver used for hit notifications during burn ticks. Auto-found if null.")]
    [SerializeField] private DamageReceiver damageReceiver;
    [Tooltip("Bone the fire VFX is parented to. Pelvis recommended.")]
    [SerializeField] private Transform pelvisBone;

    [Header("Burn Settings")]
    [Tooltip("Damage applied per tick.")]
    [SerializeField] private float damagePerTick = 10f;
    [Tooltip("Seconds between damage ticks.")]
    [SerializeField] private float tickInterval = 1f;
    [Tooltip("Hard cap on accumulated burn duration (prevents indefinite burn from long cone exposure).")]
    [SerializeField] private float maxBurnDuration = 10f;

    [Header("VFX")]
    [Tooltip("Fire VFX prefab. Spawned and parented to pelvisBone on all clients while burning.")]
    [SerializeField] private GameObject fireVFXPrefab;
    [Tooltip("Uniform scale applied to spawned VFX. Tune per-character in Inspector.")]
    [SerializeField] private float vfxScale = 1f;

    // Network state
    private NetworkVariable<bool> netIsBurning = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> netBurnTimeRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server-side state
    private float _tickTimer;
    private bool _refreshLocked; // true after death — no more Ignite() accepted, but current burn finishes

    // Client-side state
    private GameObject _activeVFX;

    private void Awake()
    {
        if (vitalManager == null) vitalManager = GetComponentInParent<VitalManager>();
        if (damageReceiver == null) damageReceiver = GetComponentInParent<DamageReceiver>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        netIsBurning.OnValueChanged += OnBurningChanged;

        // Late-join: if already burning when we spawn, light up the VFX
        if (netIsBurning.Value)
            SpawnVFX();

        if (IsServer && damageReceiver != null)
            damageReceiver.OnDeath += HandleDeath;
    }

    public override void OnNetworkDespawn()
    {
        netIsBurning.OnValueChanged -= OnBurningChanged;

        if (IsServer && damageReceiver != null)
            damageReceiver.OnDeath -= HandleDeath;

        DespawnVFX();
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Server-only. Add time to the burn timer. Source ownerId is used to prevent self-ignition.
    /// </summary>
    public void Ignite(float addedDuration, ulong sourceOwnerId)
    {
        if (!IsServer) return;
        if (_refreshLocked) return;
        if (addedDuration <= 0f) return;

        // Self-immunity: a character cannot ignite itself
        if (sourceOwnerId == OwnerClientId) return;

        float newTime = Mathf.Min(netBurnTimeRemaining.Value + addedDuration, maxBurnDuration);
        netBurnTimeRemaining.Value = newTime;

        if (!netIsBurning.Value)
        {
            netIsBurning.Value = true;
            _tickTimer = tickInterval; // first tick after one full interval
        }
    }

    /// <summary>
    /// Server-only. Force-clear burn state (used on respawn).
    /// </summary>
    public void ClearBurn()
    {
        if (!IsServer) return;
        netBurnTimeRemaining.Value = 0f;
        netIsBurning.Value = false;
        _refreshLocked = false;
        _tickTimer = 0f;
    }

    private void Update()
    {
        if (!IsServer) return;
        if (!netIsBurning.Value) return;

        float dt = Time.deltaTime;

        // Decrement burn timer
        float remaining = netBurnTimeRemaining.Value - dt;
        if (remaining <= 0f)
        {
            netBurnTimeRemaining.Value = 0f;
            netIsBurning.Value = false;
            return;
        }
        netBurnTimeRemaining.Value = remaining;

        // DOT tick
        _tickTimer -= dt;
        if (_tickTimer <= 0f)
        {
            _tickTimer += tickInterval;
            ApplyTick();
        }
    }

    private void ApplyTick()
    {
        if (vitalManager == null) return;
        // Use ApplyProjectileDamage so burn ticks fire hit notifications (feedback + animation hooks)
        if (damageReceiver != null)
            damageReceiver.ApplyProjectileDamage(damagePerTick, transform.position);
        else
            vitalManager.ApplyDamage("health", damagePerTick);
    }

    private void HandleDeath()
    {
        // Lock further Ignite() calls but let current burn timer finish naturally —
        // corpse keeps burning for the remaining duration.
        _refreshLocked = true;
    }

    private void OnBurningChanged(bool oldValue, bool newValue)
    {
        if (newValue) SpawnVFX();
        else DespawnVFX();
    }

    private void SpawnVFX()
    {
        if (_activeVFX != null) return;
        if (fireVFXPrefab == null) return;

        Transform parent = pelvisBone != null ? pelvisBone : transform;
        _activeVFX = Instantiate(fireVFXPrefab, parent.position, parent.rotation, parent);
        _activeVFX.transform.localScale = Vector3.one * vfxScale;
    }

    private void DespawnVFX()
    {
        if (_activeVFX == null) return;
        Destroy(_activeVFX);
        _activeVFX = null;
    }
}
