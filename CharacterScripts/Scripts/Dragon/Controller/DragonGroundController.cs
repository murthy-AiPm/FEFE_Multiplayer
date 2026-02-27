using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon ground movement controller.
/// 
/// KEY CHANGES:
/// - Rotation is FULLY delegated to DragonGroundAlignment (only updates targetYaw)
/// - Movement is slope-projected (moves along ground plane, not horizontally)
/// - No direct rb.MoveRotation() calls
/// </summary>
public class DragonGroundController : NetworkBehaviour
{
    [Header("Testing")]
    [SerializeField] private bool ignoreOwnershipForTesting = false;

    [Header("References")]
    [SerializeField] private DragonGroundingSystem groundingSystem;
    [SerializeField] private DragonGroundAlignment groundAlignment;
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private Animator animator;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform cam;

    [Header("Input")]
    [SerializeField] private string forwardAxis = "Vertical";
    [SerializeField] private string strafeAxis = "Horizontal";
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private KeyCode takeoffKey = KeyCode.C;
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Takeoff")]
    [SerializeField] private float takeoffLiftSpeed = 8f;
    [SerializeField] private float jumpUpAnimationDuration = 0.8f;  // Time before transitioning to hover

    [Header("Jump Animation")]
    [SerializeField] private float jumpAnimationDuration = 1.18f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 10f;
    [SerializeField] private float turnSmoothTime = 0.1f;
    [SerializeField] private float turnHeadAngle = 18;

    [Header("Fake Gravity (for horse)")]
    [SerializeField] private bool useFakeGravity = false;
    [SerializeField] private float fakeGravity = 20f;
    [SerializeField] private float maxFallSpeed = 30f;
    private float _fallVelocity;
    // Animator hashes
    private int jumpForwardHash;
    private int jumpUpHash;

    // State
    private bool isActive;
    private bool isPlayingJump;  // Jump animation playing (still grounded)
    private bool isTakingOff;    // Lifting up to hover
    private float stateTimer;

    // Cached ground normal for slope movement
    private Vector3 currentGroundNormal = Vector3.up;

