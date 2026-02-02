using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon flight controller with 3 vertical modes:
/// - Hover (Space toggles): holds altitude unless Hover axis input is provided.
/// - Glide (hover OFF): auto-forward movement and builds speed while descending.
/// - Dive (hold V): fast dive with limited pitch/yaw rates; can drive a dive camera via IsDiving.
/// 
/// Uses Legacy Input Manager axes:
/// - Horizontal: A/D
/// - Vertical: W/S
/// - Hover: E/Q (configure in Input Manager)
/// - Mouse X / Mouse Y
/// </summary>
public class DragonFlightController : NetworkBehaviour
{
    [Header("Setup")]
    [Tooltip("Optional: not required for this controller.")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private bool useRigidbodyMovement = true;

    [Header("Input (Legacy Input Manager axes)")]
    [SerializeField] private string horizontalAxis = "Horizontal"; // A/D (yaw assist)
    [SerializeField] private string verticalAxis = "Vertical";     // W/S (throttle)
    [SerializeField] private string hoverAxis = "Hover";           // E/Q (up/down while hovering)
    [SerializeField] private KeyCode toggleHoverKey = KeyCode.Space;
    [SerializeField] private KeyCode speedModifierKey = KeyCode.LeftShift;

    [Header("Modes")]
    [SerializeField] private bool startInHoverMode = true;
    [SerializeField] private KeyCode diveKey = KeyCode.V;

    [Header("Look / Turn")]
    [SerializeField] private bool invertY = false;
    [SerializeField] private float rotationSmoothTime = 0.08f;

    [Header("Look / Turn - Flight")]
    [SerializeField] private float flightMouseSensitivityX = 220f;
    [SerializeField] private float flightMouseSensitivityY = 160f;
    [SerializeField] private float flightPitchClamp = 70f;
    [SerializeField] private float flightKeyYawSpeed = 140f;

    [Header("Look / Turn - Hover")]
    [SerializeField] private float hoverMouseSensitivityX = 120f;
    [SerializeField] private float hoverMouseSensitivityY = 80f;
    [SerializeField] private float hoverPitchClamp = 20f;
    [SerializeField] private float hoverKeyYawSpeed = 70f;

    [Header("Hover Behavior")]
    [Tooltip("Optional: only used when grounded to maintain a stable hover height above ground.")]
    [SerializeField] private float hoverHeight = 2f;
    [SerializeField] private float hoverVerticalSpeed = 3f; // E/Q vertical speed
    [Tooltip("Hard cap for hover turning (Option B). Degrees per second.")]
    [SerializeField] private float hoverYawRate = 80f;
    [Tooltip("Hard cap for hover pitch turning (Option B). Degrees per second.")]
    [SerializeField] private float hoverPitchRate = 60f;

    [Header("Flight Params")]
    [SerializeField] private int minAirSpeed = 4;
    [SerializeField] private int maxAirSpeed = 25;
    [Tooltip("SmoothDamp time (bigger = slower).")]
    [SerializeField] private float acceleration = 1.25f;
    [Tooltip("SmoothDamp time (bigger = slower).")]
    [SerializeField] private float deceleration = 0.60f;
    [Tooltip("SmoothDamp time for coasting down.")]
    [SerializeField] private float momentum = 1.50f;
    [Tooltip("Shift air-brake SmoothDamp time.")]
    [SerializeField] private float glideSpeedDecay = 0.35f;

    [Header("Glide (Hover OFF)")]
    [SerializeField] private float glideDownSpeed = 2.5f;
    [SerializeField] private float glideAutoAccel = 6f;
    [SerializeField] private float glideMaxSpeed = 32f;

    [Header("Dive (Hold V)")]
    [SerializeField] private float diveTargetSpeed = 55f;
    [SerializeField] private float diveAccel = 18f;
    [SerializeField] private float divePitchMin = -85f;
    [SerializeField] private float divePitchMax = -45f;
    [SerializeField] private float diveYawRate = 55f;
    [SerializeField] private float divePitchRate = 45f;

    [Header("Roll / Bank")]
    [SerializeField] private float rollAngle = 45f;
    [SerializeField] private float rollSmoothTime = 0.15f;

    [Header("Grounding")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckDistance = 2.5f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundedConfirmTime = 0.08f;

    [Header("UI/Stats")]
    [SerializeField] private FlightStats flightStats;

    // Public read-only state
    public bool IsHoverMode => isHoverMode;
    public bool IsFlying => isFlying;
    public bool IsGliding => isGliding;
    public bool IsDiving => isDiving;
    public bool IsGrounded => isGrounded;
    public float AirSpeed => airSpeed;
    public float RollAngle => currentRollAngle;
    public float GroundDistance => groundDistance;
    public Vector3 Velocity => currentVelocity;

    // State
    private bool hoverRequested;
    private bool isHoverMode;
    private bool isFlying;
    private bool isGliding;
    private bool isGrounded;
    private bool isSpeedModified;
    private bool isDiving;

    private float airSpeed;
    private float airSpeedVelocity;

    // Rotation state
    private float yaw;
    private float pitch;
    private float smoothedYaw;
    private float smoothedPitch;
    private float yawVel;
    private float pitchVel;

    // Bank state
    private float currentRollAngle;
    private float rollVel;

    // Grounding debounce
    private float groundedTimer;
    private float groundDistance;

    private Vector3 currentVelocity;
    private Vector3 lastPosition;

    private void Awake()
    {
        isHoverMode = startInHoverMode;
        hoverRequested = startInHoverMode;

        if (rb == null) rb = GetComponent<Rigidbody>();

        isHoverMode = startInHoverMode;

        Vector3 e = transform.eulerAngles;
        yaw = smoothedYaw = e.y;
        pitch = smoothedPitch = NormalizePitch(e.x);

        lastPosition = transform.position;

        // We control vertical motion ourselves (hover/glide/dive).
        if (rb != null) rb.useGravity = false;
    }

    private void Update()
    {
        if (!IsOwner) return;
        ReadInput();
        UpdateGrounding();
        UpdateRotation(Time.deltaTime);
        UpdateMovement(Time.deltaTime);
        UpdateUI();

        currentVelocity = rb != null ? rb.linearVelocity : (transform.position - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;
    }

    private void ReadInput()
    {
        if (Input.GetKeyDown(toggleHoverKey))
        {
            hoverRequested = !hoverRequested;

            // When turning hover ON, kill vertical velocity immediately
            // Horizontal momentum will decay naturally via airspeed system
            if (hoverRequested && rb != null)
            {
                Vector3 v = rb.linearVelocity;
                v.y = 0f;
                rb.linearVelocity = v;
            }
        }

        isSpeedModified = Input.GetKey(speedModifierKey);
        isDiving = Input.GetKey(diveKey);
    }

    private void UpdateGrounding()
    {
        Vector3 origin = groundCheck != null ? groundCheck.position : transform.position;

        bool hit = Physics.Raycast(origin, Vector3.down, out RaycastHit rh,
            groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);

        // Ignore self-hit (dragon collider).
        if (hit && rh.transform != null && rh.transform.root == transform.root)
            hit = false;

        if (hit)
        {
            groundedTimer += Time.deltaTime;
            if (groundedTimer >= groundedConfirmTime)
            {
                isGrounded = true;
                groundDistance = rh.distance;
            }
        }
        else
        {
            groundedTimer = 0f;
            isGrounded = false;
            groundDistance = groundCheckDistance;
        }
    }

    private void UpdateRotation(float dt)
    {
        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y");
        float ySign = invertY ? 1f : -1f;
        float ad = Input.GetAxisRaw(horizontalAxis);

        // --- DIVE (Option B): steerable but limited yaw/pitch rates + pitch clamp ---
        if (isDiving && !isGrounded)
        {
            float desiredYawDelta = (mx * flightMouseSensitivityX + ad * flightKeyYawSpeed) * dt;
            float desiredPitchDelta = (my * flightMouseSensitivityY * ySign) * dt;

            desiredYawDelta = Mathf.Clamp(desiredYawDelta, -diveYawRate * dt, diveYawRate * dt);
            desiredPitchDelta = Mathf.Clamp(desiredPitchDelta, -divePitchRate * dt, divePitchRate * dt);

            yaw += desiredYawDelta;
            pitch += desiredPitchDelta;

            pitch = Mathf.Clamp(pitch, divePitchMin, divePitchMax);

            smoothedYaw = Mathf.SmoothDampAngle(smoothedYaw, yaw, ref yawVel, Mathf.Max(0.01f, rotationSmoothTime * 0.6f));
            smoothedPitch = Mathf.SmoothDamp(smoothedPitch, pitch, ref pitchVel, Mathf.Max(0.01f, rotationSmoothTime * 0.6f));
        }
        else
        {
            bool hoverLook = isHoverMode && !isGrounded;

            float sensX = hoverLook ? hoverMouseSensitivityX : flightMouseSensitivityX;
            float sensY = hoverLook ? hoverMouseSensitivityY : flightMouseSensitivityY;
            float clamp = hoverLook ? hoverPitchClamp : flightPitchClamp;
            float keyYaw = hoverLook ? hoverKeyYawSpeed : flightKeyYawSpeed;

            yaw += mx * sensX * dt;
            yaw += ad * keyYaw * dt;

            pitch += my * sensY * dt * ySign;
            pitch = Mathf.Clamp(pitch, -clamp, clamp);

            // Hover Option B: hard cap rotation rates (deg/sec)
            if (hoverLook)
            {
                float yawDelta = Mathf.DeltaAngle(smoothedYaw, yaw);
                float pitchDelta = pitch - smoothedPitch;

                yawDelta = Mathf.Clamp(yawDelta, -hoverYawRate * dt, hoverYawRate * dt);
                pitchDelta = Mathf.Clamp(pitchDelta, -hoverPitchRate * dt, hoverPitchRate * dt);

                smoothedYaw += yawDelta;
                smoothedPitch += pitchDelta;
            }
            else
            {
                smoothedYaw = Mathf.SmoothDampAngle(smoothedYaw, yaw, ref yawVel, rotationSmoothTime);
                smoothedPitch = Mathf.SmoothDamp(smoothedPitch, pitch, ref pitchVel, rotationSmoothTime);
            }
        }

        // Bank based on turning
        float turnForBank = Mathf.Clamp(mx + ad, -1f, 1f);
        float bankStrength = isDiving ? 1.1f : (isHoverMode ? 0.35f : 1f);
        float targetRoll = -turnForBank * rollAngle * bankStrength;
        currentRollAngle = Mathf.SmoothDamp(currentRollAngle, targetRoll, ref rollVel, rollSmoothTime);

        ApplyRotation(Quaternion.Euler(smoothedPitch, smoothedYaw, currentRollAngle));
    }

    private void UpdateMovement(float dt)
    {
        float forwardInput = Input.GetAxisRaw(verticalAxis);
        float hoverInput = Input.GetAxisRaw(hoverAxis);

        bool wantsForward = forwardInput > 0.05f;
        bool wantsBrake = forwardInput < -0.05f;
        bool airborne = !isGrounded;

        // Determine hover mode state based on conditions
        // Hover is OFF if: diving OR wanting to fly forward
        // Otherwise: use the player's hover request
        if (isDiving || wantsForward)
        {
            isHoverMode = false;
            hoverRequested = false; // Auto-disable hover request when flying/diving starts
        }
        else
        {
            // If player requested hover and not diving and not pushing W, we hover
            isHoverMode = hoverRequested;
        }

        UpdateAirspeed(wantsForward, wantsBrake, dt);

        // isFlying: airborne and either actively accelerating OR has speed
        isFlying = airborne && !isHoverMode && (wantsForward || airSpeed >= minAirSpeed);

        // isGliding: airborne, not hovering, not actively pressing W, but still has speed
        isGliding = airborne && !isHoverMode && !wantsForward && airSpeed >= minAirSpeed;

        // Forward movement
        if (airborne && airSpeed > 0.01f)
            ApplyMovement(transform.forward * airSpeed * dt);

        if (!airborne) return;

        // Vertical behavior priority: Dive > Hover (perfect) > Glide
        if (isDiving)
        {
            airSpeed = Mathf.MoveTowards(airSpeed, diveTargetSpeed, diveAccel * dt);
            ApplyMovement(Vector3.down * (diveAccel * 0.5f) * dt);
            return;
        }

        if (isHoverMode)
        {
            // Perfect hover: maintain altitude, kill Y velocity every frame
            if (rb != null)
            {
                Vector3 v = rb.linearVelocity;
                v.y = 0f;
                rb.linearVelocity = v;
            }

            // Allow manual vertical movement with E/Q
            if (Mathf.Abs(hoverInput) > 0.01f)
            {
                ApplyMovement(Vector3.up * hoverInput * hoverVerticalSpeed * dt);
            }

            // Optional near-ground assist to maintain hoverHeight
            UpdateHoverHeight(dt);
        }
        else
        {
            // Glide: auto-accelerate while descending
            airSpeed = Mathf.MoveTowards(airSpeed, Mathf.Min(glideMaxSpeed, diveTargetSpeed), glideAutoAccel * dt);
            ApplyMovement(Vector3.down * glideDownSpeed * dt);
        }
    }

    private void UpdateAirspeed(bool wantsForward, bool wantsBrake, float dt)
    {
        float forwardInput = Input.GetAxisRaw(verticalAxis);

        if (isGrounded)
        {
            airSpeed = 0f;
            airSpeedVelocity = 0f;
            return;
        }

        // Dive: speed is handled in movement (allow brake)
        if (isDiving)
        {
            if (wantsBrake)
                airSpeed = Mathf.SmoothDamp(airSpeed, 0f, ref airSpeedVelocity, Mathf.Max(0.01f, deceleration));

            airSpeed = Mathf.Clamp(airSpeed, 0f, Mathf.Max(maxAirSpeed, glideMaxSpeed, diveTargetSpeed));
            return;
        }

        // Hover mode: let momentum decay naturally
        if (isHoverMode)
        {
            // Decay airspeed naturally due to momentum
            airSpeed = Mathf.SmoothDamp(airSpeed, 0f, ref airSpeedVelocity, Mathf.Max(0.01f, momentum));

            // Shift key acts as air brake to slow down faster
            if (isSpeedModified)
                airSpeed = Mathf.SmoothDamp(airSpeed, 0f, ref airSpeedVelocity, Mathf.Max(0.01f, glideSpeedDecay));

            airSpeed = Mathf.Clamp(airSpeed, 0f, maxAirSpeed);
            return;
        }

        // Glide (hover OFF): allow W to help, brake to slow, shift to airbrake
        if (wantsForward)
        {
            airSpeed = Mathf.SmoothDamp(airSpeed, glideMaxSpeed, ref airSpeedVelocity, Mathf.Max(0.01f, acceleration));
        }
        else if (wantsBrake)
        {
            airSpeed = Mathf.SmoothDamp(airSpeed, 0f, ref airSpeedVelocity, Mathf.Max(0.01f, deceleration));
        }

        if (isSpeedModified)
            airSpeed = Mathf.SmoothDamp(airSpeed, 0f, ref airSpeedVelocity, Mathf.Max(0.01f, glideSpeedDecay));

        airSpeed = Mathf.Clamp(airSpeed, 0f, Mathf.Max(maxAirSpeed, glideMaxSpeed));
    }

    private void UpdateHoverHeight(float dt)
    {
        if (!isHoverMode || !isGrounded) return;

        float heightDelta = hoverHeight - groundDistance;
        if (Mathf.Abs(heightDelta) > 0.02f)
        {
            ApplyMovement(Vector3.up * heightDelta * dt);
        }
    }

    private void ApplyMovement(Vector3 displacement)
    {

            transform.position += displacement;
    }

    private void ApplyRotation(Quaternion targetRotation)
    {
 
            transform.rotation = targetRotation;
    }

    private void UpdateUI()
    {
        if (flightStats == null) return;

        flightStats.CurrentSpeed(airSpeed);
        flightStats.CurrentStamina(0f); // wire stamina back later if needed
        flightStats.SetMaxAirSpeed(maxAirSpeed);
        flightStats.SetMinAirSpeed(minAirSpeed);
        flightStats.SetMaxStamina(100);
        flightStats.SetMinStamina(0);
    }

    private static float NormalizePitch(float xDegrees)
    {
        while (xDegrees > 180f) xDegrees -= 360f;
        while (xDegrees < -180f) xDegrees += 360f;
        return xDegrees;
    }
}
