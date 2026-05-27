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
    [Tooltip("Optional — when set, FlightThrust is clamped to MaxFlightThrust at zero stamina, and EnterFlight is blocked when wings are broken.")]
    [SerializeField] private DragonStaminaController staminaController;
    [Tooltip("Optional — adds speed only when the animated wings are actually flapping.")]
    [SerializeField] private DragonWingActivityTracker wingActivityTracker;

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

    [Header("Input")]
    [SerializeField] private string horizontalAxis = "Horizontal";
    [SerializeField] private bool invertY = false;
    [SerializeField] private KeyCode pauseInputKey = KeyCode.P;
    [Tooltip("Hold to lock flight pitch to zero (fly level) for fire strafing runs.")]
    [SerializeField] private KeyCode pitchStabilizeKey = KeyCode.RightControl;
    [Tooltip("Roll left key.")]
    [SerializeField] private KeyCode rollLeftKey = KeyCode.Q;
    [Tooltip("Roll right key.")]
    [SerializeField] private KeyCode rollRightKey = KeyCode.E;
    [Tooltip("Hold to ascend while hovering (thrust ~ 0).")]
    [SerializeField] private KeyCode hoverUpKey = KeyCode.Space;
    [Tooltip("Hold to descend while hovering (thrust ~ 0).")]
    [SerializeField] private KeyCode hoverDownKey = KeyCode.LeftControl;
    [Tooltip("Vertical speed (m/s) applied while hover up/down keys are held.")]
    [SerializeField] private float hoverVerticalSpeed = 4f;

    [Header("Flight Speed Assist")]
    [Tooltip("Extra forward speed (m/s) at full positive thrust. This is added on top of root motion so high-thrust clips feel faster than glide clips.")]
    [SerializeField] private float highThrustBonusSpeed = 8f;
    [Tooltip("Thrust must exceed this value before high-thrust bonus speed starts blending in.")]
    [SerializeField] private float highThrustBonusStart = 0.35f;
    [Tooltip("Extra speed (m/s) at full wing activity. Only applies when DragonWingActivityTracker says the wings are flapping.")]
    [SerializeField] private float wingFlapBonusSpeed = 5f;
    [Tooltip("Wing activity, in deg/sec, that maps to full wing-flap bonus speed.")]
    [SerializeField] private float wingFlapFullActivity = 180f;
    [Tooltip("If true, nose-down pitch adds speed and nose-up pitch subtracts speed from the root-motion assist.")]
    [SerializeField] private bool usePitchSpeedModifier = true;
    [Tooltip("Extra speed (m/s) added at full nose-down pitch (_rmPitch = -1).")]
    [SerializeField] private float divePitchBonusSpeed = 6f;
    [Tooltip("Speed (m/s) subtracted at full nose-up pitch (_rmPitch = 1).")]
    [SerializeField] private float climbPitchSpeedPenalty = 4f;
    [Tooltip("Shapes how pitch magnitude maps to speed change. X is abs pitch 0..1, Y is strength 0..1.")]
    [SerializeField] private AnimationCurve pitchSpeedCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Lowest allowed total bonus speed after climb pitch penalty. Keep at 0 to prevent the modifier from adding reverse movement.")]
    [SerializeField] private float minPitchModifiedBonusSpeed = 0f;
    [Tooltip("Smoothing for the added flight speed. Higher = snappier acceleration/deceleration.")]
    [SerializeField] private float bonusSpeedSmoothing = 6f;
    [Tooltip("Minimum rigidbody speed before roll/boost uses actual travel direction instead of transform.forward.")]
    [SerializeField] private float travelDirectionMinSpeed = 1f;

    [Header("Roll")]
    [Tooltip("Forward speed (m/s) applied during a roll, since roll clips are in-place.")]
    [SerializeField] private float rollForwardSpeed = 15f;
    [Tooltip("How long a roll lasts (seconds). Input is locked for this duration.")]
    [SerializeField] private float rollDuration = 0.6f;
    [Tooltip("Cooldown (seconds) after a roll completes before another can start.")]
    [SerializeField] private float rollCooldown = 0.5f;
    [Tooltip("Thrust must exceed this for code-driven roll motion (forward speed, lateral slide, arc) to apply. Below this, the roll plays visually but the dragon doesn't translate.")]
    [SerializeField] private float rollMotionMinThrust = 0.4f;
    [Tooltip("How fast roll moves toward target per second when in smooth-axis mode (thrust ≤ rollMotionMinThrust). Higher = snappier response.")]
    [SerializeField] private float rollSmoothing = 3f;
    [Tooltip("After the roll ends, code keeps applying forward speed (tapered to zero) for this long, to cover the animator's exit blend back into flight.")]
    [SerializeField] private float rollExitBlendTime = 0.3f;
    [Tooltip("Total sideways distance (m) the dragon travels during a roll. Q slides left, E slides right.")]
    [SerializeField] private float rollLateralDistance = 4f;
    [Tooltip("Peak vertical height (m) of the arc bump during a roll. Returns to baseline at end of roll.")]
    [SerializeField] private float rollArcHeight = 2f;

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
    public Vector3 TravelDirection => GetTravelDirection();

    public float FlightThrust => _rmThrust;
    public float FlightYaw => _rmYaw;
    public float FlightPitch => _rmPitch;
    public float FlightRoll => _rmRoll;
    public float BonusFlightSpeed => _bonusFlightSpeed;

    // ─── Private State ───────────────────────────────────

    private bool isActive;
    private bool isHoverMode;
    private bool isFlapping;
    private bool isGliding;

    // Root motion flight state
    private float _rmThrust;
    private float _rmYaw;
    private float _rmPitch;
    private float _rmPitchTarget;
    private float _rmRoll;
    private float _rollTimeRemaining;
    private float _rollCooldownRemaining;
    private float _rollExitBlendRemaining;
    private float _bonusFlightSpeed;
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
        if (staminaController == null)
            staminaController = GetComponent<DragonStaminaController>();
        if (wingActivityTracker == null)
            wingActivityTracker = GetComponent<DragonWingActivityTracker>();

        thrustHash        = Animator.StringToHash("Thrust");
        yawHash           = Animator.StringToHash("Yaw");
        pitchHash         = Animator.StringToHash("Pitch");
        rollHash          = Animator.StringToHash("Roll");
        flightModeHash    = Animator.StringToHash("FlightMode");
        diveCrashLandHash = Animator.StringToHash("DiveCrashLand");

        isActive = false;
        isHoverMode = false;
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

        // Wings broken mid-flight — forced descent.
        if (staminaController != null && staminaController.WingsBroken)
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
        // Wings at zero — flight is locked until they regen.
        if (staminaController != null && staminaController.WingsBroken) return;
        isActive = true;
        isHoverMode = false;
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
        isFlapping = false;
        isGliding = false;

        // Reset root motion flight params
        _rmThrust = 0f;
        _rmYaw = 0f;
        _rmPitch = 0f;
        _rmPitchTarget = 0f;
        _rmRoll = 0;
        _rollTimeRemaining = 0f;
        _rollCooldownRemaining = 0f;
        _rollExitBlendRemaining = 0f;
        _bonusFlightSpeed = 0f;

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
        // Stamina exhaustion caps high-thrust — dragon falls to glide/cruise speed when empty.
        float maxThrustForFrame = staminaController != null
            ? Mathf.Min(thrustMax, staminaController.MaxFlightThrust)
            : thrustMax;
        _rmThrust = Mathf.Clamp(_rmThrust, thrustMin, maxThrustForFrame);

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
        // Stamina exhaustion caps the climb side of pitch (positive = nose-up). Diving
        // (negative) is unaffected — gravity does the work, no wing cost. Camera and
        // head tracking are untouched; only the animator pitch input is clamped.
        if (staminaController != null)
        {
            float climbCap = staminaController.MaxClimbPitch;
            if (_rmPitchTarget > climbCap) _rmPitchTarget = climbCap;
        }
        _rmPitch = Mathf.MoveTowards(_rmPitch, _rmPitchTarget, pitchSmoothSpeed * dt);

        // ── Roll (one-shot, locked for rollDuration, then cooldown; forward
        //   movement applied since roll clips are in-place) ──
        // Roll has two modes based on thrust:
        //   thrust >  rollMotionMinThrust → discrete one-shot (KeyDown snaps _rmRoll = ±1, locked for rollDuration)
        //   thrust ≤ rollMotionMinThrust  → smooth axis (key hold → MoveTowards target, like Yaw)
        if (_rollTimeRemaining > 0f)
        {
            _rollTimeRemaining -= dt;

            // Net lateral slide + vertical arc bump. Both velocity profiles are
            // zero at the endpoints — no velocity flick when the roll starts/ends.
            //   lateral: eases 0 → rollLateralDistance via (1 - cos(π*t))/2
            //   vertical: rises to rollArcHeight at t=0.5 then returns to 0 via (1 - cos(2π*t))/2
            if (_rmThrust > rollMotionMinThrust)
            {
                float duration = Mathf.Max(rollDuration, 0.0001f);
                float t = Mathf.Clamp01(1f - (_rollTimeRemaining / duration));
                float dirSign = _rmRoll;
                Vector3 travelForward = GetTravelDirection();
                Vector3 travelRight = GetTravelRight(travelForward);

                float rightVel = rollLateralDistance * (Mathf.PI / (2f * duration))
                                 * Mathf.Sin(Mathf.PI * t) * dirSign;
                float upVel = rollArcHeight * (Mathf.PI / duration)
                              * Mathf.Sin(2f * Mathf.PI * t);

                Vector3 displacement = (travelForward * rollForwardSpeed
                                      + travelRight   * rightVel
                                      + Vector3.up    * upVel) * dt;
                ApplyMovement(displacement);
            }

            if (_rollTimeRemaining <= 0f)
            {
                _rmRoll = 0f;
                _rollCooldownRemaining = rollCooldown;
                _rollExitBlendRemaining = rollExitBlendTime;
            }
        }
        else if (_rmThrust > rollMotionMinThrust)
        {
            if (_rollExitBlendRemaining > 0f)
            {
                float taper = rollExitBlendTime > 0f
                    ? _rollExitBlendRemaining / rollExitBlendTime
                    : 0f;
                ApplyMovement(GetTravelDirection() * rollForwardSpeed * taper * dt);
                _rollExitBlendRemaining -= dt;
            }

            if (_rollCooldownRemaining > 0f)
                _rollCooldownRemaining -= dt;

            _rmRoll = 0f;

            if (_rollCooldownRemaining <= 0f)
            {
                if (Input.GetKeyDown(rollLeftKey))
                {
                    _rmRoll = -1f;
                    _rollTimeRemaining = rollDuration;
                    _rollExitBlendRemaining = 0f;
                }
                else if (Input.GetKeyDown(rollRightKey))
                {
                    _rmRoll = 1f;
                    _rollTimeRemaining = rollDuration;
                    _rollExitBlendRemaining = 0f;
                }
            }
        }
        else
        {
            // Smooth axis mode — like Yaw. No discrete one-shot, no code-driven motion.
            float targetRoll = 0f;
            if (Input.GetKey(rollLeftKey))       targetRoll = -1f;
            else if (Input.GetKey(rollRightKey)) targetRoll =  1f;

            _rmRoll = Mathf.MoveTowards(_rmRoll, targetRoll, rollSmoothing * dt);
            if (Mathf.Abs(targetRoll) < 0.01f && Mathf.Abs(_rmRoll) < 0.02f)
                _rmRoll = 0f;

            _rollExitBlendRemaining = 0f;
            if (_rollCooldownRemaining > 0f)
                _rollCooldownRemaining -= dt;
        }

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

        isHoverMode = Mathf.Abs(_rmThrust) < 0.05f;
        isFlapping = _rmThrust > 0.05f;
        isGliding = _rmThrust < -0.05f;

        // Hover up/down — only while thrust is at or below zero
        if (_rmThrust <= 0f)
        {
            float vertical = 0f;
            if (Input.GetKey(hoverUpKey))   vertical += 1f;
            if (Input.GetKey(hoverDownKey)) vertical -= 1f;

            if (vertical != 0f)
                ApplyMovement(Vector3.up * vertical * hoverVerticalSpeed * dt);
        }

        UpdateBonusFlightSpeed(dt);
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

    private void UpdateBonusFlightSpeed(float dt)
    {
        float thrustT = Mathf.InverseLerp(highThrustBonusStart, thrustMax, Mathf.Max(0f, _rmThrust));
        float wingT = 0f;

        if (wingActivityTracker != null && wingActivityTracker.IsFlapping)
            wingT = Mathf.Clamp01(wingActivityTracker.WingActivity / Mathf.Max(wingFlapFullActivity, 0.0001f));

        float targetBonus = thrustT * Mathf.Max(0f, highThrustBonusSpeed)
                          + wingT * Mathf.Max(0f, wingFlapBonusSpeed);
        targetBonus += GetPitchSpeedModifier();
        targetBonus = Mathf.Max(minPitchModifiedBonusSpeed, targetBonus);
        _bonusFlightSpeed = Mathf.MoveTowards(_bonusFlightSpeed, targetBonus, bonusSpeedSmoothing * dt * Mathf.Max(targetBonus, _bonusFlightSpeed, 1f));

        if (_bonusFlightSpeed > 0.01f)
            ApplyMovement(GetTravelDirection() * _bonusFlightSpeed * dt);
    }

    private float GetPitchSpeedModifier()
    {
        if (!usePitchSpeedModifier) return 0f;

        if (_rmPitch < -0.001f)
        {
            float t = Mathf.Clamp01(pitchSpeedCurve.Evaluate(Mathf.Abs(_rmPitch)));
            return t * Mathf.Max(0f, divePitchBonusSpeed);
        }

        if (_rmPitch > 0.001f)
        {
            float t = Mathf.Clamp01(pitchSpeedCurve.Evaluate(_rmPitch));
            return -t * Mathf.Max(0f, climbPitchSpeedPenalty);
        }

        return 0f;
    }

    private Vector3 GetTravelDirection()
    {
        if (rb != null)
        {
            Vector3 velocity = rb.linearVelocity;
            if (velocity.sqrMagnitude >= travelDirectionMinSpeed * travelDirectionMinSpeed)
                return velocity.normalized;
        }

        return transform.forward;
    }

    private Vector3 GetTravelRight(Vector3 travelForward)
    {
        Vector3 right = Vector3.Cross(Vector3.up, travelForward);
        if (right.sqrMagnitude < 0.0001f)
            right = transform.right;
        return right.normalized;
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
