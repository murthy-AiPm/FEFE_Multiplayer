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

    [Header("Dragon Combat References")]
    [SerializeField] private DragonCombatController combatController;

    // ─── Animator Parameter Hashes (Flight) ──────────────

    private int isDivingHash;
    private int isFallingHash;
    private int isDeadHash;
    private int flightModeHash;
    private int thrustHash;
    private int yawHash;
    private int flightPitchHash;
    private int isRoarHash;

    // ─── Animator Parameter Hashes (Swim) ────────────────

    private int isSwimmingHash;
    private int swimSpeedHash;
    private int swimTurnHash;
    private int swimVerticalHash;

    // ─── NetworkVariables (Flight) ────────────────────────

    private NetworkVariable<bool> netIsDiving = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsFalling = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsRoar = new NetworkVariable<bool>(
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
        if (combatController == null)
            combatController = GetComponentInParent<DragonCombatController>();

        // Cache flight hashes
        isDivingHash      = Animator.StringToHash("IsDiving");
        isFallingHash     = Animator.StringToHash("IsFalling");
        isDeadHash        = Animator.StringToHash("IsDead");
        flightModeHash    = Animator.StringToHash("FlightMode");
        thrustHash        = Animator.StringToHash("Thrust");
        yawHash           = Animator.StringToHash("Yaw");
        flightPitchHash   = Animator.StringToHash("Pitch");

        // Cache combat hashes
        isRoarHash        = Animator.StringToHash("IsRoar");

        // Cache swim hashes
        isSwimmingHash   = Animator.StringToHash("IsSwimming");
        swimSpeedHash    = Animator.StringToHash("SwimSpeed");
        swimTurnHash     = Animator.StringToHash("SwimTurn");
        swimVerticalHash = Animator.StringToHash("SwimVertical");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Late-join fix: if the dragon is already in flight or swimming when a
        // remote client spawns, force the Animator into the correct state.
        // Without this, the Animator starts in its default state (Idle) and waits
        // for a transition that never fires because the parameters are already set.
        if (!IsOwner && animator != null)
        {
            if (netFlightMode.Value)
            {
                animator.Play("BlendFly");
                animator.SetBool(flightModeHash, true);
                animator.SetFloat(thrustHash, netFlightThrust.Value);
                animator.SetFloat(yawHash, netFlightYaw.Value);
                animator.SetFloat(flightPitchHash, netFlightPitch.Value);
            }
            else if (netIsSwimming.Value)
            {
                animator.Play("SwimmingLocomotion");
                animator.SetBool(isSwimmingHash, true);
                animator.SetFloat(swimSpeedHash, netSwimSpeed.Value);
                animator.SetFloat(swimTurnHash, netSwimTurn.Value);
                animator.SetFloat(swimVerticalHash, netSwimVertical.Value);
            }
        }
    }

    protected override void LateUpdate()
    {
        // Ground params handled by base
        base.LateUpdate();

        if (animator == null) return;

        // ─── Flight state bools ──────────────────────────
        animator.SetBool(isDivingHash,      netIsDiving.Value);

        // ─── Roar bool (synced from owner to all clients) ─
        animator.SetBool(isRoarHash,        netIsRoar.Value);

        // ─── IsFalling (synced so remotes don't compute locally with stale FlightMode) ──
        if (!IsOwner)
            animator.SetBool(isFallingHash, netIsFalling.Value);

        // ─── Root motion flight params (Thrust, Yaw, Pitch, FlightMode) ──
        // When dead, don't overwrite FlightMode — DragonDamageAnimator controls it
        bool isDead = animator.GetBool(isDeadHash);

        if (IsOwner)
        {
            if (flightController != null && !isDead)
            {
                animator.SetBool(flightModeHash, flightController.IsFlightMode);
            }
        }
        else
        {
            if (!isDead)
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

        // ─── Roar bool ───────────────────────────────────
        if (combatController != null)
        {
            bool roaring = combatController.IsRoaring;
            if (netIsRoar.Value != roaring)
                netIsRoar.Value = roaring;
        }

        // ─── IsFalling (owner is the authority) ──────────
        if (animator != null)
        {
            bool falling = animator.GetBool(isFallingHash);
            if (netIsFalling.Value != falling)
                netIsFalling.Value = falling;
        }

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
