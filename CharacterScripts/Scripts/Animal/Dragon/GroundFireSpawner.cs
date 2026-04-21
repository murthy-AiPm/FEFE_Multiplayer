using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Drops ground fire patches where the dragon's fire breath VFX particles physically
/// land. Particle collision events are forwarded by FireBreathParticleHandler on the
/// owner's local VFX instance. Spawn rate is throttled by spawnInterval so dense
/// particle hits don't flood the GroundFirePool.
///
/// Range / aim / surface filtering all live on the VFX particle system (Collision
/// module layer mask). This spawner has no raycast and no aim direction of its own.
/// </summary>
public class GroundFireSpawner : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonCombatController combatController;

    [Header("Spawn Cadence")]
    [Tooltip("Minimum seconds between patch spawns regardless of how many particle collisions arrive.")]
    [SerializeField] private float spawnInterval = 0.05f;
    [Tooltip("Random scatter applied to the hit point per spawn (meters), tangent to the surface.")]
    [SerializeField] private float scatterRadius = 0.5f;

    [Header("Patch Tuning")]
    [SerializeField] private float patchLifetime = 3f;
    [SerializeField] private float patchFadeDuration = 1.2f;
    [SerializeField] private float patchDamagePerTick = 5f;
    [SerializeField] private float patchTickInterval = 0.5f;
    [Tooltip("Burn duration added to BurnStatus when a target enters this patch.")]
    [SerializeField] private float patchBurnTimeOnContact = 1.5f;

    private float _nextAllowedSpawnTime;
    private NetworkObject _ownerNetObj;

    private void Awake()
    {
        if (combatController == null) combatController = GetComponentInParent<DragonCombatController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _ownerNetObj = GetComponentInParent<NetworkObject>();
    }

    /// <summary>
    /// Called by FireBreathParticleHandler on the owner's local VFX instance, with the
    /// actual particle intersection point and surface normal. Throttled by spawnInterval.
    /// </summary>
    public void HandleParticleHit(Vector3 point, Vector3 normal)
    {
        if (!IsOwner) return;
        if (Time.time < _nextAllowedSpawnTime) return;
        _nextAllowedSpawnTime = Time.time + spawnInterval;

        Vector3 scatter = Random.insideUnitSphere * scatterRadius;
        Vector3 tangentScatter = Vector3.ProjectOnPlane(scatter, normal);
        Vector3 spawnPos = point + tangentScatter;

        RequestSpawnGroundFireServerRpc(spawnPos, normal);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestSpawnGroundFireServerRpc(Vector3 position, Vector3 normal)
    {
        NetworkObjectReference sourceRef = _ownerNetObj != null ? new NetworkObjectReference(_ownerNetObj) : default;
        SpawnLocally(position, normal, sourceRef);
        SpawnGroundFireClientRpc(position, normal, sourceRef);
    }

    [ClientRpc]
    private void SpawnGroundFireClientRpc(Vector3 position, Vector3 normal, NetworkObjectReference sourceRef)
    {
        if (IsServer) return;
        SpawnLocally(position, normal, sourceRef);
    }

    private void SpawnLocally(Vector3 position, Vector3 normal, NetworkObjectReference sourceRef)
    {
        if (GroundFirePool.Instance == null) return;

        var config = new GroundFirePatchConfig
        {
            lifetime = patchLifetime,
            fadeDuration = patchFadeDuration,
            damagePerTick = patchDamagePerTick,
            tickInterval = patchTickInterval,
            burnTimeOnContact = patchBurnTimeOnContact,
        };

        GroundFirePool.Instance.SpawnAt(position, normal, config, sourceRef);
    }
}
