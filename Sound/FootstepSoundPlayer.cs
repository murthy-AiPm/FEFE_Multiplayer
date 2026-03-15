using UnityEngine;
using Unity.Netcode;

// ─────────────────────────────────────────────────────────
// FootstepSoundPlayer.cs — Terrain-aware footstep sounds
//
// Attach to player/dragon/horse root. Uses a cadence timer
// (no manual animation event setup needed). Detects surface
// type via TerrainSurfaceDetector and plays matching clips.
//
// Also supports animation events if you add them later —
// just call OnFootstep() from AnimationEventRelay.
// ─────────────────────────────────────────────────────────

public class FootstepSoundPlayer : NetworkBehaviour
{
    public enum FootstepMode
    {
        CadenceTimer,       // Automatic — plays based on movement speed
        AnimationEvents,    // Manual — driven by animation event callbacks
        Both                // Animation events when available, cadence as fallback
    }

    [Header("Mode")]
    [SerializeField] private FootstepMode mode = FootstepMode.CadenceTimer;

    [Header("Cadence Timer Settings")]
    [Tooltip("Base interval between footsteps at walk speed (seconds)")]
    [SerializeField] private float baseStepInterval = 0.5f;

    [Tooltip("Interval at sprint speed")]
    [SerializeField] private float sprintStepInterval = 0.3f;

    [Tooltip("Interval when crouching")]
    [SerializeField] private float crouchStepInterval = 0.7f;

    [Tooltip("Minimum speed to trigger footsteps (m/s)")]
    [SerializeField] private float minimumSpeed = 0.5f;

    [Tooltip("Speed considered 'sprinting' for interval interpolation")]
    [SerializeField] private float sprintSpeed = 8f;

    [Tooltip("Speed threshold below which crouching interval is used")]
    [SerializeField] private float crouchSpeed = 2f;

    [Header("Surface Detection")]
    [Tooltip("How far down to raycast for surface detection")]
    [SerializeField] private float raycastDistance = 3f;

    [Tooltip("Offset from transform.position for the raycast origin")]
    [SerializeField] private Vector3 raycastOffset = Vector3.zero;

    [Header("Sound Category")]
    [Tooltip("Sound category for distance filtering (Quiet for normal footsteps)")]
    [SerializeField] private SoundCategory footstepCategory = SoundCategory.Quiet;

    [Header("Heavy Footsteps (Dragon/Horse)")]
    [Tooltip("If true, uses a louder category for bigger creatures")]
    [SerializeField] private bool isHeavyFootstep = false;

    [Tooltip("Sound category for heavy creatures (dragon, horse)")]
    [SerializeField] private SoundCategory heavyCategory = SoundCategory.Combat;

    [Header("Server-Driven (Animals/AI)")]
    [Tooltip("If true, server drives footstep timing instead of owning client. Use for AI-controlled creatures like bear, deer.")]
    [SerializeField] private bool driveFromServer = false;

    [Header("Creature Type (for footstep lookup)")]
    [Tooltip("Optional. If set, looks up 'CreatureType_SurfaceType' in SoundDatabase first (e.g. 'Bear_Snow'). Leave empty for human players.")]
    [SerializeField] private string creatureType = "";

    // ─── References (auto-found) ───
    private CharacterController _characterController;
    private Rigidbody _rigidbody;
    private AnimationEventRelay _eventRelay;

    // ─── State ───
    private float _stepTimer;
    private Vector3 _lastPosition;
    private float _currentSpeed;
    private bool _animEventFiredThisFrame;
    private bool _isCrouching;
    private string _cachedSurfaceType;
    private float _surfaceCacheTimer;
    private const float SURFACE_CACHE_DURATION = 0.2f; // re-check surface every 0.2s

    private void Awake()
    {
        _characterController = GetComponentInChildren<CharacterController>();
        _rigidbody = GetComponent<Rigidbody>();
        _eventRelay = GetComponent<AnimationEventRelay>();
        if (_eventRelay == null) _eventRelay = GetComponentInChildren<AnimationEventRelay>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        bool shouldDrive = driveFromServer ? IsServer : IsOwner;
        if (!shouldDrive) return;

        // Subscribe to animation events if available
        if (_eventRelay != null && (mode == FootstepMode.AnimationEvents || mode == FootstepMode.Both))
        {
            _eventRelay.OnFootstepLeft += OnAnimFootstep;
            _eventRelay.OnFootstepRight += OnAnimFootstep;
        }

        _lastPosition = transform.position;
    }

