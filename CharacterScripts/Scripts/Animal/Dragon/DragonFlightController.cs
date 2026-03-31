using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon flight controller with stamina-driven flight modes:
/// - Flapping (W held + stamina > 0): Active flight, consumes stamina
/// - Glide (no W or stamina depleted): Passive descent, regenerates stamina
/// - Hover (Space toggle): Holds altitude, free stamina
/// 
/// Pitch modifiers (±45°):
/// - Flapping up = more stamina drain
/// - Flapping down = airspeed boost
/// - Gliding up = more airspeed loss
/// - Gliding down = airspeed boost
/// </summary>
public class DragonFlightController : NetworkBehaviour
{

 
    [Header("Setup")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private bool useRigidbodyMovement = true;

    [Header("References")]
    [SerializeField] private AnimalGroundingSystem groundingSystem;
    [SerializeField] private DragonGroundController groundController;
    [SerializeField] private Animator animator;

    [Header("Root Motion Flight")]
    [Tooltip("When enabled, Thrust/Yaw/Pitch drive blend trees and root motion handles movement. Old controls disabled.")]
    [SerializeField] private bool useFlightRootMotion = false;
    [Tooltip("How fast Thrust (GaitSpeed) ramps up/down.")]
    [SerializeField] private float thrustSmoothSpeed = 2f;
    [Tooltip("Smoothing for Yaw (TurnAngle). Higher = faster response.")]
    [SerializeField] private float yawSmoothing = 5f;
    [Tooltip("Smoothing for Pitch. Higher = faster response.")]
    [SerializeField] private float pitchSmoothing = 5f;

    [Header("Input")]
    [SerializeField] private string horizontalAxis = "Horizontal";
    [SerializeField] private string verticalAxis = "Vertical";
    [SerializeField] private string hoverAxis = "Hover";
    [SerializeField] private KeyCode toggleHoverKey = KeyCode.Space;

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
    [SerializeField] private float hoverVerticalSpeed = 3f;
    [SerializeField] private float hoverYawRate = 80f;
    [SerializeField] private float hoverPitchRate = 60f;

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaDrainRate = 10f;
    [SerializeField] private float staminaRegenRate = 15f;
    [SerializeField] private float staminaResumeThreshold = 50f;

    [Header("Flapping (W + Stamina)")]
    [SerializeField] private float flapBaseAirSpeed = 15f;
    [SerializeField] private float flapMaxAirSpeed = 25f;
    [SerializeField] private float flapAcceleration = 5f;

    [Header("Glide (No W or No Stamina)")]
    [SerializeField] private float glideMinAirSpeed = 5f;
    [SerializeField] private float glideAirSpeedLossRate = 2f;
    [SerializeField] private float glideDescentSpeed = 1.5f;

    [Header("Pitch Modifiers (±45°)")]
    [SerializeField] private float pitchStaminaDrainMultiplier = 1.5f;
    [SerializeField] private float pitchAirSpeedBoost = 3f;
    [SerializeField] private float pitchMaxAngle = 45f;

    [Header("Roll / Bank")]
    [SerializeField] private float rollAngle = 45f;
    [SerializeField] private float rollSmoothTime = 0.15f;

    [Header("UI/Stats")]
    [SerializeField] private FlightStats flightStats;

    // Public read-only state
    public bool IsHoverMode => isHoverMode;
    public bool IsFlying => isFlapping;
    public bool IsFlapping => isFlapping;
    public bool IsGliding => isGliding;
    public bool IsDiving => false;
    public bool IsGrounded => groundingSystem != null && groundingSystem.IsGrounded;
    public float AirSpeed => airSpeed;
    public float Stamina => stamina;
    public float StaminaPercent => stamina / maxStamina;
    public bool IsStaminaDepleted => staminaDepleted;
    public float RollAngle => currentRollAngle;
    public Vector3 Velocity => currentVelocity;

    // State
    private bool isActive;
    private bool hoverRequested;
    private bool isHoverMode;
    private bool isFlapping;
    private bool isGliding;
    private bool staminaDepleted;

    private float stamina;
    private float airSpeed;

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

    // Root motion flight state
    private float _rmThrust;
    private float _rmYaw;
    private float _rmYawVel;
    private float _rmPitch;
    private float _rmPitchVel;

    // Public for animator controller to read
    public float FlightPitch => _rmPitch;

    private Vector3 currentVelocity;
    private Vector3 lastPosition;

    private void Awake()
    {
        if (groundingSystem == null)
            groundingSystem = GetComponentInChildren<AnimalGroundingSystem>();
        if (groundController == null)
            groundController = GetComponent<DragonGroundController>();
        if (animator == null)
            animator = GetComponentInParent<Animator>();

        if (rb == null) rb = GetComponent<Rigidbody>();

        isActive = false;
        isHoverMode = false;
        hoverRequested = false;
        stamina = maxStamina;

        Vector3 e = transform.eulerAngles;
        yaw = smoothedYaw = e.y;
        pitch = smoothedPitch = NormalizePitch(e.x);

        lastPosition = transform.position;

        if (rb != null) rb.useGravity = false;
    }

    private void Update()
    {

        if (groundingSystem.IsGrounded && isActive)
        {
            isActive = false;
            isHoverMode = false;
            hoverRequested = false;
            isFlapping = false;
            isGliding = false;
        }

        if (!isActive) return;
        if (!IsOwner) return;

        if (useFlightRootMotion)
        {
            ReadInput();
            UpdateRootMotionFlight(Time.deltaTime);
            UpdateUI();
        }
        else
        {
            ReadInput();
            UpdateRotation(Time.deltaTime);
            UpdateStaminaAndMovement(Time.deltaTime);
            UpdateUI();
        }

        currentVelocity = rb != null ? rb.linearVelocity : (transform.position - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;
    }

    // ─── Public API for GroundController Handoff ─────────

    public void RequestHover()
    {
        isActive = true;
        isHoverMode = true;
        hoverRequested = true;
        airSpeed = 0f;
        stamina = maxStamina;

        yaw = smoothedYaw = transform.eulerAngles.y;
        pitch = smoothedPitch = 0f;
    }

    public void RequestGlide()
    {
        isActive = true;
        isHoverMode = false;
        hoverRequested = false;

        yaw = smoothedYaw = transform.eulerAngles.y;
        pitch = smoothedPitch = NormalizePitch(transform.eulerAngles.x);
    }

    // ─── Input ───────────────────────────────────────────

    private void ReadInput()
    {
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


    }

    // ─── Rotation ────────────────────────────────────────

    private void UpdateRotation(float dt)
    {
        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y");
        float ySign = invertY ? 1f : -1f;
        float ad = Input.GetAxisRaw(horizontalAxis);

        {
            bool hoverLook = isHoverMode;

            float sensX = hoverLook ? hoverMouseSensitivityX : flightMouseSensitivityX;
            float sensY = hoverLook ? hoverMouseSensitivityY : flightMouseSensitivityY;
            float clamp = hoverLook ? hoverPitchClamp : flightPitchClamp;
            float keyYaw = hoverLook ? hoverKeyYawSpeed : flightKeyYawSpeed;

            yaw += mx * sensX * dt;
            yaw += ad * keyYaw * dt;

            pitch += my * sensY * dt * ySign;
            pitch = Mathf.Clamp(pitch, -clamp, clamp);

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

        float turnForBank = Mathf.Clamp(mx + ad, -1f, 1f);
        float bankStrength = isHoverMode ? 0.35f : 1f;
        float targetRoll = -turnForBank * rollAngle * bankStrength;
        currentRollAngle = Mathf.SmoothDamp(currentRollAngle, targetRoll, ref rollVel, rollSmoothTime);

        ApplyRotation(Quaternion.Euler(smoothedPitch, smoothedYaw, currentRollAngle));
    }

    // ─── Stamina & Movement ──────────────────────────────

    private void UpdateStaminaAndMovement(float dt)
    {
        float forwardInput = Input.GetAxisRaw(verticalAxis);
        float hoverInput = Input.GetAxisRaw(hoverAxis);

        bool wantsFlap = forwardInput > 0.05f;

        // ─── Hover Mode (free stamina) ───────────────────
        if (wantsFlap)
        {
            isHoverMode = false;
            hoverRequested = false;
        }
        else
        {
            isHoverMode = hoverRequested;
        }

        if (isHoverMode)
        {
            isFlapping = false;
            isGliding = false;

            airSpeed = Mathf.MoveTowards(airSpeed, 0f, 5f * dt);

            if (rb != null)
            {
                Vector3 v = rb.linearVelocity;
                v.y = 0f;
                rb.linearVelocity = v;
            }

            if (Mathf.Abs(hoverInput) > 0.01f)
            {
                ApplyMovement(Vector3.up * hoverInput * hoverVerticalSpeed * dt);
            }

            if (airSpeed > 0.01f)
                ApplyMovement(transform.forward * airSpeed * dt);

            return;
        }

        // ─── Flapping / Gliding Logic ────────────────────

        float pitchFactor = GetPitchFactor();

        if (staminaDepleted && stamina >= staminaResumeThreshold)
        {
            staminaDepleted = false;
        }
        else if (stamina <= 0f)
        {
            staminaDepleted = true;
        }

        bool canFlap = wantsFlap && !staminaDepleted && stamina > 0f;

        if (canFlap)
        {
            // ─── FLAPPING ────────────────────────────────
            isFlapping = true;
            isGliding = false;

            float drainMultiplier = 1f;
            if (pitchFactor > 0f)
            {
                drainMultiplier = 1f + (pitchFactor * (pitchStaminaDrainMultiplier - 1f));
            }
            stamina -= staminaDrainRate * drainMultiplier * dt;
            stamina = Mathf.Max(0f, stamina);

            float targetSpeed = flapBaseAirSpeed;
            if (pitchFactor < 0f)
            {
                targetSpeed = flapMaxAirSpeed + (Mathf.Abs(pitchFactor) * pitchAirSpeedBoost);
            }
            airSpeed = Mathf.MoveTowards(airSpeed, targetSpeed, flapAcceleration * dt);

            ApplyMovement(transform.forward * airSpeed * dt);
        }
        else
        {
            // ─── GLIDING ─────────────────────────────────
            isFlapping = false;
            isGliding = true;

            stamina += staminaRegenRate * dt;
            stamina = Mathf.Min(stamina, maxStamina);

            float speedChange = glideAirSpeedLossRate;
            if (pitchFactor > 0f)
            {
                speedChange = glideAirSpeedLossRate * (1f + pitchFactor);
            }
            else if (pitchFactor < 0f)
            {
                speedChange = -pitchAirSpeedBoost * Mathf.Abs(pitchFactor);
            }

            airSpeed -= speedChange * dt;

            if (Mathf.Abs(pitchFactor) < 0.2f)
            {
                airSpeed = Mathf.Max(airSpeed, glideMinAirSpeed);
            }
            else
            {
                airSpeed = Mathf.Max(airSpeed, 0f);
            }
            airSpeed = Mathf.Min(airSpeed, flapMaxAirSpeed);

            if (airSpeed > 0.01f)
                ApplyMovement(transform.forward * airSpeed * dt);

            if (airSpeed <= glideMinAirSpeed + 0.5f)
            {
                ApplyMovement(Vector3.down * glideDescentSpeed * dt);
            }
        }
    }

    /// <summary>
    /// Returns pitch factor from -1 (down 45°) to +1 (up 45°). 0 = horizontal.
    /// </summary>
    private float GetPitchFactor()
    {
        float clampedPitch = -Mathf.Clamp(smoothedPitch, -pitchMaxAngle, pitchMaxAngle);
        return clampedPitch / pitchMaxAngle;
    }

    // ─── Helpers ─────────────────────────────────────────

    private void ApplyMovement(Vector3 displacement)
    {
        if (useRigidbodyMovement && rb != null)
            rb.MovePosition(rb.position + displacement);
        else
            transform.position += displacement;
    }

    private void ApplyRotation(Quaternion targetRotation)
    {
        if (useRigidbodyMovement && rb != null)
            rb.MoveRotation(targetRotation);
        else
            transform.rotation = targetRotation;
    }

    private void UpdateUI()
    {
        if (flightStats == null) return;

        flightStats.CurrentSpeed(airSpeed);
        flightStats.CurrentStamina(stamina);
        flightStats.SetMaxAirSpeed(flapMaxAirSpeed);
        flightStats.SetMinAirSpeed(glideMinAirSpeed);
        flightStats.SetMaxStamina(maxStamina);
        flightStats.SetMinStamina(0);
    }

    // ─── Root Motion Flight ─────────────────────────────

    private void UpdateRootMotionFlight(float dt)
    {
        if (groundController == null) return;

        float vertical = Input.GetAxisRaw(verticalAxis);   // W/S → Thrust
        float horizontal = Input.GetAxisRaw(horizontalAxis); // A/D → Yaw
        float mouseY = Input.GetAxisRaw("Mouse Y");         // Mouse Y → Pitch
        float ySign = invertY ? 1f : -1f;

        // ── Thrust (maps to GaitSpeed) ──
        // W = positive thrust, S = negative (or brake)
        float targetThrust = Mathf.Clamp(vertical, -1f, 1f);
        _rmThrust = Mathf.MoveTowards(_rmThrust, targetThrust, thrustSmoothSpeed * dt);

        // ── Yaw (maps to TurnAngle) ──
        float targetYaw = Mathf.Clamp(horizontal, -1f, 1f);
        _rmYaw = Mathf.Clamp(
            Mathf.SmoothDamp(_rmYaw, targetYaw, ref _rmYawVel, 1f / yawSmoothing),
            -1f, 1f);

        // ── Pitch ──
        float targetPitch = Mathf.Clamp(mouseY * ySign, -1f, 1f);
        _rmPitch = Mathf.Clamp(
            Mathf.SmoothDamp(_rmPitch, targetPitch, ref _rmPitchVel, 1f / pitchSmoothing),
            -1f, 1f);

        // Push Thrust and Yaw to ground controller → animator reads GaitSpeed and TurnAngle
        groundController.SetFlightAnimParams(_rmThrust, _rmYaw, _rmPitch);

        // Enable root motion on animator
        if (animator != null)
            animator.applyRootMotion = true;

        // Update hover toggle (Space still works)
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

        isHoverMode = hoverRequested && Mathf.Abs(vertical) < 0.05f;
        isFlapping = vertical > 0.05f;
        isGliding = !isHoverMode && !isFlapping;
    }

    private static float NormalizePitch(float xDegrees)
    {
        while (xDegrees > 180f) xDegrees -= 360f;
        while (xDegrees < -180f) xDegrees += 360f;
        return xDegrees;
    }
}