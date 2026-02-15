using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach to each melee weapon's visual prefab (or on a child of the weapon bone).
/// Enabled/disabled by animation events to detect hits during attack windows.
/// 
/// Uses SphereCast along the weapon's length for reliable hit detection.
/// Reports hits to CombatNetworkHandler for server-authoritative damage.
/// 
/// Setup:
///   1. Add this component to the weapon prefab
///   2. Configure length/radius to match the weapon mesh
///   3. In animation clips, add events calling OnAnimEvent_HitboxEnable / OnAnimEvent_HitboxDisable
/// </summary>
public class HitboxController : MonoBehaviour
{
    [Header("Hitbox Shape")]
    [SerializeField] private float length = 1f;
    [SerializeField] private float radius = 0.15f;
    [SerializeField] private Vector3 offset = Vector3.zero;

    [Header("Detection")]
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private int maxHitsPerSwing = 5;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color activeColor = Color.red;
    [SerializeField] private Color inactiveColor = Color.yellow;

    // State
    private bool _active;
    private HashSet<Collider> _alreadyHit = new HashSet<Collider>();
    private RaycastHit[] _hitBuffer;

    // Owner reference (set by WeaponManager when weapon is spawned)
    private NetworkObject _ownerNetObj;
    private WeaponData _weaponData;

    // Attack context (set externally per swing)
    private int _currentComboIndex;
    private bool _isHeavyAttack;

    public bool IsActive => _active;

    // Events
    public System.Action<HitInfo> OnHitDetected;

    private void Awake()
    {
        _hitBuffer = new RaycastHit[maxHitsPerSwing];
    }

    /// <summary>
    /// Call after weapon is instantiated and attached to a character.
    /// </summary>
    public void Initialize(NetworkObject owner, WeaponData weapon)
    {
        _ownerNetObj = owner;
        _weaponData = weapon;

        // Override shape from weapon data if available
        if (weapon != null)
        {
            length = weapon.hitboxLength;
            radius = weapon.hitboxRadius;
            offset = weapon.hitboxOffset;
        }
    }

    /// <summary>
    /// Set attack context before enabling hitbox (called by AnimationEventRelay or driver).
    /// </summary>
    public void SetAttackContext(int comboIndex, bool isHeavy)
    {
        _currentComboIndex = comboIndex;
        _isHeavyAttack = isHeavy;
    }

    private void FixedUpdate()
    {
        if (!_active) return;

        DetectHits();
    }

    // ─── Activation (called by animation events on the CHARACTER, forwarded here) ───

    public void EnableHitbox()
    {
        _active = true;
        _alreadyHit.Clear();
    }

    public void DisableHitbox()
    {
        _active = false;
        _alreadyHit.Clear();
    }

    // ─── Detection ───

    private void DetectHits()
    {
        Vector3 origin = transform.TransformPoint(offset);
        Vector3 direction = transform.up; // weapon points along local Y typically
        // Adjust if your weapon mesh points along Z:
        // Vector3 direction = transform.forward;

        int hitCount = Physics.SphereCastNonAlloc(
            origin, radius, direction, _hitBuffer, length, hitLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            var hit = _hitBuffer[i];
            if (hit.collider == null) continue;

            // Skip self
            if (_ownerNetObj != null)
            {
                var hitNetObj = hit.collider.GetComponentInParent<NetworkObject>();
                if (hitNetObj != null && hitNetObj == _ownerNetObj)
                    continue;
            }

            // Skip already hit this swing
            if (_alreadyHit.Contains(hit.collider))
                continue;

            _alreadyHit.Add(hit.collider);

            // Build hit info
            var hitInfo = new HitInfo
            {
                hitCollider = hit.collider,
                hitPoint = hit.point,
                hitNormal = hit.normal,
                attackerNetObj = _ownerNetObj,
                weaponData = _weaponData,
                comboIndex = _currentComboIndex,
                isHeavy = _isHeavyAttack
            };

            OnHitDetected?.Invoke(hitInfo);

            // Also check for DamageReceiver on the hit object
            var receiver = hit.collider.GetComponentInParent<DamageReceiver>();
            if (receiver != null)
            {
                receiver.OnHitLocal(hitInfo);
            }
        }
    }

    // ─── Gizmos ───

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = _active ? activeColor : inactiveColor;

        Vector3 origin = transform.TransformPoint(offset);
        Vector3 end = origin + transform.up * length;

        Gizmos.DrawWireSphere(origin, radius);
        Gizmos.DrawWireSphere(end, radius);
        Gizmos.DrawLine(origin + Vector3.left * radius, end + Vector3.left * radius);
        Gizmos.DrawLine(origin + Vector3.right * radius, end + Vector3.right * radius);
        Gizmos.DrawLine(origin + Vector3.forward * radius, end + Vector3.forward * radius);
        Gizmos.DrawLine(origin + Vector3.back * radius, end + Vector3.back * radius);
    }
}

/// <summary>
/// Data packet describing a hit. Passed from HitboxController to DamageReceiver.
/// </summary>
[System.Serializable]
public struct HitInfo
{
    public Collider hitCollider;
    public Vector3 hitPoint;
    public Vector3 hitNormal;
    public NetworkObject attackerNetObj;
    public WeaponData weaponData;
    public int comboIndex;
    public bool isHeavy;

    public float GetDamage()
    {
        if (weaponData == null) return 5f; // fist fallback
        return isHeavy ? weaponData.baseDamage * 2f : weaponData.baseDamage;
    }
}