    public override void OnNetworkDespawn()
    {
        if (_eventRelay != null)
        {
            _eventRelay.OnFootstepLeft -= OnAnimFootstep;
            _eventRelay.OnFootstepRight -= OnAnimFootstep;
        }
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        bool shouldDrive = driveFromServer ? IsServer : IsOwner;
        if (!shouldDrive) return;
        if (ProximitySoundManager.Instance == null) return;

        // Calculate speed
        Vector3 horizontalVelocity;
        if (_characterController != null)
        {
            horizontalVelocity = _characterController.velocity;
        }
        else if (_rigidbody != null)
        {
            horizontalVelocity = _rigidbody.linearVelocity;
        }
        else
        {
            horizontalVelocity = (transform.position - _lastPosition) / Time.deltaTime;
            _lastPosition = transform.position;
        }

        horizontalVelocity.y = 0f;
        _currentSpeed = horizontalVelocity.magnitude;

        // Update surface cache
        _surfaceCacheTimer -= Time.deltaTime;
        if (_surfaceCacheTimer <= 0f)
        {
            UpdateSurfaceType();
            _surfaceCacheTimer = SURFACE_CACHE_DURATION;
        }

        // Cadence timer
        if (mode == FootstepMode.CadenceTimer || mode == FootstepMode.Both)
        {
            UpdateCadenceTimer();
        }

        _animEventFiredThisFrame = false; // reset after cadence timer so Both mode works correctly
    }

    // ─── Cadence Timer ───

    private void UpdateCadenceTimer()
    {
        if (_currentSpeed < minimumSpeed)
        {
            _stepTimer = 0f;
            return;
        }

        _stepTimer -= Time.deltaTime;

        if (_stepTimer <= 0f)
        {
            // In Both mode, skip if animation event already fired
            if (mode == FootstepMode.Both && _animEventFiredThisFrame)
            {
                _stepTimer = GetStepInterval();
                return;
            }

            PlayFootstep();
            _stepTimer = GetStepInterval();
        }
    }

    private float GetStepInterval()
    {
        if (_isCrouching) return crouchStepInterval;
        float t = Mathf.InverseLerp(minimumSpeed, sprintSpeed, _currentSpeed);
        return Mathf.Lerp(baseStepInterval, sprintStepInterval, t);
    }

    /// <summary>
    /// Call this from your controller when crouch state changes.
    /// </summary>
    public void SetCrouching(bool isCrouching)
    {
        _isCrouching = isCrouching;
    }

    // ─── Animation Event Callback ───

    private void OnAnimFootstep()
    {
        Debug.Log($"[FootstepSoundPlayer] OnAnimFootstep: mode={mode} speed={_currentSpeed}");
        if (!IsOwner) return;
        _animEventFiredThisFrame = true;

        if (mode == FootstepMode.AnimationEvents)
        {
            // In AnimationEvents mode, trust the clip entirely — no speed check
            PlayFootstep();
        }
        else if (mode == FootstepMode.Both)
        {
            // In Both mode, still guard against idle anim events firing
            if (_currentSpeed >= minimumSpeed)
                PlayFootstep();
        }
    }

    /// <summary>
    /// Public method — can be called directly from animation events on the Animator GameObject.
    /// Useful if you can't use AnimationEventRelay for some characters.
    /// </summary>
    public void OnFootstep()
    {
        OnAnimFootstep();
    }

    // ─── Surface Detection ───

    private void UpdateSurfaceType()
    {
        var db = ProximitySoundManager.Instance?.Database;
        if (db == null) return;

        Vector3 origin = transform.position + raycastOffset;
        _cachedSurfaceType = TerrainSurfaceDetector.GetSurfaceType(origin, db, raycastDistance);
    }

    // ─── Play ───

    private void PlayFootstep()
    {
        if (ProximitySoundManager.Instance == null) return;

        string surface = _cachedSurfaceType;
        if (string.IsNullOrEmpty(surface))
            surface = ProximitySoundManager.Instance.Database?.DefaultSurfaceType ?? "Grass";

        Vector3 pos = transform.position;
        ProximitySoundManager.Instance.PlayFootstep(surface, pos, isHeavyFootstep ? heavyCategory : footstepCategory, creatureType);
    }
}
