using UnityEngine;
using Unity.Netcode;

// ─────────────────────────────────────────────────────────
// HorseSoundPlayer.cs — Horse-specific sounds
//
// Attach to horse prefab. Handles:
// - Gallop (terrain-aware, cadence timer based on speed)
// - Neigh on mount/startle
// - Idle snort (occasional)
// - Death sound
//
// Gallop footsteps are terrain-aware just like player footsteps.
// ─────────────────────────────────────────────────────────

public class HorseSoundPlayer : NetworkBehaviour
{
    [Header("Sound Names (must match SoundDatabase)")]
    [SerializeField] private string neighSound = "Horse_Neigh";
    [SerializeField] private string snortSound = "Horse_Snort";
    [SerializeField] private string deathSound = "Horse_Death";
    [SerializeField] private string mountSound = "Horse_Mount";
    [SerializeField] private string dismountSound = "Horse_Dismount";

    [Header("Gallop Settings")]
    [Tooltip("Base interval between hoofbeats at trot speed")]
    [SerializeField] private float trotInterval = 0.45f;

    [Tooltip("Interval between hoofbeats at gallop speed")]
    [SerializeField] private float gallopInterval = 0.25f;

    [Tooltip("Minimum speed to produce hoofbeat sounds")]
    [SerializeField] private float minSpeed = 1f;

    [Tooltip("Speed considered galloping")]
    [SerializeField] private float gallopSpeed = 12f;

    [Header("Idle Sounds")]
    [Tooltip("Random interval range for idle snort (seconds)")]
    [SerializeField] private float idleSnortMinInterval = 8f;
    [SerializeField] private float idleSnortMaxInterval = 20f;

    [Header("Surface Detection")]
    [SerializeField] private float raycastDistance = 3f;

    // ─── References ───
    private MountableEntity _mountable;
    private Rigidbody _rigidbody;

    // ─── State ───
    private float _hoofbeatTimer;
    private float _idleSnortTimer;
    private Vector3 _lastPosition;
    private float _currentSpeed;
    private string _cachedSurfaceType;
    private float _surfaceCacheTimer;
    private bool _wasMounted;

    private void Awake()
    {
        _mountable = GetComponent<MountableEntity>();
        _rigidbody = GetComponent<Rigidbody>();
        ResetIdleTimer();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _lastPosition = transform.position;
    }

    private void Update()
    {
        // Horse sounds run on all clients based on observed state
        // (no need for owner-only since horse movement is server-synced)
        if (ProximitySoundManager.Instance == null) return;

        UpdateSpeed();
        UpdateSurface();
        UpdateHoofbeats();
        UpdateIdleSounds();
        UpdateMountStateTransitions();
    }

    private void UpdateSpeed()
    {
        if (_rigidbody != null)
        {
            Vector3 vel = _rigidbody.linearVelocity;
            vel.y = 0f;
            _currentSpeed = vel.magnitude;
        }
        else
        {
            Vector3 delta = transform.position - _lastPosition;
            delta.y = 0f;
            _currentSpeed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
            _lastPosition = transform.position;
        }
    }

    private void UpdateSurface()
    {
        _surfaceCacheTimer -= Time.deltaTime;
        if (_surfaceCacheTimer <= 0f)
        {
            var db = ProximitySoundManager.Instance?.Database;
            if (db != null)
                _cachedSurfaceType = TerrainSurfaceDetector.GetSurfaceType(transform.position, db, raycastDistance);
            _surfaceCacheTimer = 0.25f;
        }
    }

    private void UpdateHoofbeats()
    {
        if (_currentSpeed < minSpeed)
        {
            _hoofbeatTimer = 0f;
            return;
        }

        _hoofbeatTimer -= Time.deltaTime;
        if (_hoofbeatTimer <= 0f)
        {
            // Play hoofbeat as a footstep (terrain-aware)
            string surface = _cachedSurfaceType ?? "Grass";
            ProximitySoundManager.Instance.PlayFootstep(surface, transform.position, SoundCategory.Quiet);

            float t = Mathf.InverseLerp(minSpeed, gallopSpeed, _currentSpeed);
            _hoofbeatTimer = Mathf.Lerp(trotInterval, gallopInterval, t);
        }
    }

    private void UpdateIdleSounds()
    {
        // Only snort when idle and not mounted (or when mounted and idle)
        if (_currentSpeed > minSpeed)
        {
            ResetIdleTimer();
            return;
        }

        _idleSnortTimer -= Time.deltaTime;
        if (_idleSnortTimer <= 0f)
        {
            ProximitySoundManager.Instance.PlaySound(snortSound, transform.position);
            ResetIdleTimer();
        }
    }

    private void UpdateMountStateTransitions()
    {
        if (_mountable == null) return;

        bool isMounted = _mountable.IsMounted;

        if (isMounted && !_wasMounted)
        {
            // Just mounted
            ProximitySoundManager.Instance.PlaySound(mountSound, transform.position);
            ProximitySoundManager.Instance.PlaySound(neighSound, transform.position);
        }
        else if (!isMounted && _wasMounted)
        {
            // Just dismounted
            ProximitySoundManager.Instance.PlaySound(dismountSound, transform.position);
        }

        _wasMounted = isMounted;
    }

    private void ResetIdleTimer()
    {
        _idleSnortTimer = Random.Range(idleSnortMinInterval, idleSnortMaxInterval);
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Call when horse takes damage or is startled.
    /// </summary>
    public void OnStartle()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(neighSound, transform.position);
    }

    /// <summary>
    /// Call when horse dies (e.g. eaten by dragon).
    /// </summary>
    public void OnDeath()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(deathSound, transform.position);
    }
}
