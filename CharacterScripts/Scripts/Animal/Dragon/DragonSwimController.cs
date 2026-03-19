using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon swimming controller.
/// Activated by AnimalSwimSystem when the dragon enters water.
/// DragonGroundController stays enabled — it handles movement, turning, alignment.
/// This controller only drives swim Animator parameters and vertical movement.
///
/// Animator parameters driven:
/// - IsSwimming     (bool)   — gates the swim state in the Animator
/// - SwimSpeed      (float)  — 0..1 forward speed
/// - SwimTurn       (float)  — -1..+1 left/right
/// - SwimVertical   (float)  — -1..+1 down/up (Ctrl = down, Space = up)
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
    [SerializeField] private Transform              cam;

    [Header("Input")]
    [SerializeField] private string  forwardAxis    = "Vertical";
    [SerializeField] private string  horizontalAxis = "Horizontal";
    [SerializeField] private KeyCode swimUpKey      = KeyCode.Space;
    [SerializeField] private KeyCode swimDownKey    = KeyCode.LeftControl;
    [SerializeField] private KeyCode sprintKey      = KeyCode.LeftShift;

    [Header("Swim Settings")]
    [SerializeField] private float swimSpeedSmoothing    = 3f;
    [SerializeField] private float swimTurnSmoothing     = 4f;
    [SerializeField] private float swimVerticalSmoothing = 3f;
    [SerializeField] private float verticalSpeed         = 4f;
    [SerializeField] private float maxPitchAngle         = 40f;
    [SerializeField] private float pitchSmoothing        = 3f;

    [Header("Surface Behaviour")]
    [SerializeField] private float surfaceBuoyancy = 2f;

    // ── Animator hashes ───────────────────────────────────────────
    private int isSwimmingHash;
    private int swimSpeedHash;
    private int swimTurnHash;
    private int swimVerticalHash;

    // ── State ─────────────────────────────────────────────────────
    private bool  _isSwimming;
    private float _swimSpeed;
    private float _swimTurn;
    private float _swimVertical;
    private float _currentPitch;

    public bool IsSwimming => _isSwimming;

    // ═════════════════════════════════════════════════════════════
    private void Awake()
    {
        if (swimSystem       == null) swimSystem       = GetComponent<AnimalSwimSystem>();
        if (groundController == null) groundController = GetComponentInParent<DragonGroundController>();
        if (flightController == null) flightController = GetComponentInParent<DragonFlightController>();
        if (animator         == null) animator         = GetComponentInParent<Animator>();
        if (rb               == null) rb               = GetComponentInParent<Rigidbody>();
        if (cam              == null) cam              = Camera.main?.transform;

        isSwimmingHash   = Animator.StringToHash("IsSwimming");
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

        // Vertical movement
        if (Mathf.Abs(_swimVertical) > 0.01f)
        {
            rb.MovePosition(rb.position + Vector3.up * _swimVertical * verticalSpeed * Time.fixedDeltaTime);
        }
        else if (swimSystem.IsNearSurface && swimSystem.SubmersionDepth > 0.1f)
        {
            rb.MovePosition(rb.position + Vector3.up * surfaceBuoyancy * Time.fixedDeltaTime);
        }

        ApplyPitch();
    }

    // ── Entry / Exit ──────────────────────────────────────────────
    private void EnterWater()
    {
        _isSwimming = true;
        if (groundController != null) groundController.SetSwimming(true);
        if (animator != null) animator.SetBool(isSwimmingHash, true);
    }

    private void ExitWater()
    {
        _isSwimming   = false;
        _swimSpeed    = 0f;
        _swimTurn     = 0f;
        _swimVertical = 0f;
        _currentPitch = 0f;
        if (groundController != null) groundController.SetSwimming(false);
        if (animator != null) animator.SetBool(isSwimmingHash, false);
    }

    // ── Input ─────────────────────────────────────────────────────
    private void HandleSwimInput()
    {
        if (animator == null) return;

        float vertical   = Input.GetAxisRaw(forwardAxis);
        float horizontal = Input.GetAxisRaw(horizontalAxis);
        bool  sprint     = Input.GetKey(sprintKey);

        float targetSpeed = 0f;
        if (vertical > 0.1f)
            targetSpeed = sprint ? 1f : 0.5f;

        _swimSpeed = Mathf.MoveTowards(_swimSpeed, targetSpeed, swimSpeedSmoothing * Time.deltaTime);

        // SwimTurn driven by camera/dragon yaw delta — only when moving, same as ground TurnAngle
        float targetTurn = 0f;
        if (vertical > 0.1f && cam != null && rb != null)
        {
            float angleDelta = Mathf.DeltaAngle(rb.rotation.eulerAngles.y, cam.eulerAngles.y);
            targetTurn = Mathf.Clamp(angleDelta / 30f, -1f, 1f);
        }
        _swimTurn = Mathf.Lerp(_swimTurn, targetTurn, swimTurnSmoothing * Time.deltaTime);

        float targetVertical = 0f;
        if (Input.GetKey(swimUpKey))   targetVertical =  1f;
        if (Input.GetKey(swimDownKey)) targetVertical = -1f;
        _swimVertical = Mathf.MoveTowards(_swimVertical, targetVertical, swimVerticalSmoothing * Time.deltaTime);

        animator.SetFloat(swimSpeedHash,    _swimSpeed);
        animator.SetFloat(swimTurnHash,     _swimTurn);
        animator.SetFloat(swimVerticalHash, _swimVertical);
    }

    // ── Pitch ─────────────────────────────────────────────────────
    private void ApplyPitch()
    {
        if (rb == null) return;

        float targetPitch = -_swimVertical * maxPitchAngle;
        _currentPitch = Mathf.LerpAngle(_currentPitch, targetPitch, pitchSmoothing * Time.fixedDeltaTime);

        Vector3    euler     = rb.rotation.eulerAngles;
        Quaternion targetRot = Quaternion.Euler(_currentPitch, euler.y, 0f);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, pitchSmoothing * Time.fixedDeltaTime));
    }

    // ── Helpers ───────────────────────────────────────────────────
    private bool IsFlightActive()
    {
        if (flightController == null) return false;
        return flightController.IsFlying    ||
               flightController.IsHoverMode ||
               flightController.IsGliding   ||
               flightController.IsDiving;
    }
}
