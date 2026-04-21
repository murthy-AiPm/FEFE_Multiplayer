using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Deals damage-over-time to anything the dragon's fire breath VFX particles physically
/// hit. Particle collision events are forwarded by FireBreathParticleHandler on the
/// owner's local VFX instance. Each tick, the unique set of receivers hit during that
/// interval is sent to the server in a single ServerRpc which applies damage + burn.
///
/// Damage scales by tickRate: damagePerSecond / tickRate is applied per receiver per tick
/// while particles continue to land on it. Targets need a DamageReceiver to take damage.
///
/// Setup:
///   1. Add to dragon prefab; assign combatController
///   2. Tune damagePerSecond, tickRate, burnTimePerTick in Inspector
///   3. Range / aim / collision filtering all live on the VFX particle system itself
/// </summary>
public class DragonFireBreathDamage : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonCombatController combatController;

    [Header("Damage")]
    [Tooltip("Total damage applied per second to a target while particles continuously hit it.")]
    [SerializeField] private float damagePerSecond = 20f;
    [Tooltip("How many times per second damage is applied. Targets hit between ticks all get damage on the next tick.")]
    [SerializeField] private float tickRate = 4f;
    [Tooltip("Burn duration (seconds) added to BurnStatus per damage tick. Should be larger than the tick interval so burn time accumulates while particles continue to hit and persists after they stop.")]
    [SerializeField] private float burnTimePerTick = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogging;

    private float _tickTimer;
    private float _tickInterval;
    private float _damagePerTick;
    private NetworkObject _ownerNetObj;
    private readonly HashSet<ulong> _hitThisTick = new HashSet<ulong>();
    private Vector3 _lastHitPoint;

    private void Awake()
    {
        if (combatController == null)
            combatController = GetComponentInParent<DragonCombatController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _ownerNetObj = GetComponentInParent<NetworkObject>();
        RecalculateTickValues();
    }

    private void Update()
    {
        if (!IsOwner) return;

        _tickTimer += Time.deltaTime;
        if (_tickTimer < _tickInterval) return;
        _tickTimer -= _tickInterval;
        FlushTick();
    }

    /// <summary>
    /// Called by FireBreathParticleHandler on the owner's local VFX instance for every
    /// particle that collided with `other` this frame. Dedupes per-tick so a target hit
    /// by 50 particles in one frame still only takes one tick of damage.
    /// </summary>
    public void HandleParticleHit(GameObject other, Vector3 point)
    {
        if (!IsOwner) return;
        if (other == null) return;

        var hitNetObj = other.GetComponentInParent<NetworkObject>();
        if (hitNetObj == null) return;
        if (hitNetObj == _ownerNetObj) return;
        if (other.GetComponentInParent<DamageReceiver>() == null) return;

        _hitThisTick.Add(hitNetObj.NetworkObjectId);
        _lastHitPoint = point;

        if (debugLogging)
            Debug.Log($"[DragonFire] Particle hit on {other.name}");
    }

    private void FlushTick()
    {
        if (_hitThisTick.Count == 0) return;

        var ids = new ulong[_hitThisTick.Count];
        int i = 0;
        foreach (var id in _hitThisTick) ids[i++] = id;
        _hitThisTick.Clear();

        RequestFireDamageServerRpc(ids, _damagePerTick, burnTimePerTick, _lastHitPoint);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestFireDamageServerRpc(ulong[] hitIds, float damage, float burnTime, Vector3 origin)
    {
        if (hitIds == null) return;
        NetworkObjectReference sourceRef = _ownerNetObj != null ? new NetworkObjectReference(_ownerNetObj) : default;

        for (int i = 0; i < hitIds.Length; i++)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(hitIds[i], out var targetObj))
                continue;
            if (targetObj == null) continue;

            var receiver = targetObj.GetComponentInChildren<DamageReceiver>();
            if (receiver != null)
                receiver.ApplyProjectileDamage(damage, origin);

            var burnStatus = targetObj.GetComponentInChildren<BurnStatus>();
            if (burnStatus != null)
                burnStatus.Ignite(burnTime, sourceRef);
        }
    }

    private void RecalculateTickValues()
    {
        _tickInterval = tickRate > 0f ? 1f / tickRate : 0.25f;
        _damagePerTick = damagePerSecond * _tickInterval;
    }

    private void OnValidate()
    {
        RecalculateTickValues();
    }
}
