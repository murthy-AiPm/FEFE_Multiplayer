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
public class AnimalGroundController : NetworkBehaviour
{
    [Header("Testing")]
    [SerializeField] private bool ignoreOwnershipForTesting = false;

    [Header("Debug Respawn")]
    [SerializeField] private Transform debugRespawnPoint;
    [SerializeField] private KeyCode debugRespawnKey = KeyCode.U;

    [Header("References")]
    [SerializeField] protected AnimalGroundingSystem groundingSystem;
    [SerializeField] protected AnimalGroundAlignment groundAlignment;
    [SerializeField] protected Animator animator;
    [SerializeField] protected Rigidbody rb;
    [SerializeField] protected Transform cam;

    [Header("Input")]
    [SerializeField] private string forwardAxis = "Vertical";
    [SerializeField] private string strafeAxis = "Horizontal";
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    private KeyCode takeoffKey = KeyCode.C;
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Takeoff")]
    [SerializeField] private float takeoffLiftSpeed = 8f;
    [SerializeField] private float jumpUpAnimationDuration = 0.8f;  // Time before transitioning to hover

    [Header("Jump Animation")]
    [SerializeField] private float jumpAnimationDuration = 1.18f;

    [Header("Root Motion Per State")]
    [SerializeField] private bool rootMotionOnJump = true;
    [SerializeField] private bool rootMotionOnCliff = false;
    [SerializeField] private bool rootMotionOnFalling = false;
    [SerializeField] private bool rootMotionOnLanding = true;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float trotSpeed = 8f;
    [SerializeField] private float runSpeed = 14f;
    [SerializeField] private float turnSmoothTime = 0.1f;
    [SerializeField] private float turnHeadAngle = 18;

    [Header("Gait")]
    [SerializeField] private KeyCode trotToggleKey = KeyCode.T;
    [SerializeField] private float turnAngleSmoothing = 5f;
    [SerializeField] private float gaitSmoothSpeed = 2f;
    [SerializeField] private float minInputHoldTime = 0.15f;

    private float _inputHoldTimer;
    private bool _inputConfirmed;

    [Header("Turn Rate (deg/sec)")]
    [SerializeField] private float walkTurnRate = 90f;
    [SerializeField] private float trotTurnRate = 120f;
    [SerializeField] private float sprintTurnRate = 160f;

    [Header("Air Steering")]
    [Tooltip("Turn rate (deg/sec) when airborne (jump or free fall).")]
    [SerializeField] private float airTurnRate = 120f;

    [Header("Root Motion")]
    [SerializeField] protected bool useRootMotion = false;
    public bool UseRootMotion => useRootMotion;


    [Header("Fake Gravity (for horse)")]
    [SerializeField] private bool useFakeGravity = false;
    [SerializeField] private float fakeGravity = 20f;
    [SerializeField] private float maxFallSpeed = 30f;
    private float _fallVelocity;
    // Animator hashes
    private int jumpForwardHash;
    private int jumpUpHash;

    //falling
    [Header("Cliff Fall")]
    [SerializeField] private float cliffFallPush = 2f;
    private bool _wasOnCliff = false;
    private int isFreeFallingHash;
    private int isOnCliffHash;
    private int isLandingHash;
    private bool _pendingRespawn = false;
    // State
    private bool isActive;
    private float _turnAngleRaw;
    private float _turnAngleVel;
    private float _cappedYaw;  // yaw that moves toward targetAngle at a capped rate
    private bool isPlayingJump;  // Jump animation playing (still grounded)
    private bool isTakingOff;    // Lifting up to hover
    private bool _isAirSteering; // Player is steering mid-air (jump or free fall)
    private float stateTimer;
    private float _lostGroundTimer; // prevents isActive flipping on single-frame grounding gaps

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
    public float TurnAngle { get; protected set; }   // -1 left .. +1 right
    public float GaitSpeed { get; protected set; }   // 0 walk, 1 trot, 2 sprint
    public bool IsTrotMode { get; private set; }   // Caps Lock toggle

    protected virtual void Awake()
    {
        if (groundingSystem == null)
            groundingSystem = GetComponent<AnimalGroundingSystem>();
        if (groundAlignment == null)
            groundAlignment = GetComponent<AnimalGroundAlignment>();
        if (animator == null)
            animator = GetComponent<Animator>();
        if (rb == null)
            rb = GetComponentInParent<Rigidbody>();
        if (cam == null)
            cam = Camera.main?.transform;

        jumpForwardHash = Animator.StringToHash("JumpForward");
        jumpUpHash = Animator.StringToHash("JumpUp");
        isFreeFallingHash = Animator.StringToHash("IsFalling");
        isOnCliffHash = Animator.StringToHash("IsOnCliff");
        isLandingHash = Animator.StringToHash("IsLanding");

        // Enable root motion - we'll disable it when flying
        if (animator != null)
            animator.applyRootMotion = !useRootMotion; // horse manages root motion manually
    }

