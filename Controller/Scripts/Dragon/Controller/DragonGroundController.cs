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
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Takeoff")]
    [SerializeField] private float takeoffHoldTime = 0.4f;

    [Header("Jump/Gravity")]
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private float gravityValue = -20f;

    [Header("Fall")]
    [SerializeField] private float jumpUpDuration = 1.11f;
    [SerializeField] private float jumpForwardDuration = 1.18f;
    [SerializeField] private float landingDuration = 1.07f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 10f;
    [SerializeField] private float turnSmoothTime = 0.1f;

    // Animator hashes
    private int jumpUpHash;
    private int jumpForwardHash;

    // State
    private bool isActive;
    private bool isJumping;
    private bool isFalling;
    private bool isLanding;
    private float jumpTimer;
    private float spaceHoldTimer;
    private bool lastJumpWasForward;

    // Cached ground normal for slope movement
    private Vector3 currentGroundNormal = Vector3.up;

    // Public state
    public bool IsWalking { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsFalling { get; private set; }
    public bool IsLanding { get; private set; }
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

        jumpUpHash = Animator.StringToHash("JumpUp");
        jumpForwardHash = Animator.StringToHash("JumpForward");
    }

    private void Update()
    {
        // State management only - no physics
        if (groundingSystem.IsGrounded && !isFalling && !isJumping && !isLanding)
        {
            if (!isActive) OnBecameGrounded();
            isActive = true;
        }

        if (IsOwner && isActive && groundingSystem.IsFalling && !isFalling && !isJumping)
        {
            StartFall();
        }

        if (!isActive && !isFalling && !isLanding) return;

        // State priority check
        if (isLanding)
        {
            HandleLandingState();
            return;
        }

        if (isFalling)
        {
            HandleFallState();
            return;
        }

        if (isJumping)
        {
            HandleJumpState();
            return;
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        if (!isActive && !isFalling && !isLanding) return;

        // Update cached ground normal
        UpdateGroundNormal();

        if (isLanding)
        {
            ApplyGravity();
            return;
        }

        if (isFalling)
        {
            ApplyGravity();
            return;
        }

        if (isJumping)
        {
            ApplyGravity();
            return;
        }

        // Normal ground movement
        HandleGroundMovement();
        ApplyGravity();
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
        bool spaceDown = Input.GetKey(jumpKey);
        bool spacePressed = Input.GetKeyDown(jumpKey);

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
            IsTurningLeft = angleDelta < -5f;
            IsTurningRight = angleDelta > 5f;
            TurnSpeed = Mathf.Abs(angleDelta) / 180f;
        }
        else
        {
            IsTurningLeft = false;
            IsTurningRight = false;
            TurnSpeed = 0f;
        }

        // Jump/Takeoff
        if (spacePressed)
            spaceHoldTimer = 0f;

        if (spaceDown)
        {
            spaceHoldTimer += Time.fixedDeltaTime;
            if (spaceHoldTimer >= takeoffHoldTime)
            {
                TriggerTakeoff();
                return;
            }
        }

        if (Input.GetKeyUp(jumpKey) && spaceHoldTimer < takeoffHoldTime)
        {
            if (groundingSystem.IsGrounded)
            {
                if (isMoving)
                    TriggerJumpForward();
                else
                    TriggerJumpUp();
            }
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

    private void ApplyGravity()
    {
        if (rb == null) return;

        if (!groundingSystem.IsGrounded)
        {
            Vector3 velocity = rb.linearVelocity;
            velocity.y += gravityValue * Time.fixedDeltaTime;
            rb.linearVelocity = velocity;
        }
        else
        {
            // When grounded, kill downward velocity
            Vector3 velocity = rb.linearVelocity;
            if (velocity.y < 0)
                velocity.y = 0f;
            rb.linearVelocity = velocity;
        }
    }

    private void TriggerJumpUp()
    {
        isJumping = true;
        lastJumpWasForward = false;
        jumpTimer = 0f;

        Vector3 velocity = rb.linearVelocity;
        velocity.y = jumpForce;
        rb.linearVelocity = velocity;

        ClearState();
        JumpUpServerRpc();
    }

    private void TriggerJumpForward()
    {
        isJumping = true;
        lastJumpWasForward = true;
        jumpTimer = 0f;

        Vector3 velocity = rb.linearVelocity;
        velocity.y = jumpForce;
        rb.linearVelocity = velocity;

        ClearState();
        JumpForwardServerRpc();
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

    private void HandleJumpState()
    {
        jumpTimer += Time.deltaTime;
        float duration = lastJumpWasForward ? jumpForwardDuration : jumpUpDuration;

        if (IsOwner && Input.GetKey(jumpKey))
        {
            EndJump();
            TriggerTakeoff();
            return;
        }

        if (groundingSystem.IsGrounded || jumpTimer >= duration)
        {
            EndJump();
        }
    }

    private void EndJump()
    {
        isJumping = false;
        jumpTimer = 0f;
        ClearState();
    }

    private void TriggerTakeoff()
    {
        isActive = false;
        isJumping = false;
        ClearState();
        groundingSystem.ResetFallingState();
        flightController.RequestHover();
    }

    private void StartFall()
    {
        isFalling = true;
        isActive = false;
        jumpTimer = 0f;
        ClearState();
        IsFalling = true;
    }

    private void HandleFallState()
    {
        if (IsOwner && Input.GetKey(jumpKey))
        {
            EndFall();
            groundingSystem.ResetFallingState();
            flightController.RequestGlide();
            return;
        }

        if (groundingSystem.IsGrounded)
        {
            EndFall();
            StartLanding();
        }
    }

    private void EndFall()
    {
        isFalling = false;
        IsFalling = false;
        jumpTimer = 0f;
    }

    private void StartLanding()
    {
        isLanding = true;
        IsLanding = true;
        jumpTimer = 0f;
    }

    private void HandleLandingState()
    {
        jumpTimer += Time.deltaTime;

        if (jumpTimer >= landingDuration)
        {
            isLanding = false;
            IsLanding = false;
            jumpTimer = 0f;
            isActive = true;
        }
    }

    private void OnBecameGrounded()
    {
        isActive = true;
        ClearState();
    }

    private void ClearState()
    {
        IsWalking = false;
        IsRunning = false;
        IsFalling = false;
        IsLanding = false;
        IsTurningLeft = false;
        IsTurningRight = false;
        ForwardSpeed = 0f;
        TurnSpeed = 0f;
    }
}