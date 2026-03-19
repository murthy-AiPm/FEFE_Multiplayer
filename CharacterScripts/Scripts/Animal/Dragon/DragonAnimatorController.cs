using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon animator controller. Extends AnimalAnimatorController with flight
/// NetworkVariables and Animator parameter sync driven by DragonFlightController.
/// 
/// Ground params are fully handled by the base class.
/// This class only adds flight state on top.
/// </summary>
public class DragonAnimatorController : AnimalAnimatorController
{
    [Header("Dragon Flight References")]
    [SerializeField] private DragonFlightController flightController;

    [Header("Dragon Swim References")]
    [SerializeField] private DragonSwimController swimController;

    // ─── Animator Parameter Hashes (Flight) ──────────────

    private int isHoveringHash;
    private int isFlyingHash;
    private int isGlidingHash;
    private int isDivingHash;
    private int airSpeedHash;
    private int verticalSpeedHash;

    // ─── Animator Parameter Hashes (Swim) ────────────────

    private int isSwimmingHash;
    private int swimSpeedHash;
    private int swimTurnHash;
    private int swimVerticalHash;

    // ─── NetworkVariables (Flight) ────────────────────────

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

    // ─── NetworkVariables (Swim) ──────────────────────────

    private NetworkVariable<bool> netIsSwimming = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netSwimSpeed = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netSwimTurn = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netSwimVertical = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private const float FLOAT_EPSILON = 0.01f;

    protected override void Awake()
    {
        base.Awake();

        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();
        if (swimController == null)
            swimController = GetComponentInParent<DragonSwimController>();

        // Cache flight hashes
        isHoveringHash    = Animator.StringToHash("IsHovering");
        isFlyingHash      = Animator.StringToHash("IsFlying");
        isGlidingHash     = Animator.StringToHash("IsGliding");
        isDivingHash      = Animator.StringToHash("IsDiving");
        airSpeedHash      = Animator.StringToHash("AirSpeed");
        verticalSpeedHash = Animator.StringToHash("VerticalSpeed");

        // Cache swim hashes
        isSwimmingHash   = Animator.StringToHash("IsSwimming");
        swimSpeedHash    = Animator.StringToHash("SwimSpeed");
        swimTurnHash     = Animator.StringToHash("SwimTurn");
        swimVerticalHash = Animator.StringToHash("SwimVertical");
    }

    protected override void LateUpdate()
    {
        // Ground params handled by base
        base.LateUpdate();

        if (animator == null) return;

        // Apply flight params to Animator (read from NetworkVariables, same as base pattern)
        animator.SetBool(isHoveringHash,    netIsHovering.Value);
        animator.SetBool(isFlyingHash,      netIsFlying.Value);
        animator.SetBool(isGlidingHash,     netIsGliding.Value);
        animator.SetBool(isDivingHash,      netIsDiving.Value);
        animator.SetFloat(airSpeedHash,     netAirSpeed.Value);
        animator.SetFloat(verticalSpeedHash, netVerticalSpeed.Value);

        // Swim params
        animator.SetBool(isSwimmingHash,      netIsSwimming.Value);
        animator.SetFloat(swimSpeedHash,      netSwimSpeed.Value);
        animator.SetFloat(swimTurnHash,       netSwimTurn.Value);
        animator.SetFloat(swimVerticalHash,   netSwimVertical.Value);
    }

    protected override void UpdateNetworkVariables()
    {
        // Ground variables handled by base
        base.UpdateNetworkVariables();

        if (flightController == null) return;

        // Flight bools
        if (netIsHovering.Value != flightController.IsHoverMode)
            netIsHovering.Value = flightController.IsHoverMode;

        if (netIsFlying.Value != flightController.IsFlying)
            netIsFlying.Value = flightController.IsFlying;

        if (netIsGliding.Value != flightController.IsGliding)
            netIsGliding.Value = flightController.IsGliding;

        if (netIsDiving.Value != flightController.IsDiving)
            netIsDiving.Value = flightController.IsDiving;

        // Flight floats
        float airSpeed = flightController.AirSpeed;
        if (Mathf.Abs(netAirSpeed.Value - airSpeed) > FLOAT_EPSILON)
            netAirSpeed.Value = airSpeed;

        float vertSpeed = flightController.Velocity.y;
        if (Mathf.Abs(netVerticalSpeed.Value - vertSpeed) > FLOAT_EPSILON)
            netVerticalSpeed.Value = vertSpeed;

        // Swim variables
        if (swimController != null)
        {
            if (netIsSwimming.Value != swimController.IsSwimming)
                netIsSwimming.Value = swimController.IsSwimming;

            // Read current animator values set by DragonSwimController on owner
            float swimSpeed = animator.GetFloat(swimSpeedHash);
            if (Mathf.Abs(netSwimSpeed.Value - swimSpeed) > FLOAT_EPSILON)
                netSwimSpeed.Value = swimSpeed;

            float swimTurn = animator.GetFloat(swimTurnHash);
            if (Mathf.Abs(netSwimTurn.Value - swimTurn) > FLOAT_EPSILON)
                netSwimTurn.Value = swimTurn;

            float swimVertical = animator.GetFloat(swimVerticalHash);
            if (Mathf.Abs(netSwimVertical.Value - swimVertical) > FLOAT_EPSILON)
                netSwimVertical.Value = swimVertical;
        }

        // ForwardSpeed from flight velocity when airborne (overrides base ground value)
        if (groundingSystem != null && !groundingSystem.IsGrounded)
        {
            float fwdSpeed = Vector3.Dot(flightController.Velocity, transform.forward);
            // netForwardSpeed is private in base — dragon writes to Animator directly when airborne
            // Base class handles ForwardSpeed for ground; we override the Animator param here
            if (animator != null)
                animator.SetFloat(Animator.StringToHash("ForwardSpeed"), fwdSpeed);
        }
    }
}
