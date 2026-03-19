using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon swimming controller.
/// Activated by AnimalSwimSystem when the dragon enters water.
/// Disables DragonGroundController for the duration and re-enables on exit.
///
/// Animator parameters driven:
/// - IsSwimming     (bool)   — gates the swim state in the Animator
/// - WaterEnter     (trigger)— one-shot entry animation
/// - SwimSpeed      (float)  — 0..1 forward speed, drives forward/fast blend
/// - SwimTurn       (float)  — -1..+1 left/right, drives turn/strafe blend
/// - SwimVertical   (float)  — -1..+1 down/up (Ctrl = down, Space = up)
///
/// Root motion drives horizontal movement.
/// Vertical movement is applied directly via rigidbody.
/// </summary>
[RequireComponent(typeof(AnimalSwimSystem))]
public class DragonSwimController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonGroundController groundController;
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private AnimalSwimSystem       swimSystem;
    [SerializeField] private Animator               animator;
    [SerializeField] private Rigidbody              rb;

    [Header("Input")]
    [SerializeField] private string  forwardAxis      = "Vertical";
    [SerializeField] private string  horizontalAxis   = "Horizontal";
    [SerializeField] private KeyCode swimUpKey        = KeyCode.Space;
    [SerializeField] private KeyCode swimDownKey      = KeyCode.LeftControl;
    [SerializeField] private KeyCode sprintKey        = KeyCode.LeftShift;

    [Header("Swim Settings")]
    [SerializeField] private float swimSpeedSmoothing    = 3f;
    [SerializeField] private float swimTurnSmoothing     = 4f;
    [SerializeField] private float swimVerticalSmoothing = 3f;
    [SerializeField] private float verticalSpeed         = 4f;
    [SerializeField] private float maxPitchAngle         = 40f;
    [SerializeField] private float pitchSmoothing        = 3f;

    [Header("Surface Behaviour")]
    [Tooltip("Dragon automatically rises to surface when near it and no vertical input")]
    [SerializeField] private float surfaceBuoyancy = 2f;

    // ── Animator hashes ───────────────────────────────────────────
    private int isSwimmingHash;
    private int waterEnterHash;
    private int swimSpeedHash;
    private int swimTurnHash;
    private int swimVerticalHash;

    // ── State ─────────────────────────────────────────────────────
    private bool  _isSwimming;
    private float _swimSpeed;
    private float _swimTurn;
    private float _swimVertical;
    private float _currentPitch;

    // ── Public state ──────────────────────────────────────────────
    public bool IsSwimming => _isSwimming;

    // ═════════════════════════════════════════════════════════════
    private void Awake()
    {
        if (swimSystem       == null) swimSystem       = GetComponent<AnimalSwimSystem>();
        if (groundController == null) groundController = GetComponentInParent<DragonGroundController>();
        if (flightController == null) flightController = GetComponentInParent<DragonFlightController>();
        if (animator         == null) animator         = GetComponentInParent<Animator>();
        if (rb               == null) rb               = GetComponentInParent<Rigidbody>();

        isSwimmingHash   = Animator.StringToHash("IsSwimming");
        waterEnterHash   = Animator.StringToHash("WaterEnter");
        swimSpeedHash    = Animator.StringToHash("SwimSpeed");
        swimTurnHash     = Animator.StringToHash("SwimTurn");
        swimVerticalHash = Animator.StringToHash("SwimVertical");
    }

    private void Update()
    {
        if (!IsOwner) return;

        bool shouldSwim = swimSystem.IsInWater && !IsFlightActive();

        if (shouldSwim && !_isSwimming)
            EnterWater();
        else if (!shouldSwim && _isSwimming)
            ExitWater();

        if (_isSwimming)
            HandleSwimInput();
    }

    private void FixedUpdate()
    {
        if (!IsOwner || !_isSwimming || rb == null) return;

        // Vertical movement — root motion handles horizontal
        if (Mathf.Abs(_swimVertical) > 0.01f)
        {
            rb.MovePosition(rb.position + Vector3.up * _swimVertical * verticalSpeed * Time.fixedDeltaTime);
        }
        else if (swimSystem.IsNearSurface && swimSystem.SubmersionDepth > 0.1f)
        {
            // Gentle buoyancy nudge toward surface when idle and submerged
            rb.MovePosition(rb.position + Vector3.up * surfaceBuoyancy * Time.fixedDeltaTime);
        }

        // Pitch rotation for vertical movement
        ApplyPitch();
    }

    // ── Entry / Exit ──────────────────────────────────────────────
    private void EnterWater()
    {
        _isSwimming = true;

        // Disable ground controller so movement doesn't conflict
        if (groundController != null)
            groundController.enabled = false;

        // Disable real gravity — swim controller handles vertical
        if (rb != null)
        {
            rb.useGravity      = false;
            rb.linearVelocity  = Vector3.zero;
        }

        // Play entry animation
        if (animator != null)
        {
            animator.SetBool(isSwimmingHash, true);
            animator.SetTrigger(waterEnterHash);
        }
    }

    private void ExitWater()
    {
        _isSwimming  = false;
        _swimSpeed   = 0f;
        _swimTurn    = 0f;
        _swimVertical = 0f;
        _currentPitch = 0f;

        // Re-enable ground controller
        if (groundController != null)
            groundController.enabled = true;

        // Restore gravity
        if (rb != null)
            rb.useGravity = true;

        if (animator != null)
            animator.SetBool(isSwimmingHash, false);
    }

    // ── Input ─────────────────────────────────────────────────────
    private void HandleSwimInput()
    {
        if (animator == null) return;

        float vertical   = Input.GetAxisRaw(forwardAxis);
        float horizontal = Input.GetAxisRaw(horizontalAxis);
        bool  sprint     = Input.GetKey(sprintKey);

        // SwimSpeed: 0 = idle, 0.5 = normal, 1 = fast
        float targetSpeed = 0f;
        if (vertical > 0.1f)
            targetSpeed = sprint ? 1f : 0.5f;

        _swimSpeed = Mathf.MoveTowards(_swimSpeed, targetSpeed, swimSpeedSmoothing * Time.deltaTime);

        // SwimTurn: -1 left, +1 right
        float targetTurn = Mathf.Clamp(horizontal, -1f, 1f);
        _swimTurn = Mathf.MoveTowards(_swimTurn, targetTurn, swimTurnSmoothing * Time.deltaTime);

        // SwimVertical: Space = +1 (up), LeftCtrl = -1 (down)
        float targetVertical = 0f;
        if (Input.GetKey(swimUpKey))   targetVertical =  1f;
        if (Input.GetKey(swimDownKey)) targetVertical = -1f;
        _swimVertical = Mathf.MoveTowards(_swimVertical, targetVertical, swimVerticalSmoothing * Time.deltaTime);

        // Push parameters to Animator
        animator.SetFloat(swimSpeedHash,    _swimSpeed);
        animator.SetFloat(swimTurnHash,     _swimTurn);
        animator.SetFloat(swimVerticalHash, _swimVertical);
    }

    // ── Pitch ─────────────────────────────────────────────────────
    private void ApplyPitch()
    {
        if (rb == null) return;

        float targetPitch = -_swimVertical * maxPitchAngle; // negative = nose up when swimming up
        _currentPitch = Mathf.LerpAngle(_currentPitch, targetPitch, pitchSmoothing * Time.fixedDeltaTime);

        Vector3 euler = rb.rotation.eulerAngles;
        // Preserve yaw, apply pitch, zero roll
        Quaternion targetRot = Quaternion.Euler(_currentPitch, euler.y, 0f);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, pitchSmoothing * Time.fixedDeltaTime));
    }

    // ── Helpers ───────────────────────────────────────────────────
    private bool IsFlightActive()
    {
        if (flightController == null) return false;
        return flightController.IsFlying   ||
               flightController.IsHoverMode ||
               flightController.IsGliding  ||
               flightController.IsDiving;
    }
}