    // Public state
    public bool IsWalking { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsPlayingJump { get; private set; }
    public bool IsTakingOff { get; private set; }
    public bool IsTurningLeft { get; private set; }
    public bool IsTurningRight { get; private set; }
    public float ForwardSpeed { get; private set; }
    public float TurnSpeed { get; private set; }

    private void Awake()
    {
        if (groundingSystem == null)
            groundingSystem = GetComponent<DragonGroundingSystem>();
        if (groundAlignment == null)
            groundAlignment = GetComponent<DragonGroundAlignment>();
        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();
        if (animator == null)
            animator = GetComponent<Animator>();
        if (rb == null)
            rb = GetComponentInParent<Rigidbody>();
        if (cam == null)
            cam = Camera.main?.transform;

        jumpForwardHash = Animator.StringToHash("JumpForward");
        jumpUpHash = Animator.StringToHash("JumpUp");

        // Enable root motion - we'll disable it when flying
        if (animator != null)
            animator.applyRootMotion = true;
    }

    private void Update()
    {

        // Takeoff in progress - handled separately
        if (isTakingOff)
        {
            HandleTakeoffState();
            return;
        }

        // Jump animation playing (still grounded, just animating)
        if (isPlayingJump)
        {
            HandleJumpAnimation();
            return;
        }

        // Normal grounded state
        if (groundingSystem.IsGrounded)
        {
            if (!isActive) OnBecameGrounded();
            isActive = true;
        }
        else
        {
            isActive = false;
        }
    }

    private void FixedUpdate()
    {
        //if (useFakeGravity)
        //{
        //    if (groundingSystem.IsGrounded)
        //        _fallVelocity = 0f;
        //    else
        //    {
        //        _fallVelocity += fakeGravity * Time.fixedDeltaTime;
        //        _fallVelocity = Mathf.Min(_fallVelocity, maxFallSpeed);
        //        rb.MovePosition(rb.position + Vector3.down * _fallVelocity * Time.fixedDeltaTime);
        //    }
        //}
        if (!ignoreOwnershipForTesting && !IsOwner) return;

        // Takeoff - lift dragon up
        if (isTakingOff)
        {
            ApplyTakeoffLift();
            return;
        }

        // Jump animation playing - root motion handles movement
        if (isPlayingJump) return;

        // Not active - do nothing
        if (!isActive) return;

        // Update cached ground normal
        UpdateGroundNormal();

        // Normal ground movement
        HandleGroundMovement();
    }

    private void UpdateGroundNormal()
    {
        if (groundingSystem == null || !groundingSystem.IsGrounded)
        {
            currentGroundNormal = Vector3.up;
            return;
        }

        // Get averaged normal from paw hits
        var hits = groundingSystem.GetPawHits();
        Vector3 sum = Vector3.zero;
        int count = 0;

        if (hits.leftHandHit.collider != null) { sum += hits.leftHandHit.normal; count++; }
        if (hits.rightHandHit.collider != null) { sum += hits.rightHandHit.normal; count++; }
        if (hits.leftFootHit.collider != null) { sum += hits.leftFootHit.normal; count++; }
        if (hits.rightFootHit.collider != null) { sum += hits.rightFootHit.normal; count++; }

        if (count >= 2)
        {
            currentGroundNormal = (sum / count).normalized;
            if (currentGroundNormal.y < 0f)
                currentGroundNormal = -currentGroundNormal;
        }
        else
        {
            currentGroundNormal = Vector3.up;
        }
    }

    private void HandleGroundMovement()
    {
        float vertical = Input.GetAxisRaw(forwardAxis);
        float horizontal = Input.GetAxisRaw(strafeAxis);
        bool sprint = Input.GetKey(sprintKey);
        Vector2 input = new Vector2(horizontal, vertical);
        bool isMoving = input.magnitude > 0.1f;

        // State
        IsWalking = isMoving && !sprint;
        IsRunning = isMoving && sprint;
        ForwardSpeed = vertical;

        // Camera-relative movement
        if (isMoving && cam != null && rb != null)
        {
            Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;

            // ONLY update yaw - let DragonGroundAlignment handle actual rotation
            if (groundAlignment != null)
                groundAlignment.UpdateTargetYaw(targetAngle);

            // Slope-projected movement
            Vector3 worldForward = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            Vector3 moveDir = ProjectOnSlope(worldForward, currentGroundNormal);

            float speed = sprint ? runSpeed : walkSpeed;
            rb.MovePosition(rb.position + moveDir * speed * Time.fixedDeltaTime);

            // Turn detection (for animation)
            float currentYaw = rb.rotation.eulerAngles.y;
            float angleDelta = Mathf.DeltaAngle(currentYaw, targetAngle);
            IsTurningLeft = angleDelta < -turnHeadAngle;
            IsTurningRight = angleDelta > turnHeadAngle;
            TurnSpeed = Mathf.Abs(angleDelta) / 180f;
        }
        else
        {
            IsTurningLeft = false;
            IsTurningRight = false;
            TurnSpeed = 0f;
        }

        // Jump animation (Space) - just plays animation, stays grounded
        if (Input.GetKey(jumpKey) && groundingSystem.IsGrounded /*&& !isPlayingJump*/)
        {
            TriggerJumpAnimation();
        }

        // Takeoff (C) - lifts dragon up to hover
        if (Input.GetKey(takeoffKey) && groundingSystem.IsGrounded)
        {
            TriggerTakeoff();
        }
    }

    /// <summary>
    /// Projects a direction onto the slope plane, maintaining magnitude.
    /// This ensures movement goes "along" the slope rather than into it.
    /// </summary>
    private Vector3 ProjectOnSlope(Vector3 direction, Vector3 groundNormal)
    {
        // Project onto plane defined by ground normal
        Vector3 projected = Vector3.ProjectOnPlane(direction, groundNormal);

        // Maintain original magnitude (so speed stays consistent on slopes)
        if (projected.sqrMagnitude > 0.0001f)
            projected = projected.normalized;

        return projected;
    }

    // ═══════════════════════════════════════════════════════════════
    // JUMP ANIMATION (Space) - stays grounded, just plays animation
    // ═══════════════════════════════════════════════════════════════

    private void TriggerJumpAnimation()
    {
        isPlayingJump = true;
        IsPlayingJump = true;
        //stateTimer = 0f;
        JumpForwardServerRpc();
    }

    [ServerRpc]
    private void JumpForwardServerRpc()
    {
        JumpForwardClientRpc();
    }

    [ClientRpc]
    private void JumpForwardClientRpc()
    {
        animator.SetTrigger(jumpForwardHash);
    }

    private void HandleJumpAnimation()
    {
        stateTimer += Time.deltaTime;

        if (stateTimer >= jumpAnimationDuration)
        {
            isPlayingJump = false;
            IsPlayingJump = false;
            stateTimer = 0f;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TAKEOFF (C) - lifts dragon up until raycasts miss, then hover
    // ═══════════════════════════════════════════════════════════════

    private void TriggerTakeoff()
    {
        isTakingOff = true;
        IsTakingOff = true;
        isActive = false;
        isPlayingJump = false;
        IsPlayingJump = false;
        stateTimer = 0f;
        ClearState();

        // Play JumpUp animation
        JumpUpServerRpc();
    }

    [ServerRpc]
    private void JumpUpServerRpc()
    {
        JumpUpClientRpc();
    }

    [ClientRpc]
    private void JumpUpClientRpc()
    {
        animator.SetTrigger(jumpUpHash);
    }

    private void ApplyTakeoffLift()
    {
        // Lift dragon up (in addition to any root motion from animation)
        Vector3 newPos = rb.position;
        newPos.y += takeoffLiftSpeed * Time.fixedDeltaTime;
        rb.MovePosition(newPos);
    }

    private void HandleTakeoffState()
    {
        stateTimer += Time.deltaTime;

        // After JumpUp animation duration, transition to hover
        if (stateTimer >= jumpUpAnimationDuration)
        {
            isTakingOff = false;
            IsTakingOff = false;
            stateTimer = 0f;

            // Disable root motion for flight
            if (animator != null)
                animator.applyRootMotion = false;

            groundingSystem.ResetFallingState();
            flightController.RequestHover();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // STATE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    private void OnBecameGrounded()
    {
        isActive = true;
        ClearState();

        // Sync yaw to current rotation so dragon doesn't snap
        if (groundAlignment != null)
        {
            float currentYaw = rb.rotation.eulerAngles.y;
            groundAlignment.SetYawImmediate(currentYaw);
        }

        // Re-enable root motion for ground movement
        if (animator != null)
            animator.applyRootMotion = true;
    }

    /// <summary>
    /// Captures animation root motion and applies it to the rigidbody.
    /// This makes the camera follow the dragon during jump animations.
    /// Requires "Apply Root Motion" checked on the Animator.
    /// </summary>
    private void OnAnimatorMove()
    {
        if (animator == null || rb == null) return;
        if (!IsOwner) return;

        // During jump or takeoff, apply animation root motion to rigidbody
        if (isPlayingJump /*|| isTakingOff*/)
        {
            // Apply position delta from animation
            Vector3 deltaPos = animator.deltaPosition;
            rb.MovePosition(rb.position + deltaPos);

            // Apply rotation delta from animation (optional - comment out if you don't want it)
            // rb.MoveRotation(rb.rotation * animator.deltaRotation);
        }
    }

    private void ClearState()
    {
        IsWalking = false;
        IsRunning = false;
        IsTurningLeft = false;
        IsTurningRight = false;
        ForwardSpeed = 0f;
        TurnSpeed = 0f;
    }
}