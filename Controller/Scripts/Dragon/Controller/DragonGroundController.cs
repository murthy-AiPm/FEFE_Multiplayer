using UnityEngine;
using Unity.Netcode;

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

    [Header("Slope Alignment")]
    [SerializeField] private float slopeAlignmentSpeed = 5f;
    [SerializeField] private float slopeRaycastDistance = 3f;

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

    // Rotation
    private float turnSmoothVelocity;

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
        // All physics operations happen here
        if (!IsOwner) return;
        if (!isActive && !isFalling && !isLanding) return;

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
        //AlignToSlope();
        ApplyGravity();
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
            // float angle = Mathf.SmoothDampAngle(rb.rotation.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);
            if (groundAlignment != null)
                groundAlignment.UpdateTargetYaw(targetAngle);
            // Rotation
            Quaternion targetRotation = Quaternion.Euler(0f, targetAngle, 0f);

            Quaternion newRotation = Quaternion.Slerp(rb.rotation, targetRotation, Time.fixedDeltaTime / turnSmoothTime);
            rb.MoveRotation(newRotation);
            //rb.MoveRotation(Quaternion.Euler(0f, angle, 0f));

            // Movement
            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            float speed = sprint ? runSpeed : walkSpeed;
            rb.MovePosition(rb.position + moveDir.normalized * speed * Time.fixedDeltaTime);

            // Turn detection
            float angleDelta = Mathf.DeltaAngle(rb.rotation.eulerAngles.y, targetAngle);
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

    private void AlignToSlope()
    {
        if (rb == null || !groundingSystem.IsGrounded) return;

        // Raycast down from center to get ground normal
        Vector3 rayOrigin = rb.position + Vector3.up * 0.5f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, slopeRaycastDistance))
        {
            // Calculate pitch from ground normal
            Vector3 groundNormal = hit.normal;
            Vector3 forward = rb.transform.forward;

            // Get angle between forward and slope
            float slopeAngle = Vector3.Angle(Vector3.up, groundNormal);

            // Calculate target pitch (positive = uphill, negative = downhill)
            Vector3 slopeDirection = Vector3.Cross(groundNormal, rb.transform.right);
            float targetPitch = Vector3.Angle(forward, slopeDirection) - 90f;

            // Determine sign (uphill vs downhill)
            if (Vector3.Dot(forward, groundNormal) < 0)
                targetPitch = -targetPitch;

            // Get current rotation
            Vector3 currentEuler = rb.rotation.eulerAngles;
            float currentYaw = currentEuler.y;
            float currentPitch = currentEuler.x;
            if (currentPitch > 180f) currentPitch -= 360f;

            // Smooth lerp pitch
            float newPitch = Mathf.Lerp(currentPitch, targetPitch, Time.fixedDeltaTime * slopeAlignmentSpeed);

            // Apply rotation (preserve yaw, update pitch, zero roll)
            rb.MoveRotation(Quaternion.Euler(newPitch, currentYaw, 0f));
        }
    }

    private void ApplyGravity()
    {
        if (rb == null) return;

        // Apply gravity if not grounded
        if (!groundingSystem.IsGrounded)
        {
            Vector3 velocity = rb.linearVelocity;
            velocity.y += gravityValue * Time.fixedDeltaTime;
            rb.linearVelocity = velocity;
        }
        else
        {
            // Kill downward velocity when grounded
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

        // Apply jump velocity
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

        // Apply jump velocity
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

        // Allow takeoff during jump
        if (IsOwner && Input.GetKey(jumpKey))
        {
            EndJump();
            TriggerTakeoff();
            return;
        }

        // End jump when grounded or duration exceeded
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
        // Allow glide during fall
        if (IsOwner && Input.GetKey(jumpKey))
        {
            EndFall();
            groundingSystem.ResetFallingState();
            flightController.RequestGlide();
            return;
        }

        // Landing
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