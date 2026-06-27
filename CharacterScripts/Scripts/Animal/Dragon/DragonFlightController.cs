using UnityEngine;
using Unity.Netcode;
using UnityEngine.Serialization;

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

    [Header("Takeoff Thrust Ramp")]
    [Tooltip("Smoothly accelerates from Initial Thrust to Target Thrust after a ground takeoff.")]
    [SerializeField] private bool enableTakeoffThrustRamp = true;
    [Tooltip("Flight thrust applied when the ground takeoff enters flight mode.")]
    [Range(-1f, 1f)]
    [SerializeField] private float takeoffInitialThrust = 0.2f;
    [Tooltip("Flight thrust reached at the end of the automatic takeoff ramp.")]
    [Range(-1f, 1f)]
    [SerializeField] private float takeoffTargetThrust = 1f;
    [Tooltip("Seconds taken to reach Target Thrust. Pressing W or S cancels the ramp.")]
    [Min(0.01f)]
    [SerializeField] private float takeoffThrustRampDuration = 1.5f;
    [Tooltip("Shapes acceleration over the normalized takeoff ramp time.")]
    [SerializeField] private AnimationCurve takeoffThrustRampCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Input")]
    [SerializeField] private string horizontalAxis = "Horizontal";
    [Tooltip("When enabled, camera yaw relative to the dragon adds to flight yaw.")]
    [SerializeField] private bool enableMouseYaw = true;
    [Tooltip("Camera-vs-dragon yaw angle that maps to full flight yaw. Lower = stronger camera turning.")]
    [SerializeField] private float cameraYawAngleForFullTurn = 35f;
    [Tooltip("Positive FlightThrust required before camera yaw steering begins. 0.05 = 5% thrust.")]
    [Range(0f, 1f)]
    [SerializeField] private float minThrustForCameraYaw = 0.05f;
    [Tooltip("Blend range above Min Thrust For Camera Yaw. 0 = hard cutoff, 0.05 = fade in over the next 5% thrust.")]
    [Range(0f, 1f)]
    [SerializeField] private float cameraYawThrustGateBlendRange = 0.05f;
    [Tooltip("Invert the camera yaw contribution.")]
    [SerializeField] private bool invertMouseYaw = false;

    [Header("Hard Turn")]
    [Tooltip("When enabled, the Horizontal axis starts dedicated hard left/right turns instead of contributing to normal yaw.")]
    [SerializeField] private bool enableHardTurns = true;
    [Tooltip("Float parameter used to select the hard-turn animation. -1 = left, 0 = inactive, 1 = right.")]
    [SerializeField] private string hardTurnAnimatorParameter = "HardTurn";
    [Tooltip("Total world-space heading change produced by one hard turn.")]
    [Range(0f, 180f)]
    [SerializeField] private float hardTurnAngle = 90f;
    [Tooltip("Time in seconds for code to apply the full hard-turn heading change. Match this to the animation's turning section.")]
    [Min(0.01f)]
    [SerializeField] private float hardTurnDuration = 0.75f;
    [Tooltip("Delay after a hard turn before another can begin.")]
    [Min(0f)]
    [SerializeField] private float hardTurnCooldown = 0.25f;
    [Tooltip("Absolute Horizontal axis value required to start a hard turn. The axis must be released before another turn can start.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float hardTurnInputThreshold = 0.5f;
    [Tooltip("Minimum positive thrust required to start a hard turn. Set to 0 to allow hard turns while hovering or gliding.")]
    [Range(0f, 1f)]
    [SerializeField] private float hardTurnMinThrust = 0f;
    [Tooltip("Shapes how the scripted heading rotation is distributed over the hard-turn duration.")]
    [SerializeField] private AnimationCurve hardTurnYawCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("How long normal camera yaw remains neutral after the hard turn, allowing the follow camera to settle behind the new heading.")]
    [Min(0f)]
    [SerializeField] private float hardTurnCameraYawRecoveryTime = 0.35f;
    [Tooltip("Visual model pivot used for hard-turn banking. Defaults to the child named Dragon; do not assign the Rigidbody root.")]
    [SerializeField] private Transform hardTurnVisualRollTransform;
    [Tooltip("Maximum visual body bank during a hard turn.")]
    [Range(0f, 180f)]
    [SerializeField] private float hardTurnVisualRollAngle = 90f;
    [Tooltip("Visual bank over normalized hard-turn time. Default rolls in, holds at 90 degrees, then rolls out.")]
    [SerializeField] private AnimationCurve hardTurnVisualRollCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.2f, 1f),
        new Keyframe(0.8f, 1f),
        new Keyframe(1f, 0f));
    [Tooltip("Reverses the visual bank direction without changing the actual heading turn.")]
    [SerializeField] private bool invertHardTurnVisualRoll = false;

    [SerializeField] private bool invertY = false;
    [SerializeField] private KeyCode pauseInputKey = KeyCode.P;
    [Tooltip("Hold to preserve the dragon's current pitch and yaw steering values. Hard turns are blocked while held.")]
    [FormerlySerializedAs("pitchStabilizeKey")]
    [SerializeField] private KeyCode attitudeLockKey = KeyCode.LeftShift;
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
    [Tooltip("Positive FlightThrust required before wing-flap bonus speed can add movement. Keeps animated wing motion from creeping the dragon at zero thrust.")]
    [Range(0f, 1f)]
    [SerializeField] private float minThrustForWingFlapBonus = 0.1f;
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
    [Tooltip("Positive FlightThrust required before dive pitch bonus speed can add movement. Climb penalty can still reduce speed below this value.")]
    [Range(0f, 1f)]
    [SerializeField] private float minThrustForDivePitchBonus = 0.1f;
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
    public float FlightHardTurn => _hardTurnDirection;
    public string HardTurnAnimatorParameter => hardTurnAnimatorParameter;
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
    private bool _takeoffThrustRampActive;
    private float _takeoffThrustRampElapsed;
    private float _rollTimeRemaining;
    private float _rollCooldownRemaining;
    private float _rollExitBlendRemaining;
    private float _hardTurnDirection;
    private float _hardTurnElapsed;
    private float _hardTurnCooldownRemaining;
    private float _hardTurnCameraYawRecoveryRemaining;
    private float _pendingHardTurnYawDelta;
    private bool _hardTurnInputWasPressed;
    private float _visualHardTurnDirection;
    private float _visualHardTurnElapsed;
    private float _lastAppliedHardTurnVisualRoll;
    private float _bonusFlightSpeed;
    private bool _inputPaused;
    private bool _wasPauseMenuPaused;
    private bool _attitudeLockActive;
    private float _lockedPitch;
    private float _lockedYaw;

    // Animator hashes
    private int thrustHash;
    private int yawHash;
    private int pitchHash;
    private int rollHash;
    private int hardTurnHash;
    private int flightModeHash;
    private int diveCrashLandHash;
    private bool hasHardTurnAnimatorParameter;
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
        if (hardTurnVisualRollTransform == null)
            hardTurnVisualRollTransform = transform.Find("Dragon");

        thrustHash        = Animator.StringToHash("Thrust");
        yawHash           = Animator.StringToHash("Yaw");
        pitchHash         = Animator.StringToHash("Pitch");
        rollHash          = Animator.StringToHash("Roll");
        hardTurnHash      = Animator.StringToHash(hardTurnAnimatorParameter);
        flightModeHash    = Animator.StringToHash("FlightMode");
        diveCrashLandHash = Animator.StringToHash("DiveCrashLand");
        hasHardTurnAnimatorParameter = HasAnimatorFloatParameter(hardTurnHash);

        isActive = false;
        isHoverMode = false;
        isFlapping = false;
        isGliding = false;
        _diveCrashTriggered = false;

        lastPosition = transform.position;

        if (rb != null) rb.useGravity = false;
    }

    private void OnDisable()
    {
        RemoveHardTurnVisualRoll();
    }

    private void Update()
    {
        RemoveHardTurnVisualRoll();

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
            _takeoffThrustRampActive = false;
            _takeoffThrustRampElapsed = 0f;
            ResetAttitudeLock();
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
                EnterFlight(false);
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
    /// Enter flight mode. Ground takeoff uses a smooth thrust ramp by default;
    /// free-fall re-entry can bypass it for immediate recovery thrust.
    /// </summary>
    public void EnterFlight(bool smoothTakeoffThrust = true)
    {
        // Don't enter flight while swimming
        if (IsSwimmingActive()) return;
        // Wings at zero — flight is locked until they regen.
        if (staminaController != null && staminaController.WingsBroken) return;
        isActive = true;
        isHoverMode = false;
        _takeoffThrustRampActive = smoothTakeoffThrust && enableTakeoffThrustRamp;
        _takeoffThrustRampElapsed = 0f;
        _rmThrust = _takeoffThrustRampActive
            ? Mathf.Clamp(takeoffInitialThrust, thrustMin, thrustMax)
            : thrustMax;
        ResetHardTurn();
        ResetAttitudeLock();

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
        _takeoffThrustRampActive = false;
        _takeoffThrustRampElapsed = 0f;
        _rollTimeRemaining = 0f;
        _rollCooldownRemaining = 0f;
        _rollExitBlendRemaining = 0f;
        ResetHardTurn();
        ResetAttitudeLock();
        _bonusFlightSpeed = 0f;

        // Clear animator flight params
        if (animator != null)
        {
            animator.SetBool(flightModeHash, false);
            animator.SetFloat(thrustHash, 0f);
            animator.SetFloat(yawHash, 0f);
            animator.SetFloat(pitchHash, 0f);
            animator.SetFloat(rollHash, 0f);
            if (hasHardTurnAnimatorParameter)
                animator.SetFloat(hardTurnHash, 0f);
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

        if (cam == null)
            cam = Camera.main?.transform;

        float horizontal = Input.GetAxisRaw(horizontalAxis);
        UpdateAttitudeLock();

        // ── Thrust (hold-to-ramp: W accelerates, S decelerates, release freezes value) ──
        bool accelerateHeld = Input.GetKey(KeyCode.W);
        bool decelerateHeld = Input.GetKey(KeyCode.S);
        if (accelerateHeld || decelerateHeld)
            _takeoffThrustRampActive = false;

        if (accelerateHeld)
            _rmThrust += thrustAccelRate * dt;
        if (decelerateHeld)
            _rmThrust -= thrustDecelRate * dt;

        if (_takeoffThrustRampActive)
        {
            float duration = Mathf.Max(0.01f, takeoffThrustRampDuration);
            _takeoffThrustRampElapsed = Mathf.Min(_takeoffThrustRampElapsed + dt, duration);
            float normalizedTime = _takeoffThrustRampElapsed / duration;
            float rampT = takeoffThrustRampCurve != null && takeoffThrustRampCurve.length > 0
                ? Mathf.Clamp01(takeoffThrustRampCurve.Evaluate(normalizedTime))
                : normalizedTime;
            float initialThrust = Mathf.Clamp(takeoffInitialThrust, thrustMin, thrustMax);
            float targetThrust = Mathf.Clamp(takeoffTargetThrust, thrustMin, thrustMax);
            _rmThrust = Mathf.Lerp(initialThrust, targetThrust, rampT);

            if (_takeoffThrustRampElapsed >= duration)
                _takeoffThrustRampActive = false;
        }

        // Stamina exhaustion caps high-thrust — dragon falls to glide/cruise speed when empty.
        float maxThrustForFrame = staminaController != null
            ? Mathf.Min(thrustMax, staminaController.MaxFlightThrust)
            : thrustMax;
        _rmThrust = Mathf.Clamp(_rmThrust, thrustMin, maxThrustForFrame);

        bool suppressCameraYaw = UpdateHardTurn(_attitudeLockActive ? 0f : horizontal, dt);
        if (_attitudeLockActive)
            _hardTurnInputWasPressed = Mathf.Abs(horizontal) >= hardTurnInputThreshold;

        // ── Yaw ──
        float cameraYaw = 0f;
        if (!suppressCameraYaw && !_attitudeLockActive && enableMouseYaw && cam != null)
        {
            float yawDelta = Mathf.DeltaAngle(transform.eulerAngles.y, cam.eulerAngles.y);
            if (invertMouseYaw)
                yawDelta = -yawDelta;

            float positiveThrust = Mathf.Clamp01(_rmThrust);
            float yawGateT = cameraYawThrustGateBlendRange <= 0.0001f
                ? (positiveThrust >= minThrustForCameraYaw ? 1f : 0f)
                : Mathf.InverseLerp(
                    minThrustForCameraYaw,
                    Mathf.Clamp01(minThrustForCameraYaw + cameraYawThrustGateBlendRange),
                    positiveThrust);

            cameraYaw = Mathf.Clamp(yawDelta / Mathf.Max(cameraYawAngleForFullTurn, 0.0001f), -1f, 1f) * yawGateT;
        }

        float targetYaw = cameraYaw;
        if (suppressCameraYaw)
            _rmYaw = 0f;
        else if (_attitudeLockActive)
            _rmYaw = _lockedYaw;
        else
        {
            _rmYaw = Mathf.MoveTowards(_rmYaw, targetYaw, yawSmoothing * dt);
            if (Mathf.Abs(targetYaw) < 0.01f && Mathf.Abs(_rmYaw) < 0.02f)
                _rmYaw = 0f;
        }

        // ── Pitch (driven from camera angle unless attitude lock is held) ──
        if (_attitudeLockActive)
            _rmPitchTarget = _lockedPitch;
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
        _rmPitch = _attitudeLockActive
            ? _rmPitchTarget
            : Mathf.MoveTowards(_rmPitch, _rmPitchTarget, pitchSmoothSpeed * dt);

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
            if (hasHardTurnAnimatorParameter)
                animator.SetFloat(hardTurnHash, _hardTurnDirection);
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

    private void UpdateAttitudeLock()
    {
        bool lockHeld = Input.GetKey(attitudeLockKey);

        if (lockHeld && !_attitudeLockActive)
        {
            _attitudeLockActive = true;
            _lockedPitch = _rmPitch;
            _lockedYaw = _rmYaw;
        }
        else if (!lockHeld && _attitudeLockActive)
        {
            ResetAttitudeLock();
        }
    }

    private void ResetAttitudeLock()
    {
        _attitudeLockActive = false;
        _lockedPitch = 0f;
        _lockedYaw = 0f;
    }

    private bool UpdateHardTurn(float horizontal, float dt)
    {
        _pendingHardTurnYawDelta = 0f;

        if (_hardTurnCooldownRemaining > 0f)
            _hardTurnCooldownRemaining = Mathf.Max(0f, _hardTurnCooldownRemaining - dt);
        if (_hardTurnCameraYawRecoveryRemaining > 0f)
            _hardTurnCameraYawRecoveryRemaining = Mathf.Max(0f, _hardTurnCameraYawRecoveryRemaining - dt);

        bool inputPressed = Mathf.Abs(horizontal) >= hardTurnInputThreshold;

        if (!enableHardTurns)
        {
            _hardTurnInputWasPressed = inputPressed;
            ResetHardTurnMotion();
            return false;
        }

        if (_hardTurnDirection == 0f && !_hardTurnInputWasPressed && inputPressed
            && _hardTurnCooldownRemaining <= 0f
            && Mathf.Clamp01(_rmThrust) >= hardTurnMinThrust)
        {
            _hardTurnDirection = Mathf.Sign(horizontal);
            _hardTurnElapsed = 0f;
            _hardTurnCameraYawRecoveryRemaining = 0f;
        }

        _hardTurnInputWasPressed = inputPressed;
        bool wasActiveThisFrame = _hardTurnDirection != 0f;

        if (wasActiveThisFrame)
        {
            float duration = Mathf.Max(0.01f, hardTurnDuration);
            float previousT = Mathf.Clamp01(_hardTurnElapsed / duration);
            _hardTurnElapsed = Mathf.Min(_hardTurnElapsed + dt, duration);
            float currentT = Mathf.Clamp01(_hardTurnElapsed / duration);

            float previousProgress = EvaluateHardTurnProgress(previousT);
            float currentProgress = EvaluateHardTurnProgress(currentT);
            _pendingHardTurnYawDelta = (currentProgress - previousProgress)
                                     * hardTurnAngle
                                     * _hardTurnDirection;

            if (_hardTurnElapsed >= duration)
            {
                _hardTurnDirection = 0f;
                _hardTurnCooldownRemaining = hardTurnCooldown;
                _hardTurnCameraYawRecoveryRemaining = hardTurnCameraYawRecoveryTime;
            }
        }

        return wasActiveThisFrame || _hardTurnCameraYawRecoveryRemaining > 0f;
    }

    private float EvaluateHardTurnProgress(float t)
    {
        if (hardTurnYawCurve == null || hardTurnYawCurve.length == 0)
            return t;

        float start = hardTurnYawCurve.Evaluate(0f);
        float end = hardTurnYawCurve.Evaluate(1f);
        if (Mathf.Abs(end - start) < 0.0001f)
            return t;

        return Mathf.Clamp01((hardTurnYawCurve.Evaluate(t) - start) / (end - start));
    }

    public float ConsumeHardTurnYawDelta()
    {
        float yawDelta = _pendingHardTurnYawDelta;
        _pendingHardTurnYawDelta = 0f;
        return yawDelta;
    }

    public void ApplyHardTurnVisualRoll(float hardTurnDirection, float dt)
    {
        if (hardTurnVisualRollTransform == null) return;

        float direction = Mathf.Abs(hardTurnDirection) >= 0.5f
            ? Mathf.Sign(hardTurnDirection)
            : 0f;

        if (direction != 0f)
        {
            if (_visualHardTurnDirection != direction)
            {
                _visualHardTurnDirection = direction;
                _visualHardTurnElapsed = 0f;
            }
            else
            {
                _visualHardTurnElapsed += dt;
            }

            float duration = Mathf.Max(0.01f, hardTurnDuration);
            float normalizedTime = Mathf.Clamp01(_visualHardTurnElapsed / duration);
            float rollT = hardTurnVisualRollCurve != null && hardTurnVisualRollCurve.length > 0
                ? Mathf.Clamp01(hardTurnVisualRollCurve.Evaluate(normalizedTime))
                : normalizedTime;
            float rollDirection = invertHardTurnVisualRoll ? -direction : direction;
            _lastAppliedHardTurnVisualRoll = rollDirection * hardTurnVisualRollAngle * rollT;
        }
        else
        {
            _visualHardTurnDirection = 0f;
            _visualHardTurnElapsed = 0f;
            _lastAppliedHardTurnVisualRoll = 0f;
        }

        hardTurnVisualRollTransform.localRotation *=
            Quaternion.AngleAxis(_lastAppliedHardTurnVisualRoll, Vector3.forward);
    }

    private void RemoveHardTurnVisualRoll()
    {
        if (hardTurnVisualRollTransform == null || Mathf.Abs(_lastAppliedHardTurnVisualRoll) < 0.0001f)
            return;

        hardTurnVisualRollTransform.localRotation *=
            Quaternion.Inverse(Quaternion.AngleAxis(_lastAppliedHardTurnVisualRoll, Vector3.forward));
        _lastAppliedHardTurnVisualRoll = 0f;
    }

    private void ResetHardTurn()
    {
        ResetHardTurnMotion();
        _hardTurnCooldownRemaining = 0f;
        _hardTurnInputWasPressed = false;
    }

    private void ResetHardTurnMotion()
    {
        _hardTurnDirection = 0f;
        _hardTurnElapsed = 0f;
        _hardTurnCameraYawRecoveryRemaining = 0f;
        _pendingHardTurnYawDelta = 0f;
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

        if (_rmThrust >= minThrustForWingFlapBonus && wingActivityTracker != null && wingActivityTracker.IsFlapping)
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
            if (_rmThrust < minThrustForDivePitchBonus) return 0f;

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

    private bool HasAnimatorFloatParameter(int parameterHash)
    {
        if (animator == null) return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == parameterHash && parameter.type == AnimatorControllerParameterType.Float)
                return true;
        }

        return false;
    }

    private static float NormalizePitch(float xDegrees)
    {
        while (xDegrees > 180f) xDegrees -= 360f;
        while (xDegrees < -180f) xDegrees += 360f;
        return xDegrees;
    }
}
