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

    [Header("Footstep Mode")]
    [Tooltip("If true, footsteps are driven by animation events (HorseFootStep). If false, uses cadence timer.")]
    [SerializeField] private bool useAnimationEvents = false;

    [Header("Gallop Settings")]
    [Tooltip("Base interval between hoofbeats at trot speed")]
    [SerializeField] private float trotInterval = 0.45f;

    [Tooltip("Interval between hoofbeats at gallop speed")]
    [SerializeField] private float gallopInterval = 0.25f;

    [Tooltip("Minimum speed to produce hoofbeat sounds")]
    [SerializeField] private float minSpeed = 1f;

    [Tooltip("Speed considered galloping")]
    [SerializeField] private float gallopSpeed = 12f;

    [Header("Footstep Category")]
    [Tooltip("Sound category for hoofbeat sounds. Use Combat for louder hoofbeats heard from further away.")]
    [SerializeField] private SoundCategory hoofbeatCategory = SoundCategory.Combat;

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
    private Vector3 _lastFixedPosition;
    private float _currentSpeed;
    private float _smoothedSpeed;
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
        _lastFixedPosition = transform.position;
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (ProximitySoundManager.Instance == null) return;

        UpdateSpeed();
        UpdateSurface();
        UpdateHoofbeats();
        UpdateIdleSounds();
        UpdateMountStateTransitions();
    }

    private void FixedUpdate()
    {
        // Sample speed in FixedUpdate to match when rb.MovePosition actually runs
        Vector3 delta = transform.position - _lastFixedPosition;
        delta.y = 0f;
        _currentSpeed = delta.magnitude / Mathf.Max(Time.fixedDeltaTime, 0.001f);
        _lastFixedPosition = transform.position;

        // Smooth to avoid rapid flicker between zero and real speed
        _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, _currentSpeed, 0.3f);
    }

    private void UpdateSpeed()
    {
        // Speed is now sampled in FixedUpdate — nothing to do here
        // Using _smoothedSpeed in UpdateHoofbeats prevents timer reset flicker
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
        if (useAnimationEvents) return;

        if (_smoothedSpeed < minSpeed)
        {
            // Don't reset timer to 0 — avoids immediate fire when movement resumes
            _hoofbeatTimer = Mathf.Max(_hoofbeatTimer, 0.1f);
            return;
        }

        _hoofbeatTimer -= Time.deltaTime;
        if (_hoofbeatTimer <= 0f)
        {//
            // Play hoofbeat as a footstep (terrain-aware)
            string surface = _cachedSurfaceType ?? "Grass";
            ProximitySoundManager.Instance.PlayFootstep(surface, transform.position, hoofbeatCategory, "Horse");

            float t = Mathf.InverseLerp(minSpeed, gallopSpeed, _smoothedSpeed);
            _hoofbeatTimer = Mathf.Lerp(trotInterval, gallopInterval, t);
        }
    }

    private void UpdateIdleSounds()
    {
        // Only snort when idle and not mounted (or when mounted and idle)
        if (_smoothedSpeed > minSpeed)
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
    /// Called from animation event named 'HorseFootStep'.
    /// </summary>
    public void HorseFootStep()
    {
        if (ProximitySoundManager.Instance == null) return;
        string surface = _cachedSurfaceType ?? "Grass";
        ProximitySoundManager.Instance.PlayFootstep(surface, transform.position, hoofbeatCategory, "Horse");
    }

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
