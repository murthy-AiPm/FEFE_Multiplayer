using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon flight controller — root motion flight only.
/// Thrust/Yaw/Pitch drive animator blend trees; root motion handles movement.
/// Includes ground avoidance (altitude floor) and dive crash detection.
/// </summary>
public class DragonFlightController : NetworkBehaviour
{
    [Header("Setup")]
    [SerializeField] private Rigidbody rb;

    [Header("References")]
    [SerializeField] private AnimalGroundingSystem groundingSystem;
    [SerializeField] private DragonGroundController groundController;
    [SerializeField] private AnimalGroundAlignment groundAlignment;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform cam;
    [SerializeField] private DragonSwimController swimController;

    [Header("Root Motion Flight")]
    [Tooltip("Rate thrust ramps UP per second while W is held.")]
    [SerializeField] private float thrustAccelRate = 0.5f;
    [Tooltip("Rate thrust ramps DOWN per second while S is held.")]
    [SerializeField] private float thrustDecelRate = 0.5f;
    [Tooltip("Min thrust value.")]
    [SerializeField] private float thrustMin = -1f;
    [Tooltip("Max thrust value.")]
    [SerializeField] private float thrustMax = 1f;
    [Tooltip("Smoothing for Yaw (TurnAngle). Higher = faster response.")]
    [SerializeField] private float yawSmoothing = 5f;
    [Tooltip("How fast pitch smoothly moves to target value (per second).")]
    [SerializeField] private float pitchSmoothSpeed = 3f;
    [Tooltip("How fast roll smoothly moves to target value (per second).")]
    [SerializeField] private float rollSmoothSpeed = 3f;

    [Header("Input")]
    [SerializeField] private string horizontalAxis = "Horizontal";
    [SerializeField] private KeyCode toggleHoverKey = KeyCode.Space;
    [SerializeField] private bool invertY = false;
    [SerializeField] private KeyCode pauseInputKey = KeyCode.P;
    [Tooltip("Hold to lock flight pitch to zero (fly level) for fire strafing runs.")]
    [SerializeField] private KeyCode pitchStabilizeKey = KeyCode.RightControl;
    [Tooltip("Roll left key.")]
    [SerializeField] private KeyCode rollLeftKey = KeyCode.Q;
    [Tooltip("Roll right key.")]
    [SerializeField] private KeyCode rollRightKey = KeyCode.E;

    [Header("Pitch")]
    [Tooltip("Max camera pitch angle used to normalize pitch to -1..1 range.")]
    [SerializeField] private float flightPitchClamp = 70f;

    [Header("Ground Avoidance")]
    [Tooltip("Where the downward raycast originates. If unset, uses transform.position.")]
    [SerializeField] private Transform groundAvoidanceOrigin;
    [Tooltip("Minimum distance above terrain during flight. Dragon is pushed up if closer.")]
    [SerializeField] private float minFlightAltitude = 3f;
    [Tooltip("How far down to raycast for terrain detection.")]
    [SerializeField] private float groundAvoidanceRayDistance = 20f;
    [Tooltip("Layers that count as ground for avoidance.")]
    [SerializeField] private LayerMask groundAvoidanceMask = ~0;
    [SerializeField] private bool showGroundAvoidanceDebug = false;

    [Header("Dive Crash Detection")]
    [Tooltip("Raycast origin for forward ground detection during dives. Assign to the dragon's nose/head bone.")]
    [SerializeField] private Transform noseRaycastOrigin;
    [Tooltip("_rmPitch threshold to activate dive detection. _rmPitch ranges from -1 (full nose-down) to +1 (full nose-up).")]
    [SerializeField] private float divePitchThreshold = 0.5f;
    [Tooltip("How far the nose raycast looks ahead along the nose transform's forward.")]
    [SerializeField] private float diveGroundDetectDistance = 30f;

    // ─── Public Read-Only State ──────────────────────────

    public bool IsFlightMode => isActive;
    public bool IsHoverMode => isHoverMode;
    public bool IsFlying => isFlapping;
    public bool IsFlapping => isFlapping;
    public bool IsGliding => isGliding;
    public bool IsDiving => false;
    public bool IsGrounded => groundingSystem != null && groundingSystem.IsGrounded;
    public Vector3 Velocity => currentVelocity;

    public float FlightThrust => _rmThrust;
    public float FlightYaw => _rmYaw;
    public float FlightPitch => _rmPitch;
    public float FlightRoll => _rmRoll;

    // ─── Private State ───────────────────────────────────

    private bool isActive;
    private bool hoverRequested;
    private bool isHoverMode;
    private bool isFlapping;
    private bool isGliding;

