using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class DragonAnimatorController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private Animator animator;

    [Header("Animator Parameters")]
    [SerializeField] private string isHoveringParam = "IsHovering";
    [SerializeField] private string isFlyingParam = "IsFlying";
    [SerializeField] private string isGlidingParam = "IsGliding";
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string airSpeedParam = "AirSpeed";
    [SerializeField] private string verticalSpeedParam = "VerticalSpeed";
    [SerializeField] private string forwardSpeedParam = "ForwardSpeed";
    [SerializeField] private string isDivingParam = "IsDiving";

    private int isHoveringHash;
    private int isFlyingHash;
    private int isGlidingHash;
    private int isGroundedHash;
    private int airSpeedHash;
    private int verticalSpeedHash;
    private int forwardSpeedHash;
    private int isDivingHash;

    // Network variables for syncing animation state
    // Network variables - add write permissions
    private NetworkVariable<bool> netIsHovering = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsFlying = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsGliding = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsGrounded = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsDiving = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netAirSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netVerticalSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netForwardSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();

        isDivingHash = Animator.StringToHash(isDivingParam);
        isHoveringHash = Animator.StringToHash(isHoveringParam);
        isFlyingHash = Animator.StringToHash(isFlyingParam);
        isGlidingHash = Animator.StringToHash(isGlidingParam);
        isGroundedHash = Animator.StringToHash(isGroundedParam);
        airSpeedHash = Animator.StringToHash(airSpeedParam);
        verticalSpeedHash = Animator.StringToHash(verticalSpeedParam);
        forwardSpeedHash = Animator.StringToHash(forwardSpeedParam);
    }

    private void LateUpdate()
    {
        if (animator == null || flightController == null)
            return;

        if (IsOwner)
        {
            // Owner updates network variables
            netIsHovering.Value = flightController.IsHoverMode;
            netIsFlying.Value = flightController.IsFlying;
            netIsGliding.Value = flightController.IsGliding;
            netIsGrounded.Value = flightController.IsGrounded;
            netIsDiving.Value = flightController.IsDiving;
            netAirSpeed.Value = flightController.AirSpeed;

            Vector3 velocity = flightController.Velocity;
            netVerticalSpeed.Value = velocity.y;
            netForwardSpeed.Value = Vector3.Dot(velocity, transform.forward);
        }

        // Everyone (including owner) reads from network variables and applies to animator
        animator.SetBool(isHoveringHash, netIsHovering.Value);
        animator.SetBool(isFlyingHash, netIsFlying.Value);
        animator.SetBool(isGlidingHash, netIsGliding.Value);
        animator.SetBool(isGroundedHash, netIsGrounded.Value);
        animator.SetBool(isDivingHash, netIsDiving.Value);
        animator.SetFloat(airSpeedHash, netAirSpeed.Value);
        animator.SetFloat(verticalSpeedHash, netVerticalSpeed.Value);
        animator.SetFloat(forwardSpeedHash, netForwardSpeed.Value);
    }
}