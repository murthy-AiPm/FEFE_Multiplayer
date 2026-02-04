using UnityEngine;
using Unity.Netcode;

public class DragonGroundController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonGroundingSystem groundingSystem;
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController controller;
    [SerializeField] private Transform cam;

    [Header("Input")]
    [SerializeField] private string forwardAxis = "Vertical";
    [SerializeField] private string strafeAxis = "Horizontal";
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Takeoff")]
    [SerializeField] private float takeoffHoldTime = 0.4f;

    [Header("Jump/Gravity")]
    [SerializeField] private float jumpHeight = 3f;
    [SerializeField] private float gravityValue = -9.81f;

    [Header("Fall")]
    [SerializeField] private float jumpUpDuration = 1.11f;
    [SerializeField] private float jumpForwardDuration = 1.18f;
    [SerializeField] private float landingDuration = 1.07f;
    [SerializeField] private float freeFallThreshold = -10f;

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
    private bool isFreeFalling;
    private bool isLanding;
    private float jumpTimer;
    private float spaceHoldTimer;
    private bool lastJumpWasForward;

    // Physics
    private Vector3 playerVelocity;
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
        if (controller == null)
            controller = GetComponentInParent<CharacterController>();
        if (cam == null)
            cam = Camera.main?.transform;

        jumpUpHash = Animator.StringToHash("JumpUp");
        jumpForwardHash = Animator.StringToHash("JumpForward");
    }

    private void Update()
    {
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

        if (isLanding)
        {
            HandleLanding();
            return;
        }

        if (isFalling)
        {
            HandleFall();
            return;
        }

        if (isJumping)
        {
            HandleJump();
            return;
        }

        if (IsOwner)
        {
            HandleGroundMovement();
           // AlignToSlope();
            ApplyGravity();
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
        if (isMoving && cam != null && controller != null)
        {
            Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            float angle = Mathf.SmoothDampAngle(transform.parent.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);

            // Rotation
            transform.parent.rotation = Quaternion.Euler(0f, angle, 0f);

            // Movement
            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            float speed = sprint ? runSpeed : walkSpeed;
            controller.Move(moveDir.normalized * speed * Time.deltaTime);

            // Turn detection
            float angleDelta = Mathf.DeltaAngle(transform.parent.eulerAngles.y, targetAngle);
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
            spaceHoldTimer += Time.deltaTime;
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
        if (controller == null || !groundingSystem.IsGrounded) return;

        // Raycast down from center to get ground normal
        Vector3 rayOrigin = transform.parent.position + Vector3.up * 0.5f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, slopeRaycastDistance))
        {
            // Calculate pitch from ground normal
            Vector3 groundNormal = hit.normal;
            Vector3 forward = transform.parent.forward;

            // Project forward onto ground plane
            Vector3 projectedForward = Vector3.ProjectOnPlane(forward, groundNormal).normalized;

            // Calculate pitch angle (rotation around right axis)
            float targetPitch = -Vector3.SignedAngle(forward, projectedForward, transform.parent.right);

            // Get current rotation
            Vector3 currentEuler = transform.parent.eulerAngles;
            float currentYaw = currentEuler.y;
            float currentPitch = currentEuler.x;

            // Normalize pitch to -180 to 180
           // if (currentPitch > 180f) currentPitch -= 360f;

            // Smooth lerp pitch
            float newPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * slopeAlignmentSpeed);

            // Apply rotation (preserve yaw, update pitch, zero roll)
            transform.parent.rotation = Quaternion.Euler(newPitch, currentYaw, 0f);
        }
    }

    private void ApplyGravity()
    {
        if (controller == null) return;

        // Free fall detection
        if (playerVelocity.y < freeFallThreshold && !groundingSystem.IsGrounded)
        {
            isFreeFalling = true;
        }

        // Reset velocity when grounded
        if (groundingSystem.IsGrounded && playerVelocity.y < 0)
        {
            playerVelocity.y = -2f; // Small downward force to keep grounded
            isFreeFalling = false;
        }

        // Apply gravity
        playerVelocity.y += gravityValue * Time.deltaTime;
        controller.Move(playerVelocity * Time.deltaTime);
    }

    private void TriggerJumpUp()
    {
        isJumping = true;
        lastJumpWasForward = false;
        jumpTimer = 0f;
        playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravityValue);
        ClearState();
        JumpUpServerRpc();
    }

    private void TriggerJumpForward()
    {
        isJumping = true;
        lastJumpWasForward = true;
        jumpTimer = 0f;
        playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravityValue);
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

    private void HandleJump()
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

        // Continue applying gravity during jump
        if (IsOwner)
            ApplyGravity();
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

    private void HandleFall()
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

        // Continue applying gravity during fall
        if (IsOwner)
            ApplyGravity();
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

    private void HandleLanding()
    {
        jumpTimer += Time.deltaTime;

        if (jumpTimer >= landingDuration)
        {
            isLanding = false;
            IsLanding = false;
            jumpTimer = 0f;
            isActive = true;
        }

        // Apply gravity during landing animation
        if (IsOwner)
            ApplyGravity();
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