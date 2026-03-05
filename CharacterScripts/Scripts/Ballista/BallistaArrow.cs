using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Ballista arrow projectile.
/// Spawned by BallistaController on server.
/// Travels straight, deals damage on impact via DamageReceiver.
/// Despawns after max range or on hit.
/// </summary>
public class BallistaArrow : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float damage = 50f;
    [SerializeField] private float maxRange = 200f;
    [SerializeField] private float stickDuration = 5f; // how long before despawn after sticking
    [SerializeField] private float despawnDelay = 0.2f; // small delay so clients see impact

    [Header("Effects")]
    [SerializeField] private GameObject impactEffectPrefab; // optional VFX on hit

    private Vector3 _startPosition;
    private bool _hasHit;
    private Collider _collider;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _startPosition = transform.position;

        // Disable collider briefly so arrow clears its spawn point
        if (_collider != null)
        {
            _collider.enabled = false;
            Invoke(nameof(EnableCollider), 0.1f);
        }
    }

    private void EnableCollider()
    {
        if (_collider != null)
            _collider.enabled = true;
    }

    private void Update()
    {
        if (_hasHit) return;

        // Despawn if exceeded max range
        float distTravelled = Vector3.Distance(_startPosition, transform.position);
        if (distTravelled >= maxRange)
        {
            if (IsServer) DespawnArrow();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[BallistaArrow] Hit: {other.gameObject.name}");
        if (!IsServer) return;
        if (_hasHit) return;

        // Ignore other ballista arrows (check self and parent)
        if (other.GetComponentInParent<BallistaArrow>() != null) return;

        // Ignore the shooter (only skip owner check for players, not NPCs like bears)
        var hitNetObj = other.GetComponentInParent<NetworkObject>();
        bool isPlayer = hitNetObj != null && other.GetComponentInParent<CombatController>() != null;
        if (isPlayer && hitNetObj.OwnerClientId == OwnerClientId) return;
        _hasHit = true;
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }


        // Try to deal damage
        var vitalManager = other.GetComponentInParent<VitalManager>();
        if (vitalManager != null)
        {
            vitalManager.ApplyDamage("health", damage);
            Debug.Log($"[BallistaArrow] Applied {damage} damage to {vitalManager.gameObject.name}");
        }

        // Notify impact effects on clients
        NotifyImpactClientRpc(transform.position, transform.rotation);

        // Stick into surface
        StickArrowClientRpc(transform.position, transform.rotation);

        // Despawn after stick duration
        Invoke(nameof(DespawnArrow), stickDuration);
    }

    private void DespawnArrow()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    [ClientRpc]
    private void NotifyImpactClientRpc(Vector3 position, Quaternion rotation)
    {
        if (impactEffectPrefab != null)
            Instantiate(impactEffectPrefab, position, rotation);
    }
    [ClientRpc]
    private void StickArrowClientRpc(Vector3 position, Quaternion rotation)
    {
        // Stop physics and freeze in place
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
        transform.position = position;
        transform.rotation = rotation;
    }
}