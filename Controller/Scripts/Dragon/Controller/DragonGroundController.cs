using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Handles all grounded dragon movement: walk, run, turn, jump, fall, takeoff.
/// Root motion driven - Animator handles actual movement distances/speeds.
/// Only owner reads input. Exposes public state for DragonAnimatorController to sync.
/// Triggers (JumpUp/JumpForward) use ServerRpc since NetworkVariables don't support triggers.
/// </summary>
public class DragonGroundController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonGroundingSystem groundingSystem;
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private Animator animator;

    [Header("Input")]
    [SerializeField] private string forwardAxis = "Vertical";      // W/S
    [SerializeField] private string turnAxis = "Horizontal";       // A/D
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Takeoff")]
    [SerializeField] private float takeoffHoldTime = 0.4f;
    [SerializeField] private float jumpUpDuration = 1.11f;
    [SerializeField] private float jumpForwardDuration = 1.18f;

    [Header("Fall")]
    [SerializeField] private float landingDuration = 1.07f;

    // Animator trigger hashes (only used locally after Rpc)
    private int jumpUpHash;
    private int jumpForwardHash;

    // State
    private bool isActive;
    private bool isJumping;
    private bool isFalling;
    private bool isLanding;
    private float jumpTimer;
    private float spaceHoldTimer;
    private bool lastJumpWasForward;    // Track which jump type for duration check

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
            flightController = GetComponent<DragonFlightController>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        jumpUpHash = Animator.StringToHash("JumpUp");
        jumpForwardHash = Animator.StringToHash("JumpForward");
    }

    private void Update()
    {
        // Activate/deactivate based on grounding
        if (groundingSystem.IsGrounded && !isFalling && !isJumping && !isLanding)
        {
            if (!isActive) OnBecameGrounded();
            isActive = true;
        }

        // Cliff fall detection - only owner decides
        if (IsOwner && isActive && groundingSystem.IsFalling && !isFalling && !isJumping)
        {
            StartFall();
        }

        if (!isActive && !isFalling && !isLanding) return;

        // Priority: Landing > Falling > Jumping > Ground Movement
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

        // Only owner reads input for ground movement
        if (IsOwner)
            HandleGroundMovement();
    }

    private void HandleGroundMovement()
    {
        float forward = Input.GetAxisRaw(forwardAxis);
        float turn = Input.GetAxisRaw(turnAxis);
        bool sprint = Input.GetKey(sprintKey);
        bool spaceDown = Input.GetKey(jumpKey);
        bool spacePressed = Input.GetKeyDown(jumpKey);

        // Walk / Run
        bool isMoving = Mathf.Abs(forward) > 0.1f;

        IsWalking = isMoving && !sprint;
        IsRunning = isMoving && sprint;
        ForwardSpeed = forward;

        // Turn
        IsTurningLeft = turn < -0.1f;
        IsTurningRight = turn > 0.1f;
        TurnSpeed = turn;

        // Jump / Takeoff logic
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

        // Space released before takeoff threshold = jump
        if (Input.GetKeyUp(jumpKey) && spaceHoldTimer < takeoffHoldTime)
        {
            if (isMoving)
                TriggerJumpForward();
            else
                TriggerJumpUp();
        }
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

        // Only owner checks input during jump
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
        IsFalling = true;  // Set after ClearState
    }

    private void HandleFall()
    {
        // Only owner checks input during fall
        if (IsOwner && Input.GetKey(jumpKey))
        {
            EndFall();
            groundingSystem.ResetFallingState();
            flightController.RequestGlide();
            return;
        }

        // Everyone checks grounding for landing
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
    }
}
