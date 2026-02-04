using UnityEngine;
using Unity.Netcode;

public class DragonGroundController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonGroundingSystem groundingSystem;
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
    [SerializeField] private float bodyHeightOffset = 0.2f;  // Extra height above paws

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
        AdjustBodyHeight();
        AlignToSlope();
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
            float angle = Mathf.SmoothDampAngle(rb.rotation.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);

            // Rotation
            rb.MoveRotation(Quaternion.Euler(0f, angle, 0f));

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

    private void AdjustBodyHeight()
    {
        if (rb == null || !groundingSystem.IsGrounded) return;

        // Get 4 paw hit data from GroundingSystem
        var pawHits = groundingSystem.GetPawHits();

        // Calculate average ground height from valid paw hits
        float totalHeight = 0f;
        int validHits = 0;

        if (pawHits.leftHandHit.collider != null)
        {
            totalHeight += pawHits.leftHandHit.point.y;
            validHits++;
        }
        if (pawHits.rightHandHit.collider != null)
        {
            totalHeight += pawHits.rightHandHit.point.y;
            validHits++;
        }
        if (pawHits.leftFootHit.collider != null)
        {
            totalHeight += pawHits.leftFootHit.point.y;
            validHits++;
        }
        if (pawHits.rightFootHit.collider != null)
        {
            totalHeight += pawHits.rightFootHit.point.y;
            validHits++;
        }

        if (validHits >= 3)  // Need at least 3 paws hitting ground
        {
            float averageGroundHeight = totalHeight / validHits;
            float targetY = averageGroundHeight + bodyHeightOffset;

            // Smoothly adjust body height
            Vector3 pos = rb.position;
            pos.y = Mathf.Lerp(pos.y, targetY, Time.fixedDeltaTime * slopeAlignmentSpeed);
            rb.MovePosition(pos);
        }
    }

    private void AlignToSlope()
    {
        if (rb == null || !groundingSystem.IsGrounded) return;

        // Get 4 paw hit data from GroundingSystem
        var pawHits = groundingSystem.GetPawHits();

        // Calculate ground normal from 4 paw positions
        Vector3 groundNormal = CalculateGroundNormal(pawHits);

        if (groundNormal == Vector3.zero)
        {
            // No valid ground plane, keep current rotation
            return;
        }

        // Calculate target rotation to align with ground normal
        Vector3 currentForward = rb.transform.forward;
        Vector3 currentRight = rb.transform.right;

        // Project forward onto ground plane
        Vector3 projectedForward = Vector3.ProjectOnPlane(currentForward, groundNormal).normalized;
        Vector3 projectedRight = Vector3.ProjectOnPlane(currentRight, groundNormal).normalized;

        // Build target rotation from projected vectors
        Quaternion targetRotation = Quaternion.LookRotation(projectedForward, groundNormal);

        // Get current yaw (preserve it from movement)
        float currentYaw = rb.rotation.eulerAngles.y;

        // Extract pitch and roll from target rotation
        Vector3 targetEuler = targetRotation.eulerAngles;
        float targetPitch = targetEuler.x;
        float targetRoll = targetEuler.z;

        // Normalize angles
        if (targetPitch > 180f) targetPitch -= 360f;
        if (targetRoll > 180f) targetRoll -= 360f;

        // Get current pitch and roll
        Vector3 currentEuler = rb.rotation.eulerAngles;
        float currentPitch = currentEuler.x;
        float currentRoll = currentEuler.z;
        if (currentPitch > 180f) currentPitch -= 360f;
        if (currentRoll > 180f) currentRoll -= 360f;

        // Smooth lerp pitch and roll while preserving yaw
        float newPitch = Mathf.Lerp(currentPitch, targetPitch, Time.fixedDeltaTime * slopeAlignmentSpeed);
        float newRoll = Mathf.Lerp(currentRoll, targetRoll, Time.fixedDeltaTime * slopeAlignmentSpeed);

        // Apply rotation (preserve yaw from movement, update pitch/roll from slope)
        rb.MoveRotation(Quaternion.Euler(newPitch, currentYaw, newRoll));
    }

    private Vector3 CalculateGroundNormal(DragonGroundingSystem.PawHitInfo pawHits)
    {
        // Count valid hits
        int validHits = 0;
        Vector3 leftHandPos = Vector3.zero;
        Vector3 rightHandPos = Vector3.zero;
        Vector3 leftFootPos = Vector3.zero;
        Vector3 rightFootPos = Vector3.zero;

        if (pawHits.leftHandHit.collider != null)
        {
            leftHandPos = pawHits.leftHandHit.point;
            validHits++;
        }
        if (pawHits.rightHandHit.collider != null)
        {
            rightHandPos = pawHits.rightHandHit.point;
            validHits++;
        }
        if (pawHits.leftFootHit.collider != null)
        {
            leftFootPos = pawHits.leftFootHit.point;
            validHits++;
        }
        if (pawHits.rightFootHit.collider != null)
        {
            rightFootPos = pawHits.rightFootHit.point;
            validHits++;
        }

        // Need at least 3 points to calculate a plane
        if (validHits < 3)
            return Vector3.zero;

        // Method 1: Use front paws and one back paw (most stable)
        if (pawHits.leftHandHit.collider != null && pawHits.rightHandHit.collider != null)
        {
            // Front vector: left hand to right hand
            Vector3 frontVector = rightHandPos - leftHandPos;

            // Side vector: use whichever back paw is available
            Vector3 sideVector;
            if (pawHits.leftFootHit.collider != null)
            {
                sideVector = leftFootPos - leftHandPos;
            }
            else if (pawHits.rightFootHit.collider != null)
            {
                sideVector = rightFootPos - rightHandPos;
            }
            else
            {
                // Only have front paws, use average normal from both
                return ((pawHits.leftHandHit.normal + pawHits.rightHandHit.normal) * 0.5f).normalized;
            }

            // Cross product gives perpendicular (up) vector
            Vector3 normal = Vector3.Cross(frontVector, sideVector).normalized;

            // Ensure normal points upward
            if (normal.y < 0)
                normal = -normal;

            return normal;
        }

        // Method 2: Fallback - average all hit normals
        Vector3 averageNormal = Vector3.zero;
        if (pawHits.leftHandHit.collider != null) averageNormal += pawHits.leftHandHit.normal;
        if (pawHits.rightHandHit.collider != null) averageNormal += pawHits.rightHandHit.normal;
        if (pawHits.leftFootHit.collider != null) averageNormal += pawHits.leftFootHit.normal;
        if (pawHits.rightFootHit.collider != null) averageNormal += pawHits.rightFootHit.normal;

        return (averageNormal / validHits).normalized;
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