    private void Update()
    {
        // Gait toggle must be in Update — GetKeyDown is unreliable in FixedUpdate
        if (IsOwner && !PauseMenu.IsPaused && Input.GetKeyDown(trotToggleKey))
            IsTrotMode = !IsTrotMode;

        if (Input.GetKeyDown(debugRespawnKey) && debugRespawnPoint != null)
        {
            Debug.Log("RESPAWN");
            transform.position = debugRespawnPoint.position;
            transform.rotation = debugRespawnPoint.rotation;
            _fallVelocity = 0f;
            groundingSystem.ResetFallingState();
        }

        if (isTakingOff)
        {
            HandleTakeoffState();
            return;
        }

        // Jump animation playing (still grounded, just animating)
        if (isPlayingJump)
        {
            HandleAirSteering();
            HandleJumpAnimation();
            return;
        }

        // Free fall — allow air steering
        if (!isActive && !IsSwimmingActive())
        {
            HandleAirSteering();
        }

        // Normal grounded state — use a small grace period before marking ungrounded
        // to prevent OnBecameGrounded firing repeatedly during root motion paw bouncing.
        if (groundingSystem.IsGrounded)
        {
            _lostGroundTimer = 0f;
            if (!isActive)
            {
                OnBecameGrounded();
                isActive = true;
            }
        }
        else
        {
            _lostGroundTimer += Time.deltaTime;
            if (_lostGroundTimer > 0.1f)
                isActive = false;
        }
        UpdateFallAnimParams();
        OnGroundUpdate();
    }

    protected virtual void OnGroundUpdate() { }

    private void FixedUpdate()
    {
        if (_pendingRootMotion != Vector3.zero && rb != null)
        {
            rb.MovePosition(rb.position + _pendingRootMotion);
            _pendingRootMotion = Vector3.zero;
        }

        if (useFakeGravity)
        {
            if (groundingSystem.IsGrounded)
                _fallVelocity = 0f;
            else if (!isPlayingJump)
            {
                _fallVelocity += fakeGravity * Time.fixedDeltaTime;
                _fallVelocity = Mathf.Min(_fallVelocity, maxFallSpeed);
                rb.MovePosition(rb.position + Vector3.down * _fallVelocity * Time.fixedDeltaTime);
            }
        }
        if (!ignoreOwnershipForTesting && !IsOwner) return;

        OnFixedGroundUpdate();

        // Takeoff - lift dragon up
        if (isTakingOff)
        {
            ApplyTakeoffLift();
            return;
        }

        // Jump animation playing - root motion handles movement, GaitSpeed held by subclass
        if (isPlayingJump) return;

        // Not active - do nothing (unless subclass overrides)
        if (!isActive && !CanMoveWhileInactive()) return;

        // Update cached ground normal
        UpdateGroundNormal();

        // Normal ground movement
        HandleGroundMovement();
    }

