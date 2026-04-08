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

    private int isDivingHash;
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

    private NetworkVariable<bool> netIsDiving = new NetworkVariable<bool>(
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
        isDivingHash      = Animator.StringToHash("IsDiving");
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
        animator.SetBool(isDivingHash,      netIsDiving.Value);

        // ─── Root motion flight params (Thrust, Yaw, Pitch, FlightMode) ──
        if (IsOwner)
        {
            // Owner: flight controller writes Thrust/Yaw/Pitch directly to animator in its own Update.
            // We only need to ensure FlightMode is set here.
            if (flightController != null)
            {
                animator.SetBool(flightModeHash, flightController.IsFlightMode);
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
        if (netIsDiving.Value != flightController.IsDiving)
            netIsDiving.Value = flightController.IsDiving;

        // ─── Root motion flight params ───────────────────
        if (netFlightMode.Value != flightController.IsFlightMode)
            netFlightMode.Value = flightController.IsFlightMode;

        // Read Thrust/Yaw/Pitch directly from the flight controller (source of truth)
        float thrust = flightController.FlightThrust;
        if (Mathf.Abs(netFlightThrust.Value - thrust) > FLOAT_EPSILON)
            netFlightThrust.Value = thrust;
        if (thrust == 0f && netFlightThrust.Value != 0f)
            netFlightThrust.Value = 0f;

        float flightYaw = flightController.FlightYaw;
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
