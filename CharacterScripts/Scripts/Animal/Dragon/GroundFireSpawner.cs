using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Drops ground fire patches along the dragon's fire breath impact line. Owner-driven
/// detection (matches DragonFireBreathDamage pattern): owner raycasts down from a point
/// along the breath cone forward at fixed intervals while IsBreathingFire is true, then
/// sends a ServerRpc that authoritatively spawns a patch in the host's GroundFirePool and
/// fans out a ClientRpc so every client spawns a visual patch from their local pool.
/// </summary>
public class GroundFireSpawner : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonCombatController combatController;
    [SerializeField] private DragonFlightController flightController;
    [Tooltip("Where the fire cone originates (mouth bone). Same source as DragonFireBreathDamage.")]
    [SerializeField] private Transform fireOrigin;
    [Tooltip("Camera transform — used in flight to aim the cone forward.")]
    [SerializeField] private Transform cam;

    [Header("Spawn Cadence")]
    [Tooltip("Seconds between patch spawn attempts while breathing fire.")]
    [SerializeField] private float spawnInterval = 0.05f;
    [Tooltip("Distance forward along the cone to project before raycasting down.")]
    [SerializeField] private float forwardProjection = 8f;
    [Tooltip("Random scatter applied to forward projection per spawn (meters).")]
    [SerializeField] private float scatterRadius = 1.5f;

    [Header("Ground Raycast")]
    [Tooltip("Separate mask for ground detection. DO NOT leave at Default-only — set explicitly in Inspector.")]
    [SerializeField] private LayerMask groundMask = 0;
    [Tooltip("Max raycast distance downward when searching for ground.")]
    [SerializeField] private float maxGroundDistance = 50f;

    [Header("Patch Tuning")]
    [SerializeField] private float patchLifetime = 3f;
    [SerializeField] private float patchFadeDuration = 1.2f;
    [SerializeField] private float patchDamagePerTick = 5f;
    [SerializeField] private float patchTickInterval = 0.5f;
    [Tooltip("Burn duration added to BurnStatus when a target enters this patch.")]
    [SerializeField] private float patchBurnTimeOnContact = 1.5f;

    private float _spawnTimer;
    private NetworkObject _ownerNetObj;

    private void Awake()
    {
        if (combatController == null) combatController = GetComponentInParent<DragonCombatController>();
        if (flightController == null) flightController = GetComponentInParent<DragonFlightController>();
        if (cam == null) cam = Camera.main?.transform;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _ownerNetObj = GetComponentInParent<NetworkObject>();
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (combatController == null || !combatController.IsBreathingFire) { _spawnTimer = 0f; return; }
        if (fireOrigin == null) return;

        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer > 0f) return;
        _spawnTimer = spawnInterval;

        TrySpawnPatch();
    }

    private void TrySpawnPatch()
    {
        bool inFlight = flightController != null && flightController.IsFlightMode;
        Vector3 forward = (inFlight && cam != null) ? cam.forward : fireOrigin.forward;
        Vector3 origin = fireOrigin.position;

        Vector3 scatter = Random.insideUnitSphere * scatterRadius;
        scatter.y = 0f;
        Vector3 projected = origin + forward * forwardProjection + scatter;

        // Raycast down from above the projected point to find ground
        Vector3 rayStart = projected + Vector3.up * (maxGroundDistance * 0.5f);
        if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxGroundDistance, groundMask, QueryTriggerInteraction.Ignore))
            return;

        RequestSpawnGroundFireServerRpc(hit.point, hit.normal);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestSpawnGroundFireServerRpc(Vector3 position, Vector3 normal)
    {
        ulong sourceOwnerId = _ownerNetObj != null ? _ownerNetObj.OwnerClientId : OwnerClientId;
        SpawnLocally(position, normal, sourceOwnerId);
        SpawnGroundFireClientRpc(position, normal, sourceOwnerId);
    }

    [ClientRpc]
    private void SpawnGroundFireClientRpc(Vector3 position, Vector3 normal, ulong sourceOwnerId)
    {
        // Host already spawned via SpawnLocally inside the ServerRpc
        if (IsServer) return;
        SpawnLocally(position, normal, sourceOwnerId);
    }

    private void SpawnLocally(Vector3 position, Vector3 normal, ulong sourceOwnerId)
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

        GroundFirePool.Instance.SpawnAt(position, normal, config, sourceOwnerId);
    }
}
