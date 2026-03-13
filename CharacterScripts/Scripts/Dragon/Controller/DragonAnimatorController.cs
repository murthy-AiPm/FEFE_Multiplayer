using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Single script that drives ALL dragon animator parameters and syncs them over the network.
/// Owner writes state from FlightController + GroundController into NetworkVariables.
/// All clients (including owner) read NetworkVariables and apply to local Animator.
/// Triggers (JumpForward) are handled separately via ServerRpc in DragonGroundController.
/// 
/// OPTIMIZATIONS:
/// - Throttled updates: 20Hz instead of 60Hz (reduces network traffic by 66%)
/// - Change detection: Only sends NetworkVariable updates when values actually change
/// - Float epsilon: Only updates floats if change > 0.01 (prevents micro-changes)
/// </summary>
[RequireComponent(typeof(Animator))]
public class DragonAnimatorController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private DragonGroundController groundController;
    [SerializeField] private DragonGroundingSystem groundingSystem;
    [SerializeField] private Animator animator;

    // ─── Animator Parameter Hashes ───────────────────────

    // Flight
    private int isHoveringHash;
    private int isFlyingHash;
    private int isGlidingHash;
    private int isDivingHash;
    private int airSpeedHash;
    private int verticalSpeedHash;

    // Shared
    private int isGroundedHash;
    private int forwardSpeedHash;

    // Ground
    private int isWalkingHash;
    private int isRunningHash;
    private int isPlayingJumpHash;
    private int isTakingOffHash;
    private int isTurningLeftHash;
    private int isTurningRightHash;
    private int turnSpeedHash;
    private int turnAngleHash;
    private int gaitSpeedHash;

    // ─── NetworkVariables ────────────────────────────────

    // Flight
    private NetworkVariable<bool> netIsHovering = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsFlying = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsGliding = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsDiving = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netAirSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netVerticalSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Shared
    private NetworkVariable<bool> netIsGrounded = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netForwardSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Ground
    private NetworkVariable<bool> netIsWalking = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsRunning = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsPlayingJump = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsTakingOff = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsTurningLeft = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsTurningRight = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netTurnSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netTurnAngle = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netGaitSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ─── Throttling ──────────────────────────────────────
    private const float NETWORK_UPDATE_INTERVAL = 0.05f; // 20 updates/sec instead of 60
    private float nextNetworkUpdateTime;
    private const float FLOAT_EPSILON = 0.01f; // Only update floats if change > this

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();
        if (groundController == null)
            groundController = GetComponentInParent<DragonGroundController>();
        if (groundingSystem == null)
            groundingSystem = GetComponentInParent<DragonGroundingSystem>();

        // Cache hashes - Flight
        isHoveringHash = Animator.StringToHash("IsHovering");
        isFlyingHash = Animator.StringToHash("IsFlying");
        isGlidingHash = Animator.StringToHash("IsGliding");
        isDivingHash = Animator.StringToHash("IsDiving");
        airSpeedHash = Animator.StringToHash("AirSpeed");
        verticalSpeedHash = Animator.StringToHash("VerticalSpeed");

        // Cache hashes - Shared
        isGroundedHash = Animator.StringToHash("IsGrounded");
        forwardSpeedHash = Animator.StringToHash("ForwardSpeed");

        // Cache hashes - Ground
        isWalkingHash = Animator.StringToHash("IsWalking");
        isRunningHash = Animator.StringToHash("IsRunning");
        isPlayingJumpHash = Animator.StringToHash("IsPlayingJump");
        isTakingOffHash = Animator.StringToHash("IsTakingOff");
        isTurningLeftHash = Animator.StringToHash("IsTurningLeft");
        isTurningRightHash = Animator.StringToHash("IsTurningRight");
        turnSpeedHash = Animator.StringToHash("TurnSpeed");
        turnAngleHash = Animator.StringToHash("TurnAngle");
        gaitSpeedHash = Animator.StringToHash("GaitSpeed");
    }

    private void LateUpdate()
    {
        if (animator == null) return;

        // ─── Owner writes to NetworkVariables (THROTTLED) ────
        if (IsOwner)
        {
            // Only update network state at fixed intervals (20Hz instead of 60Hz)
            if (Time.time >= nextNetworkUpdateTime)
            {
                nextNetworkUpdateTime = Time.time + NETWORK_UPDATE_INTERVAL;
                UpdateNetworkVariables();
            }
        }

        // Ramp GaitSpeed to zero when ground controller is disabled (e.g. after dismount)
        if (groundController != null && !groundController.enabled && netGaitSpeed.Value > 0.01f)
        {
            if (IsOwner)
                netGaitSpeed.Value = Mathf.MoveTowards(netGaitSpeed.Value, 0f, 2f * Time.deltaTime);
        }

        // ─── Everyone reads NetworkVariables → Animator ──

        // Shared
        animator.SetBool(isGroundedHash, netIsGrounded.Value);
        animator.SetFloat(forwardSpeedHash, netForwardSpeed.Value);

        // Flight
        animator.SetBool(isHoveringHash, netIsHovering.Value);
        animator.SetBool(isFlyingHash, netIsFlying.Value);
        animator.SetBool(isGlidingHash, netIsGliding.Value);
        animator.SetBool(isDivingHash, netIsDiving.Value);
        animator.SetFloat(airSpeedHash, netAirSpeed.Value);
        animator.SetFloat(verticalSpeedHash, netVerticalSpeed.Value);

        // Ground
        animator.SetBool(isWalkingHash, netIsWalking.Value);
        animator.SetBool(isRunningHash, netIsRunning.Value);
        //animator.SetBool(isPlayingJumpHash, netIsPlayingJump.Value);
       // animator.SetBool(isTakingOffHash, netIsTakingOff.Value);
        animator.SetBool(isTurningLeftHash, netIsTurningLeft.Value);
        animator.SetBool(isTurningRightHash, netIsTurningRight.Value);
        animator.SetFloat(turnSpeedHash, netTurnSpeed.Value);
        if (IsOwner)
        {
            animator.SetFloat(gaitSpeedHash, groundController.GaitSpeed);
            animator.SetFloat(turnAngleHash, groundController.TurnAngle);
        }
        else
        {
            animator.SetFloat(gaitSpeedHash, netGaitSpeed.Value);
            animator.SetFloat(turnAngleHash, netTurnAngle.Value);
        }
    }

    /// <summary>
    /// Only update NetworkVariables when values actually change.
    /// This prevents flooding the network with redundant updates.
    /// </summary>
    private void UpdateNetworkVariables()
    {
        // Shared
        if (groundingSystem != null)
        {
            bool isGrounded = groundingSystem.IsGrounded;
            if (netIsGrounded.Value != isGrounded)
                netIsGrounded.Value = isGrounded;
        }

        // Flight
        if (flightController != null)
        {
            if (netIsHovering.Value != flightController.IsHoverMode)
                netIsHovering.Value = flightController.IsHoverMode;

            if (netIsFlying.Value != flightController.IsFlying)
                netIsFlying.Value = flightController.IsFlying;

            if (netIsGliding.Value != flightController.IsGliding)
                netIsGliding.Value = flightController.IsGliding;

            if (netIsDiving.Value != flightController.IsDiving)
                netIsDiving.Value = flightController.IsDiving;

            // Only update floats if change is significant
            float airSpeed = flightController.AirSpeed;
            if (Mathf.Abs(netAirSpeed.Value - airSpeed) > FLOAT_EPSILON)
                netAirSpeed.Value = airSpeed;

            float vertSpeed = flightController.Velocity.y;
            if (Mathf.Abs(netVerticalSpeed.Value - vertSpeed) > FLOAT_EPSILON)
                netVerticalSpeed.Value = vertSpeed;

            // ForwardSpeed: use flight velocity when airborne
            if (groundingSystem != null && !groundingSystem.IsGrounded)
            {
                float fwdSpeed = Vector3.Dot(flightController.Velocity, transform.forward);
                if (Mathf.Abs(netForwardSpeed.Value - fwdSpeed) > FLOAT_EPSILON)
                    netForwardSpeed.Value = fwdSpeed;
            }
        }

        // Ground
        if (groundController != null)
        {
            if (netIsWalking.Value != groundController.IsWalking)
                netIsWalking.Value = groundController.IsWalking;

            if (netIsRunning.Value != groundController.IsRunning)
                netIsRunning.Value = groundController.IsRunning;

            if (netIsPlayingJump.Value != groundController.IsPlayingJump)
                netIsPlayingJump.Value = groundController.IsPlayingJump;

            if (netIsTakingOff.Value != groundController.IsTakingOff)
                netIsTakingOff.Value = groundController.IsTakingOff;

            if (netIsTurningLeft.Value != groundController.IsTurningLeft)
                netIsTurningLeft.Value = groundController.IsTurningLeft;

            if (netIsTurningRight.Value != groundController.IsTurningRight)
                netIsTurningRight.Value = groundController.IsTurningRight;

            float turnSpeed = groundController.TurnSpeed;
            if (Mathf.Abs(netTurnSpeed.Value - turnSpeed) > FLOAT_EPSILON)
                netTurnSpeed.Value = turnSpeed;

            float turnAngle = groundController.TurnAngle;
            if (Mathf.Abs(netTurnAngle.Value - turnAngle) > FLOAT_EPSILON)
                netTurnAngle.Value = turnAngle;

            if (groundController.enabled)
            {
                float gaitSpeed = groundController.GaitSpeed;
                // Force zero through without epsilon check so animator doesn't stick at stale value
                if (gaitSpeed == 0f && netGaitSpeed.Value != 0f)
                    netGaitSpeed.Value = 0f;
                else if (Mathf.Abs(netGaitSpeed.Value - gaitSpeed) > FLOAT_EPSILON)
                    netGaitSpeed.Value = gaitSpeed;
            }

            // ForwardSpeed: use ground state when grounded
            if (groundingSystem != null && groundingSystem.IsGrounded)
            {
                float fwdSpeed = groundController.ForwardSpeed;
                if (Mathf.Abs(netForwardSpeed.Value - fwdSpeed) > FLOAT_EPSILON)
                    netForwardSpeed.Value = fwdSpeed;
            }
        }
    }
}