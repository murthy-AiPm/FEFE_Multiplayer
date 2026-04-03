using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon animator controller. Extends AnimalAnimatorController with flight,
/// swim, and root motion flight NetworkVariables and Animator parameter sync.
/// 
/// Ground params are fully handled by the base class.
/// This class adds flight state, swim state, and root motion flight params on top.
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
    private int flightModeHash;
    private int thrustHash;
    private int yawHash;
    private int flightPitchHash;

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

    // ─── NetworkVariables (Root Motion Flight) ────────────

    private NetworkVariable<bool> netFlightMode = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netFlightThrust = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netFlightYaw = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> netFlightPitch = new NetworkVariable<float>(
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
        flightModeHash    = Animator.StringToHash("FlightMode");
        thrustHash        = Animator.StringToHash("Thrust");
        yawHash           = Animator.StringToHash("Yaw");
        flightPitchHash   = Animator.StringToHash("Pitch");

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

        // ─── Flight state bools ──────────────────────────
        animator.SetBool(isHoveringHash,    netIsHovering.Value);
        animator.SetBool(isFlyingHash,      netIsFlying.Value);
        animator.SetBool(isGlidingHash,     netIsGliding.Value);
        animator.SetBool(isDivingHash,      netIsDiving.Value);
        animator.SetFloat(airSpeedHash,     netAirSpeed.Value);
        animator.SetFloat(verticalSpeedHash, netVerticalSpeed.Value);

        // ─── Root motion flight params (Thrust, Yaw, Pitch, FlightMode) ──
        if (IsOwner)
        {
            // Owner: flight controller writes Thrust/Yaw/Pitch directly to animator in its own Update,
            // but FlightMode and Pitch also need to be set here for consistency
            if (flightController != null)
            {
                animator.SetBool(flightModeHash, flightController.IsFlightMode);
                animator.SetFloat(flightPitchHash, flightController.FlightPitch);
            }
        }
        else
        {
            // Remotes: read all flight root motion params from NetworkVariables
            animator.SetBool(flightModeHash, netFlightMode.Value);
            animator.SetFloat(thrustHash, netFlightThrust.Value);
            animator.SetFloat(yawHash, netFlightYaw.Value);
            animator.SetFloat(flightPitchHash, netFlightPitch.Value);
        }

        // ─── Swim params ─────────────────────────────────
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

        // ─── Flight state bools ──────────────────────────
        if (netIsHovering.Value != flightController.IsHoverMode)
            netIsHovering.Value = flightController.IsHoverMode;

        if (netIsFlying.Value != flightController.IsFlying)
            netIsFlying.Value = flightController.IsFlying;

        if (netIsGliding.Value != flightController.IsGliding)
            netIsGliding.Value = flightController.IsGliding;

        if (netIsDiving.Value != flightController.IsDiving)
            netIsDiving.Value = flightController.IsDiving;

        float airSpeed = flightController.AirSpeed;
        if (Mathf.Abs(netAirSpeed.Value - airSpeed) > FLOAT_EPSILON)
            netAirSpeed.Value = airSpeed;

        float vertSpeed = flightController.Velocity.y;
        if (Mathf.Abs(netVerticalSpeed.Value - vertSpeed) > FLOAT_EPSILON)
            netVerticalSpeed.Value = vertSpeed;

        // ─── Root motion flight params ───────────────────
        if (netFlightMode.Value != flightController.IsFlightMode)
            netFlightMode.Value = flightController.IsFlightMode;

        // Read Thrust/Yaw/Pitch from what the owner wrote to the animator
        float thrust = animator.GetFloat(thrustHash);
        if (Mathf.Abs(netFlightThrust.Value - thrust) > FLOAT_EPSILON)
            netFlightThrust.Value = thrust;
        if (thrust == 0f && netFlightThrust.Value != 0f)
            netFlightThrust.Value = 0f;

        float flightYaw = animator.GetFloat(yawHash);
        if (Mathf.Abs(netFlightYaw.Value - flightYaw) > FLOAT_EPSILON)
            netFlightYaw.Value = flightYaw;
        if (flightYaw == 0f && netFlightYaw.Value != 0f)
            netFlightYaw.Value = 0f;

        float flightPitch = flightController.FlightPitch;
        if (Mathf.Abs(netFlightPitch.Value - flightPitch) > FLOAT_EPSILON)
            netFlightPitch.Value = flightPitch;
        if (flightPitch == 0f && netFlightPitch.Value != 0f)
            netFlightPitch.Value = 0f;

        // ─── Swim variables ──────────────────────────────
        if (swimController != null)
        {
            if (netIsSwimming.Value != swimController.IsSwimming)
                netIsSwimming.Value = swimController.IsSwimming;

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
            if (animator != null)
                animator.SetFloat(Animator.StringToHash("ForwardSpeed"), fwdSpeed);
        }
    }
}
