using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Deals damage-over-time to anything inside the dragon's fire breath cone.
/// Runs a periodic server-side overlap check while IsBreathingFire is true.
/// Targets must have a DamageReceiver component to take damage.
///
/// Setup:
///   1. Add to dragon prefab
///   2. Assign fireOrigin (mouth bone) and combatController
///   3. Tweak damagePerSecond, tickRate, coneAngle, coneRange in Inspector
/// </summary>
public class DragonFireBreathDamage : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonCombatController combatController;
    [Tooltip("Where the fire cone originates (mouth bone / fire breath spawn point).")]
    [SerializeField] private Transform fireOrigin;
    [Tooltip("Camera transform — cone direction follows where the player is aiming.")]
    [SerializeField] private Transform cam;

    [Header("Damage")]
    [Tooltip("Total damage applied per second while a target is inside the cone.")]
    [SerializeField] private float damagePerSecond = 20f;
    [Tooltip("How many times per second damage is applied.")]
    [SerializeField] private float tickRate = 4f;

    [Header("Cone Shape")]
    [Tooltip("Half-angle of the fire cone in degrees.")]
    [SerializeField] private float coneAngle = 30f;
    [Tooltip("How far the fire cone reaches.")]
    [SerializeField] private float coneRange = 15f;

    [Header("Detection")]
    [SerializeField] private LayerMask hitLayers = ~0;
    [Tooltip("Max targets detected per tick.")]
    [SerializeField] private int maxTargetsPerTick = 20;

    [Header("Debug")]
    [SerializeField] private bool debugLogging;
    [SerializeField] private bool drawGizmos = true;

    // ─── State ───────────────────────────────────────────
    private float _tickTimer;
    private float _tickInterval;
    private float _damagePerTick;
    private Collider[] _overlapBuffer;
    private HashSet<int> _hitThisTick = new HashSet<int>();
    private NetworkObject _ownerNetObj;

    private void Awake()
    {
        if (combatController == null)
            combatController = GetComponentInParent<DragonCombatController>();
        if (cam == null)
            cam = Camera.main?.transform;

        _overlapBuffer = new Collider[maxTargetsPerTick];
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
        if (combatController == null || !combatController.IsBreathingFire) return;

        _tickTimer += Time.deltaTime;
        if (_tickTimer >= _tickInterval)
        {
            _tickTimer -= _tickInterval;
            DealFireDamage();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DAMAGE TICK
    // ═══════════════════════════════════════════════════════════════

    private void DealFireDamage()
    {
        if (fireOrigin == null) return;

        Vector3 origin = fireOrigin.position;
        Vector3 forward = cam != null ? cam.forward : fireOrigin.forward;

        // Overlap sphere at the midpoint of the cone for best coverage
        int count = Physics.OverlapSphereNonAlloc(origin, coneRange, _overlapBuffer, hitLayers, QueryTriggerInteraction.Ignore);

        _hitThisTick.Clear();

        for (int i = 0; i < count; i++)
        {
            var col = _overlapBuffer[i];
            if (col == null) continue;

            // Resolve root object to avoid hitting the same character twice
            var rootObj = col.attachedRigidbody != null
                ? col.attachedRigidbody.gameObject
                : col.gameObject;

            int rootId = rootObj.GetInstanceID();
            if (_hitThisTick.Contains(rootId)) continue;

            // Skip self
            var hitNetObj = col.GetComponentInParent<NetworkObject>();
            if (hitNetObj != null && hitNetObj == _ownerNetObj) continue;

            // Cone angle check
            Vector3 toTarget = (col.ClosestPoint(origin) - origin).normalized;
            float angle = Vector3.Angle(forward, toTarget);
            if (angle > coneAngle) continue;

            // Find DamageReceiver
            var receiver = col.GetComponentInParent<DamageReceiver>();
            if (receiver == null) continue;

            _hitThisTick.Add(rootId);

            // Apply damage through server-authoritative pipeline
            receiver.ApplyProjectileDamage(_damagePerTick, origin);

            if (debugLogging)
                Debug.Log($"[DragonFire] Hit {rootObj.name} for {_damagePerTick} damage");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private void RecalculateTickValues()
    {
        _tickInterval = tickRate > 0f ? 1f / tickRate : 0.25f;
        _damagePerTick = damagePerSecond * _tickInterval;
    }

    private void OnValidate()
    {
        RecalculateTickValues();
    }

    // ═══════════════════════════════════════════════════════════════
    // GIZMOS
    // ═══════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || fireOrigin == null) return;

        Gizmos.color = combatController != null && combatController.IsBreathingFire
            ? new Color(1f, 0.3f, 0f, 0.4f)
            : new Color(1f, 0.8f, 0f, 0.2f);

        Vector3 origin = fireOrigin.position;
        Vector3 forward = cam != null ? cam.forward : fireOrigin.forward;
        Vector3 up = cam != null ? cam.up : Vector3.up;
        Vector3 right = cam != null ? cam.right : Vector3.right;

        // Draw cone edges
        Quaternion leftRot = Quaternion.AngleAxis(-coneAngle, up);
        Quaternion rightRot = Quaternion.AngleAxis(coneAngle, up);
        Quaternion upRot = Quaternion.AngleAxis(-coneAngle, right);
        Quaternion downRot = Quaternion.AngleAxis(coneAngle, right);

        Gizmos.DrawRay(origin, leftRot * forward * coneRange);
        Gizmos.DrawRay(origin, rightRot * forward * coneRange);
        Gizmos.DrawRay(origin, upRot * forward * coneRange);
        Gizmos.DrawRay(origin, downRot * forward * coneRange);
        Gizmos.DrawRay(origin, forward * coneRange);

        // Draw range sphere wireframe
        Gizmos.DrawWireSphere(origin, coneRange);
    }
}