    // Root motion flight state
    private float _rmThrust;
    private float _rmYaw;
    private float _rmPitch;
    private float _rmPitchTarget;
    private float _rmRoll;
    private float _rmRollTarget;
    private bool _inputPaused;
    private bool _wasPauseMenuPaused;

    // Animator hashes
    private int thrustHash;
    private int yawHash;
    private int pitchHash;
    private int rollHash;
    private int flightModeHash;
    private int diveCrashLandHash;
    private KeyCode exitFlightKey = KeyCode.C;

    private bool _diveCrashTriggered;

    private Vector3 currentVelocity;
    private Vector3 lastPosition;

    // ─── Lifecycle ───────────────────────────────────────

    private void Awake()
    {
        if (groundingSystem == null)
            groundingSystem = GetComponentInChildren<AnimalGroundingSystem>();
        if (groundController == null)
            groundController = GetComponent<DragonGroundController>();
        if (groundAlignment == null)
            groundAlignment = GetComponent<AnimalGroundAlignment>();
        if (animator == null)
            animator = GetComponentInParent<Animator>();
        if (cam == null)
            cam = Camera.main?.transform;
        if (swimController == null)
            swimController = GetComponentInParent<DragonSwimController>();
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        thrustHash        = Animator.StringToHash("Thrust");
        yawHash           = Animator.StringToHash("Yaw");
        pitchHash         = Animator.StringToHash("Pitch");
        rollHash          = Animator.StringToHash("Roll");
        flightModeHash    = Animator.StringToHash("FlightMode");
        diveCrashLandHash = Animator.StringToHash("DiveCrashLand");

        isActive = false;
        isHoverMode = false;
        hoverRequested = false;
        isFlapping = false;
        isGliding = false;
        _diveCrashTriggered = false;

        lastPosition = transform.position;

        if (rb != null) rb.useGravity = false;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Pause menu (Esc): zero thrust/pitch/yaw on entry so the dragon hovers
        // in place while paused. Animator stays running so the blend tree can
        // transition to the glide/hover pose naturally.
        bool menuPaused = PauseMenu.IsPaused;
        if (menuPaused && !_wasPauseMenuPaused)
        {
            _rmThrust = 0f;
            _rmPitch = 0f;
            _rmPitchTarget = 0f;
            _rmYaw = 0f;
            if (animator != null)
            {
                animator.SetFloat(thrustHash, 0f);
                animator.SetFloat(pitchHash, 0f);
                animator.SetFloat(yawHash, 0f);
            }
        }
        _wasPauseMenuPaused = menuPaused;

        if (menuPaused) return;

        // Not in flight mode
        if (!isActive)
        {
            // Reset crash flag once grounded so it can trigger again next fall
            if (groundingSystem != null && groundingSystem.IsGrounded)
                _diveCrashTriggered = false;

            // Re-enter flight from free-fall (C key) — not while swimming or grounded
            if (Input.GetKeyDown(exitFlightKey) && !IsSwimmingActive()
                && groundingSystem != null && !groundingSystem.IsGrounded)
            {
                EnterFlight();
                return;
            }

            CheckFreeFallCrash();
            return;
        }

        // Exit flight (C key)
        if (Input.GetKeyDown(exitFlightKey))
        {
            ExitFlight();
            return;
        }

        UpdateRootMotionFlight(Time.deltaTime);

        EnforceMinAltitude();
        CheckDiveCrash();

        currentVelocity = rb != null ? rb.linearVelocity : (transform.position - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;
    }

    // ─── Public API ──────────────────────────────────────

    /// <summary>
    /// Enter flight mode. Called from ground takeoff and from free-fall re-entry.
    /// Levels the dragon, suspends ground alignment, starts at full thrust.
    /// </summary>
    public void EnterFlight()
    {
        // Don't enter flight while swimming
        if (IsSwimmingActive()) return;
        isActive = true;
        isHoverMode = false;
        hoverRequested = false;
        _rmThrust = 1f;

        // Suspend ground alignment so slope tilt doesn't carry into flight
        if (groundAlignment != null)
            groundAlignment.SuspendAlignment = true;

        // Level the dragon — clear any slope tilt or free-fall rotation
        float yaw = transform.eulerAngles.y;
        ApplyRotation(Quaternion.Euler(0f, yaw, 0f));

        // Kill any falling velocity so flight starts clean
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Reset grounding so IsFalling doesn't fight FlightMode
        if (groundingSystem != null)
            groundingSystem.ResetFallingState();

        // Set FlightMode animator param
        if (animator != null)
            animator.SetBool(flightModeHash, true);
    }

    /// <summary>
    /// Exit flight mode. Dragon will fall to ground via gravity.
    /// </summary>
    public void ExitFlight()
    {
        isActive = false;
        isHoverMode = false;
        hoverRequested = false;
        isFlapping = false;
        isGliding = false;

        // Reset root motion flight params
        _rmThrust = 0f;
        _rmYaw = 0f;
        _rmPitch = 0f;
        _rmPitchTarget = 0f;
        _rmRoll = 0f;
        _rmRollTarget = 0f;

        // Clear animator flight params
        if (animator != null)
        {
            animator.SetBool(flightModeHash, false);
            animator.SetFloat(thrustHash, 0f);
            animator.SetFloat(yawHash, 0f);
            animator.SetFloat(pitchHash, 0f);
            animator.SetFloat(rollHash, 0f);
            animator.applyRootMotion = true;
        }

        // Clear ground system flags
        if (groundController != null)
            groundController.FlightRootMotionActive = false;
        if (groundAlignment != null)
        {
            groundAlignment.SuspendAlignment = false;
            groundAlignment.SetYawImmediate(transform.eulerAngles.y);
        }

        // Kill velocity so dragon drops cleanly
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    // ─── Root Motion Flight ─────────────────────────────

    private void UpdateRootMotionFlight(float dt)
    {
        if (groundController == null) return;

        if (Input.GetKeyDown(pauseInputKey))
        {
            _inputPaused = !_inputPaused;
            animator.speed = _inputPaused ? 0f : 1f;
        }

        if (_inputPaused) return;

        float horizontal = Input.GetAxisRaw(horizontalAxis);

        // ── Thrust (hold-to-ramp: W accelerates, S decelerates, release freezes value) ──
        if (Input.GetKey(KeyCode.W))
            _rmThrust += thrustAccelRate * dt;
        if (Input.GetKey(KeyCode.S))
            _rmThrust -= thrustDecelRate * dt;
        _rmThrust = Mathf.Clamp(_rmThrust, thrustMin, thrustMax);

        // ── Yaw ──
        float targetYaw = Mathf.Clamp(horizontal, -1f, 1f);
        _rmYaw = Mathf.MoveTowards(_rmYaw, targetYaw, yawSmoothing * dt);
        if (Mathf.Abs(targetYaw) < 0.01f && Mathf.Abs(_rmYaw) < 0.02f)
            _rmYaw = 0f;

        // ── Pitch (driven from camera angle — dragon follows where camera looks) ──
        if (Input.GetKey(pitchStabilizeKey))
        {
            // Pitch stabilized — fly level, ignore camera pitch
            _rmPitchTarget = 0f;
        }
        else if (cam != null)
        {
            float camPitch = cam.eulerAngles.x;
            if (camPitch > 180f) camPitch -= 360f;
            float pitchSign = invertY ? 1f : -1f;
            _rmPitchTarget = Mathf.Clamp(pitchSign * camPitch / flightPitchClamp, -1f, 1f);
        }
        _rmPitch = Mathf.MoveTowards(_rmPitch, _rmPitchTarget, pitchSmoothSpeed * dt);

        // ── Roll (Q = left, E = right; smoothly returns to 0 when released) ──
        if (Input.GetKey(rollLeftKey))
            _rmRollTarget = -1f;
        else if (Input.GetKey(rollRightKey))
            _rmRollTarget = 1f;
        else
            _rmRollTarget = 0f;
        _rmRoll = Mathf.MoveTowards(_rmRoll, _rmRollTarget, rollSmoothSpeed * dt);

        // Tell ground systems to hand off to flight
        if (groundController != null)
            groundController.FlightRootMotionActive = true;
        if (groundAlignment != null)
            groundAlignment.SuspendAlignment = true;

        // Write directly to animator
        if (animator != null)
        {
            animator.SetFloat(thrustHash, _rmThrust);
            animator.SetFloat(yawHash, _rmYaw);
            animator.SetFloat(pitchHash, _rmPitch);
            animator.SetFloat(rollHash, _rmRoll);
            animator.applyRootMotion = true;
        }

        // Hover toggle
        if (Input.GetKeyDown(toggleHoverKey))
        {
            hoverRequested = !hoverRequested;
            if (hoverRequested && rb != null)
            {
                Vector3 v = rb.linearVelocity;
                v.y = 0f;
                rb.linearVelocity = v;
            }
        }

        isHoverMode = hoverRequested && _rmThrust < 0.05f;
        isFlapping = _rmThrust > 0.05f;
        isGliding = !isHoverMode && !isFlapping;
    }

    // ─── Free-Fall Crash Detection ───────────────────

    /// <summary>
    /// Runs when NOT in flight mode. If the grounding system says IsFalling,
    /// fires the nose raycast along nose forward. If ground is detected,
    /// triggers DiveCrashLand animation.
    /// </summary>
    private void CheckFreeFallCrash()
    {
        if (noseRaycastOrigin == null) return;
        if (_diveCrashTriggered) return;
        if (groundingSystem == null || !groundingSystem.IsFalling) return;

        Vector3 origin = noseRaycastOrigin.position;
        Vector3 direction = noseRaycastOrigin.forward;

        if (Physics.Raycast(origin, direction, out RaycastHit hit,
            diveGroundDetectDistance, groundAvoidanceMask))
        {
            if (showGroundAvoidanceDebug)
                Debug.DrawLine(origin, hit.point, Color.blue);

            if (animator != null)
                DiveCrashLandServerRpc();

            _diveCrashTriggered = true;
        }
        else if (showGroundAvoidanceDebug)
        {
            Debug.DrawRay(origin, direction * diveGroundDetectDistance, Color.blue);
        }
    }

    // ─── Dive Crash Detection ─────────────────────────

    /// <summary>
    /// When the dragon is diving (_rmPitch below -divePitchThreshold),
    /// fires a raycast from the nose along the nose transform's forward.
    /// If ground is detected, forces a crash landing.
    /// </summary>
    private void CheckDiveCrash()
    {
        if (noseRaycastOrigin == null) return;

        // _rmPitch: -1 = full nose-down, +1 = full nose-up
        if (_rmPitch > -divePitchThreshold) return;

        Vector3 origin = noseRaycastOrigin.position;
        Vector3 direction = noseRaycastOrigin.forward;

        if (Physics.Raycast(origin, direction, out RaycastHit hit,
            diveGroundDetectDistance, groundAvoidanceMask))
        {
            if (showGroundAvoidanceDebug)
                Debug.DrawLine(origin, hit.point, Color.blue);

            OnDiveCrashDetected(hit);
        }
        else if (showGroundAvoidanceDebug)
        {
            Debug.DrawRay(origin, direction * diveGroundDetectDistance, Color.blue);
        }
    }

    /// <summary>
    /// Called when the nose raycast detects ground during a dive.
    /// Fires the DiveCrashLand trigger then exits flight mode.
    /// </summary>
    private void OnDiveCrashDetected(RaycastHit hit)
    {
        DiveCrashLandServerRpc();
        ExitFlight();
    }

    [ServerRpc]
    private void DiveCrashLandServerRpc()
    {
        DiveCrashLandClientRpc();
    }

    [ClientRpc]
    private void DiveCrashLandClientRpc()
    {
        if (animator != null)
            animator.SetTrigger(diveCrashLandHash);
    }

    // ─── Ground Avoidance ─────────────────────────────

    /// <summary>
    /// Pushes the dragon upward if it's too close to the terrain.
    /// Instant snap — no smoothing so fast dives can't outrun the correction.
    /// No rotation change. Camera stays unaffected.
    /// </summary>
    private void EnforceMinAltitude()
    {
        Vector3 origin = groundAvoidanceOrigin != null ? groundAvoidanceOrigin.position : transform.position;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
            groundAvoidanceRayDistance, groundAvoidanceMask))
        {
            float currentAltitude = hit.distance;

            if (showGroundAvoidanceDebug)
            {
                Debug.DrawLine(origin, hit.point, Color.green);
                Debug.DrawLine(origin, origin + Vector3.down * minFlightAltitude, Color.yellow);
            }

            if (currentAltitude < minFlightAltitude)
            {
                float pushUp = minFlightAltitude - currentAltitude;
                ApplyMovement(Vector3.up * pushUp);
            }
        }
        else if (showGroundAvoidanceDebug)
        {
            Debug.DrawRay(origin, Vector3.down * groundAvoidanceRayDistance, Color.red);
        }
    }

    // ─── Helpers ─────────────────────────────────────────

    private bool IsSwimmingActive()
    {
        return swimController != null && swimController.IsSwimming;
    }

    private void ApplyMovement(Vector3 displacement)
    {
        if (rb != null)
            rb.MovePosition(rb.position + displacement);
        else
            transform.position += displacement;
    }

    private void ApplyRotation(Quaternion targetRotation)
    {
        if (rb != null)
            rb.MoveRotation(targetRotation);
        else
            transform.rotation = targetRotation;
    }

    private static float NormalizePitch(float xDegrees)
    {
        while (xDegrees > 180f) xDegrees -= 360f;
        while (xDegrees < -180f) xDegrees += 360f;
        return xDegrees;
    }
}