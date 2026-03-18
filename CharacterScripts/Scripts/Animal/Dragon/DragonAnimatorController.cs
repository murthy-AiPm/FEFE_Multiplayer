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

    // ─── Animator Parameter Hashes (Flight) ──────────────

    private int isHoveringHash;
    private int isFlyingHash;
    private int isGlidingHash;
    private int isDivingHash;
    private int airSpeedHash;
    private int verticalSpeedHash;

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

    private const float FLOAT_EPSILON = 0.01f;

    protected override void Awake()
    {
        base.Awake();

        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();

        // Cache flight hashes
        isHoveringHash    = Animator.StringToHash("IsHovering");
        isFlyingHash      = Animator.StringToHash("IsFlying");
        isGlidingHash     = Animator.StringToHash("IsGliding");
        isDivingHash      = Animator.StringToHash("IsDiving");
        airSpeedHash      = Animator.StringToHash("AirSpeed");
        verticalSpeedHash = Animator.StringToHash("VerticalSpeed");
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