    protected virtual void OnFixedGroundUpdate() { }
    private void UpdateFallAnimParams()
    {
        if (animator == null || groundingSystem == null) return;
        bool falling = groundingSystem.IsFalling && !isPlayingJump && !groundingSystem.IsOnCliff && !HasGroundBelow() && !IsSwimmingActive();
        bool onCliff = groundingSystem.IsOnCliff && !isPlayingJump;
        bool landing = groundingSystem.IsLanding && !isPlayingJump;

        // Push horse forward when first stepping off cliff
        if (onCliff && !_wasOnCliff)
            rb.AddForce(transform.forward * cliffFallPush, ForceMode.VelocityChange);
        _wasOnCliff = onCliff;

        animator.SetBool(isFreeFallingHash, falling);
        animator.SetBool(isOnCliffHash, onCliff);
        animator.SetBool(isLandingHash, landing);
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
        bool paused = PauseMenu.IsPaused;
        float vertical = (_ignoreInput || paused) ? 0f : Input.GetAxisRaw(forwardAxis);
        float horizontal = (_ignoreInput || paused) ? 0f : Input.GetAxisRaw(strafeAxis);
        bool sprint = !_ignoreInput && !paused && Input.GetKey(sprintKey);
        Vector2 input = new Vector2(horizontal, vertical);
        bool hasInput = input.magnitude > 0.1f;

        // Require key to be held for minInputHoldTime before movement registers
        if (hasInput)
            _inputHoldTimer += Time.fixedDeltaTime;
        else
            _inputHoldTimer = 0f;

        if (_inputHoldTimer >= minInputHoldTime) _inputConfirmed = true;
        if (!hasInput) _inputConfirmed = false;

        bool isMoving = _inputConfirmed;

        // Gait: 0=idle, 0.33=walk, 0.66=trot, 1=sprint
        float targetGait;
        if (!isMoving)          targetGait = 0f;
        else if (sprint)        targetGait = 1f;
        else if (IsTrotMode)    targetGait = 0.66f;
        else                    targetGait = 0.33f;

        GaitSpeed = Mathf.MoveTowards(GaitSpeed, targetGait, gaitSmoothSpeed * Time.fixedDeltaTime);

        // State
        IsWalking = isMoving && !sprint && !IsTrotMode;
        IsRunning = isMoving && sprint;
        ForwardSpeed = vertical;

        // Camera-relative movement
        if (isMoving && cam != null && rb != null)
        {
            Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;

            // Cap how fast the desired yaw can change per gait
            float turnRate = sprint ? sprintTurnRate : (IsTrotMode ? trotTurnRate : walkTurnRate);
            _cappedYaw = Mathf.MoveTowardsAngle(_cappedYaw, targetAngle, turnRate * Time.fixedDeltaTime);

            // Feed the capped yaw to rotation — horse turns gradually
            if (groundAlignment != null)
                groundAlignment.UpdateTargetYaw(_cappedYaw);

            // Move in the capped direction so horse doesn't slide sideways
            Vector3 worldForward = Quaternion.Euler(0f, _cappedYaw, 0f) * Vector3.forward;
            Vector3 moveDir = ProjectOnSlope(worldForward, currentGroundNormal);

            if (!useRootMotion)
            {
                float speed = sprint ? runSpeed : (IsTrotMode ? trotSpeed : walkSpeed);
                rb.MovePosition(rb.position + moveDir * speed * Time.fixedDeltaTime);
            }

            // TurnAngle from delta between where horse is facing and where it needs to face
            // Normalize over 30 degrees so ±1 is reachable in normal turns
            float currentYaw = rb.rotation.eulerAngles.y;
            float angleDelta = Mathf.DeltaAngle(currentYaw, _cappedYaw);
            float targetTurnAngle = Mathf.Clamp(angleDelta / 30f, -1f, 1f);
            TurnAngle = Mathf.Clamp(
                Mathf.SmoothDamp(TurnAngle, targetTurnAngle, ref _turnAngleVel, 1f / turnAngleSmoothing),
                -1f, 1f);
            IsTurningLeft = angleDelta < -turnHeadAngle;
            IsTurningRight = angleDelta > turnHeadAngle;
            TurnSpeed = Mathf.Abs(angleDelta) / 180f;

            rb.constraints = RigidbodyConstraints.FreezeRotation;


        }
        else
        {
            // Gradually track current facing so TurnAngle decays smoothly instead of snapping
            float currentYaw = rb.rotation.eulerAngles.y;
            _cappedYaw = Mathf.MoveTowardsAngle(_cappedYaw, currentYaw, walkTurnRate * Time.fixedDeltaTime);
            TurnAngle = Mathf.Clamp(
                Mathf.SmoothDamp(TurnAngle, 0f, ref _turnAngleVel, 1f / turnAngleSmoothing),
                -1f, 1f);

            IsTurningLeft = false;
            IsTurningRight = false;
            TurnSpeed = 0f;



            //if (groundingSystem.IsGrounded && !isPlayingJump)
            //    rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        // Jump animation (Space) - just plays animation, stays grounded
        if (!paused && Input.GetKey(jumpKey) && groundingSystem.IsGrounded /*&& !isPlayingJump*/)
        {
            TriggerJumpAnimation();
        }

        // Takeoff (C) - lifts animal up (subclass handles flight handoff)
        if (!paused && Input.GetKey(takeoffKey) && groundingSystem.IsGrounded)
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
        OnJumpTriggered();
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
    // AIR STEERING - rotate dragon mid-jump or during free fall
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads WASD + camera direction and steers the dragon while airborne.
    /// Only rotates — no horizontal movement added.
    /// </summary>
    private void HandleAirSteering()
    {
        if (_ignoreInput || cam == null || rb == null) return;

        float vertical   = Input.GetAxisRaw(forwardAxis);
        float horizontal = Input.GetAxisRaw(strafeAxis);
        Vector2 input = new Vector2(horizontal, vertical);
        bool hasInput = input.magnitude > 0.1f;

        _isAirSteering = hasInput;

        if (!hasInput) return;

        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
        float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;

        _cappedYaw = Mathf.MoveTowardsAngle(_cappedYaw, targetAngle, airTurnRate * Time.deltaTime);

        if (groundAlignment != null)
            groundAlignment.UpdateTargetYaw(_cappedYaw);
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

        // After JumpUp animation duration, hand off to flight (subclass)
        if (stateTimer >= jumpUpAnimationDuration)
        {
            isTakingOff = false;
            IsTakingOff = false;
            stateTimer = 0f;

            // Disable root motion for flight
            if (animator != null)
                animator.applyRootMotion = false;

            groundingSystem.ResetFallingState();
            OnTakeoffRequested();
        }
    }

    /// <summary>
    /// Called when takeoff animation completes. Override in subclasses to hand off to a flight controller.
    /// </summary>
    protected virtual void OnTakeoffRequested() { }
    protected virtual void OnLanded() { }
    protected virtual bool CanMoveWhileInactive() => false;

    /// <summary>
    /// Override in subclasses that have a ground-detection raycast.
    /// Returns true if solid ground exists below the animal (independent of paw grounding).
    /// Base returns false — meaning free-fall animation is not suppressed by default.
    /// </summary>
    protected virtual bool HasGroundBelow() => false;

    /// <summary>
    /// Override in subclasses that have a swim controller.
    /// Returns true if the animal is currently swimming.
    /// Base returns false.
    /// </summary>
    protected virtual bool IsSwimmingActive() => false;

    /// <summary>
    /// Called when a jump animation is triggered. Override in subclasses to play jump sounds.
    /// </summary>
    protected virtual void OnJumpTriggered() { }

    // ═══════════════════════════════════════════════════════════════
    // STATE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    private void OnBecameGrounded()
    {
        isActive = true;
        _ignoreInput = false;
        ClearState();
        OnLanded();

        // Only kill velocity when NOT using root motion.
        // Root motion drives velocity itself — zeroing it here kills the landing momentum.
        if (rb != null && !useRootMotion)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Sync yaw to current rotation so dragon doesn't snap
        float currentYaw = rb.rotation.eulerAngles.y;
        _cappedYaw = currentYaw;
        if (groundAlignment != null)
            groundAlignment.SetYawImmediate(currentYaw);

        if (animator != null)
            animator.applyRootMotion = true;
    }

    /// <summary>
    /// Captures animation root motion and applies it to the rigidbody.
    /// This makes the camera follow the dragon during jump animations.
    /// Requires "Apply Root Motion" checked on the Animator.
    /// </summary>
    private Vector3 _pendingRootMotion;

    protected virtual void OnAnimatorMove()
    {
        if (animator == null || rb == null) return;
        if (!IsOwner) return;

        if (!enabled)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        bool shouldApply =
            useRootMotion ||
            (isPlayingJump && rootMotionOnJump) ||
            (groundingSystem.IsOnCliff && rootMotionOnCliff) ||
            (groundingSystem.IsFalling && rootMotionOnFalling) ||
            (groundingSystem.IsLanding && rootMotionOnLanding);

        if (shouldApply)
        {
            if (useRootMotion)
            {
                if (animator.deltaPosition.sqrMagnitude > 0.00001f)
                {
                    Vector3 delta = animator.deltaPosition;

                    // If player is steering mid-air, redirect root motion to match current facing
                    if (_isAirSteering && isPlayingJump)
                    {
                        float speed = delta.magnitude;
                        Vector3 forward = rb.rotation * Vector3.forward;
                        delta = forward * speed;
                        delta.y = animator.deltaPosition.y; // preserve vertical from animation
                    }

                    rb.linearVelocity = delta / Time.deltaTime;
                }
                else
                    rb.linearVelocity = Vector3.zero;
            }
            else
                _pendingRootMotion += animator.deltaPosition;
        }
    }



    public void ClearInputState() => ClearState();

    private bool _ignoreInput;

    public void StopGradually()
    {
        GaitSpeed = 0f;
        IsWalking = false;
        IsRunning = false;
        ForwardSpeed = 0f;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
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
        TurnAngle = 0f;
        GaitSpeed = 0f;
        _turnAngleVel = 0f;
    }
}