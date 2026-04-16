using System.Collections;
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

    [Header("Surface Raycast")]
    [Tooltip("Separate mask for surface detection. DO NOT leave at Default-only — set explicitly in Inspector.")]
    [SerializeField] private LayerMask groundMask = 0;
    [Tooltip("Max raycast distance when searching for a surface along the breath direction.")]
    [SerializeField] private float maxGroundDistance = 50f;
    [Tooltip("How fast the flame stream travels (m/s). Patch spawn is delayed by hitDistance/streamSpeed so visual flame appears to cause the fire. Lower = more delay, more obvious travel time.")]
    [SerializeField] private float flameStreamSpeed = 30f;

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

        // Bullet-decal pattern: single raycast forward along breath direction. Hit any surface
        // within maxGroundDistance → spawn patch. Miss → no spawn. No aim-angle gating. Empty sky
        // breathes produce no patches because the ray hits nothing. Ground, walls, ceilings, slopes
        // all work uniformly via hit.normal.
        if (!Physics.Raycast(origin, forward, out RaycastHit hit, maxGroundDistance, groundMask, QueryTriggerInteraction.Ignore))
            return;

        // Scatter is applied tangent to the surface, not in world XZ, so walls/slopes get clean scatter too.
        Vector3 scatter = Random.insideUnitSphere * scatterRadius;
        Vector3 tangentScatter = Vector3.ProjectOnPlane(scatter, hit.normal);
        Vector3 spawnPos = hit.point + tangentScatter;

        // Delay spawn by flame travel time so visible breath stream appears to cause the fire.
        // Fire-and-forget: even if breath stops, the in-flight flame still lands (per design).
        float travelDelay = (flameStreamSpeed > 0f) ? hit.distance / flameStreamSpeed : 0f;
        if (travelDelay <= 0f)
            RequestSpawnGroundFireServerRpc(spawnPos, hit.normal);
        else
            StartCoroutine(SpawnAfterDelay(spawnPos, hit.normal, travelDelay));
    }

    private IEnumerator SpawnAfterDelay(Vector3 position, Vector3 normal, float delay)
    {
        yield return new WaitForSeconds(delay);
        // Guard: dragon may have despawned or lost ownership while flame was in flight.
        if (!IsSpawned || !IsOwner) yield break;
        RequestSpawnGroundFireServerRpc(position, normal);
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
        // Host already spawned via SpawnLocally inside the ServerRpc
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
