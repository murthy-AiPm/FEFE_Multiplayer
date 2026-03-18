using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon flight controller with stamina-driven flight modes:
/// - Flapping (W held + stamina > 0): Active flight, consumes stamina
/// - Glide (no W or stamina depleted): Passive descent, regenerates stamina
/// - Hover (Space toggle): Holds altitude, free stamina
/// - Dive (V hold): Fast descent, ignores stamina
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

    [Header("Input")]
    [SerializeField] private string horizontalAxis = "Horizontal";
    [SerializeField] private string verticalAxis = "Vertical";
    [SerializeField] private string hoverAxis = "Hover";
    [SerializeField] private KeyCode toggleHoverKey = KeyCode.Space;
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

    [Header("UI/Stats")]
    [SerializeField] private FlightStats flightStats;

    // Public read-only state
    public bool IsHoverMode => isHoverMode;
    public bool IsFlying => isFlapping;
    public bool IsFlapping => isFlapping;
    public bool IsGliding => isGliding;
    public bool IsDiving => isDiving;
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
    private bool isDiving;
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

    private Vector3 currentVelocity;
    private Vector3 lastPosition;

    private void Awake()
    {
        if (groundingSystem == null)
            groundingSystem = GetComponentInChildren<AnimalGroundingSystem>();

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

        ReadInput();
        UpdateRotation(Time.deltaTime);
        UpdateStaminaAndMovement(Time.deltaTime);
        UpdateUI();

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

        isDiving = Input.GetKey(diveKey);
    }

    // ─── Rotation ────────────────────────────────────────

    private void UpdateRotation(float dt)
    {
        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y");
        float ySign = invertY ? 1f : -1f;
        float ad = Input.GetAxisRaw(horizontalAxis);

        if (isDiving)
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
        float bankStrength = isDiving ? 1.1f : (isHoverMode ? 0.35f : 1f);
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

        // ─── Dive Mode (ignores stamina) ─────────────────
        if (isDiving)
        {
            isHoverMode = false;
            hoverRequested = false;
            isFlapping = false;
            isGliding = false;

            airSpeed = Mathf.MoveTowards(airSpeed, diveTargetSpeed, diveAccel * dt);
            ApplyMovement(transform.forward * airSpeed * dt);
            ApplyMovement(Vector3.down * (diveAccel * 0.5f) * dt);
            return;
        }

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

    private static float NormalizePitch(float xDegrees)
    {
        while (xDegrees > 180f) xDegrees -= 360f;
        while (xDegrees < -180f) xDegrees += 360f;
        return xDegrees;
    }
}