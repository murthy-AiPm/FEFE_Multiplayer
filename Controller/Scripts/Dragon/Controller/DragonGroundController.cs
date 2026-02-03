using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Camera-relative ground movement for dragon.
/// W/A/S/D move relative to camera forward direction.
/// Dragon body smoothly rotates to face movement direction.
/// Only owner reads input. Exposes public state for DragonAnimatorController to sync.
/// </summary>
public class DragonGroundController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonGroundingSystem groundingSystem;
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private Animator animator;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform cameraTransform;  // Main camera or camera follow target

    [Header("Input")]
    [SerializeField] private string forwardAxis = "Vertical";      // W/S
    [SerializeField] private string strafeAxis = "Horizontal";     // A/D
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Takeoff")]
    [SerializeField] private float takeoffHoldTime = 0.4f;
    [SerializeField] private float jumpUpDuration = 1.11f;
    [SerializeField] private float jumpForwardDuration = 1.18f;

    [Header("Fall")]
    [SerializeField] private float landingDuration = 1.07f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 10f;

    [Header("Rotation")]
    [SerializeField] private float turnSpeed = 360f;               // Max degrees per second for body rotation
    [SerializeField] private float turnSmoothTime = 0.1f;          // Smoothing for turn-while-moving
    [SerializeField] private float snapThreshold = 150f;           // Angles > this use snap turn (180° backward)

    // Animator trigger hashes
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

    // Rotation smoothing
    private float currentTurnVelocity;
    private float targetYaw;

    // Public state - read by DragonAnimatorController for network sync
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
        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

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
            HandleGroundMovement();
    }

    private void HandleGroundMovement()
    {

        float vertical = Input.GetAxisRaw(forwardAxis);    // W/S
        float horizontal = Input.GetAxisRaw(strafeAxis);   // A/D
        bool sprint = Input.GetKey(sprintKey);
        bool spaceDown = Input.GetKey(jumpKey);
        bool spacePressed = Input.GetKeyDown(jumpKey);

        Vector2 input = new Vector2(horizontal, vertical);
        bool isMoving = input.magnitude > 0.1f;

        // ─── Calculate Camera-Relative Movement Direction ───
        Vector3 moveDirection = Vector3.zero;
        if (isMoving && cameraTransform != null)
        {
            // Camera forward/right projected on horizontal plane
            Vector3 camForward = cameraTransform.forward;
            Vector3 camRight = cameraTransform.right;

            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            // Combine input with camera axes
            moveDirection = (camForward * vertical + camRight * horizontal).normalized;
        }

        // ─── State Updates (for animator sync) ───
        IsWalking = isMoving && !sprint;
        IsRunning = isMoving && sprint;
        ForwardSpeed = vertical;  // Note: This is input, not actual movement direction

        // ─── Rotation: Body follows movement direction ───
        // In HandleGroundMovement, replace the rotation section:
        if (isMoving && moveDirection != Vector3.zero)
        {
            float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;

            Transform root = rb != null ? rb.transform : transform.parent;
            float currentAngle = root.rotation.eulerAngles.y;
            float angleDelta = Mathf.DeltaAngle(currentAngle, targetAngle);

            IsTurningLeft = angleDelta < -5f;
            IsTurningRight = angleDelta > 5f;
            TurnSpeed = Mathf.Abs(angleDelta) / 180f;

            float newAngle;
            if (Mathf.Abs(angleDelta) > snapThreshold)
                newAngle = targetAngle;
            else
                newAngle = Mathf.SmoothDampAngle(currentAngle, targetAngle, ref currentTurnVelocity, turnSmoothTime);

            Quaternion targetRotation = Quaternion.Euler(0f, newAngle, 0f);

            if (rb != null)
                rb.MoveRotation(targetRotation);
            else
                root.rotation = targetRotation;
        }
        else
        {
            // Not moving - clear turn state
            IsTurningLeft = false;
            IsTurningRight = false;
            TurnSpeed = 0f;
            currentTurnVelocity = 0f;
        }

        // ─── Translation: Move in calculated direction ───
        // ─── Translation: Move in calculated direction ───
        if (isMoving)
        {
            float speed = sprint ? runSpeed : walkSpeed;
            Vector3 movement = moveDirection * speed * Time.deltaTime;

            if (rb != null)
                rb.MovePosition(rb.position + movement);
            else
            {
                Transform root = transform.parent;
                root.position += movement;
            }
        }

        // ─── Jump / Takeoff ───
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
            if (isMoving)
                TriggerJumpForward();
            else
                TriggerJumpUp();
        }
        Debug.Log($"V:{vertical} H:{horizontal} CamNull:{cameraTransform == null} MoveDir:{moveDirection}");
    }

    // ─── Jump ────────────────────────────────────────────

    private void TriggerJumpUp()
    {
        isJumping = true;
        lastJumpWasForward = false;
        jumpTimer = 0f;
        ClearState();
        JumpUpServerRpc();
    }

    private void TriggerJumpForward()
    {
        isJumping = true;
        lastJumpWasForward = true;
        jumpTimer = 0f;
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

        if (IsOwner && Input.GetKey(jumpKey))
        {
            EndJump();
            TriggerTakeoff();
            return;
        }

        if (jumpTimer >= duration)
            EndJump();
    }

    private void EndJump()
    {
        isJumping = false;
        jumpTimer = 0f;
        ClearState();
    }

    // ─── Takeoff ─────────────────────────────────────────

    private void TriggerTakeoff()
    {
        isActive = false;
        isJumping = false;
        ClearState();
        groundingSystem.ResetFallingState();
        flightController.RequestHover();
    }

    // ─── Fall ────────────────────────────────────────────

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

    // ─── Landing ─────────────────────────────────────────

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
    }

    // ─── Helpers ─────────────────────────────────────────

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
        currentTurnVelocity = 0f;
    }
}