using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Single script that drives ALL dragon animator parameters and syncs them over the network.
/// Owner writes state from FlightController + GroundController into NetworkVariables.
/// All clients (including owner) read NetworkVariables and apply to local Animator.
/// Triggers (JumpUp/JumpForward) are handled separately via ServerRpc in DragonGroundController.
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
    private int isFallingHash;
    private int isLandingHash;
    private int isTurningLeftHash;
    private int isTurningRightHash;
    private int turnSpeedHash;

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
    private NetworkVariable<bool> netIsFalling = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsLanding = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsTurningLeft = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsTurningRight = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netTurnSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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
        isFallingHash = Animator.StringToHash("IsFalling");
        isLandingHash = Animator.StringToHash("IsLanding");
        isTurningLeftHash = Animator.StringToHash("IsTurningLeft");
        isTurningRightHash = Animator.StringToHash("IsTurningRight");
        turnSpeedHash = Animator.StringToHash("TurnSpeed");
    }

    private void LateUpdate()
    {
        if (animator == null) return;

        // ─── Owner writes to NetworkVariables ────────────
        if (IsOwner)
        {
            // Shared
            if (groundingSystem != null)
                netIsGrounded.Value = groundingSystem.IsGrounded;

            // Flight
            if (flightController != null)
            {
                netIsHovering.Value = flightController.IsHoverMode;
                netIsFlying.Value = flightController.IsFlying;
                netIsGliding.Value = flightController.IsGliding;
                netIsDiving.Value = flightController.IsDiving;
                netAirSpeed.Value = flightController.AirSpeed;
                netVerticalSpeed.Value = flightController.Velocity.y;

                // ForwardSpeed: use flight velocity when airborne, ground state when grounded
                if (!groundingSystem.IsGrounded)
                    netForwardSpeed.Value = Vector3.Dot(flightController.Velocity, transform.forward);
            }

            // Ground
            if (groundController != null)
            {
                netIsWalking.Value = groundController.IsWalking;
                netIsRunning.Value = groundController.IsRunning;
                netIsFalling.Value = groundController.IsFalling;
                netIsLanding.Value = groundController.IsLanding;
                netIsTurningLeft.Value = groundController.IsTurningLeft;
                netIsTurningRight.Value = groundController.IsTurningRight;
                netTurnSpeed.Value = groundController.TurnSpeed;

                // ForwardSpeed: use ground state when grounded
                if (groundingSystem.IsGrounded)
                    netForwardSpeed.Value = groundController.ForwardSpeed;
            }
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
        animator.SetBool(isFallingHash, netIsFalling.Value);
        animator.SetBool(isLandingHash, netIsLanding.Value);
        animator.SetBool(isTurningLeftHash, netIsTurningLeft.Value);
        animator.SetBool(isTurningRightHash, netIsTurningRight.Value);
        animator.SetFloat(turnSpeedHash, netTurnSpeed.Value);
    }
}
