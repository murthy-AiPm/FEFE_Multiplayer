using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Base animator controller for all animals. Drives ground animator parameters and syncs them over the network.
/// Owner writes ground state from GroundController into NetworkVariables.
/// All clients (including owner) read NetworkVariables and apply to local Animator.
/// Subclasses (e.g. DragonAnimatorController) extend this with flight or species-specific params.
///
/// OPTIMIZATIONS:
/// - Throttled updates: 20Hz instead of 60Hz (reduces network traffic by 66%)
/// - Change detection: Only sends NetworkVariable updates when values actually change
/// - Float epsilon: Only updates floats if change > 0.01 (prevents micro-changes)
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimalAnimatorController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] protected AnimalGroundController groundController;
    [SerializeField] protected AnimalGroundingSystem groundingSystem;
    [SerializeField] protected Animator animator;

    // ─── Animator Parameter Hashes ───────────────────────

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

    /// <summary>Server-readable gait speed (0=idle, 0.33=walk, 0.66=trot, 1=sprint). Source-of-truth from owner.</summary>
    public float NetGaitSpeed => netGaitSpeed.Value;

    // ─── Throttling ──────────────────────────────────────
    private const float NETWORK_UPDATE_INTERVAL = 0.05f; // 20 updates/sec instead of 60
    private float nextNetworkUpdateTime;
    private const float FLOAT_EPSILON = 0.01f; // Only update floats if change > this

    // Parameter hashes that actually exist on this instance's Animator.
    // Used to skip SetBool/SetFloat calls for params the controller doesn't define,
    // which would otherwise spam "Parameter does not exist" warnings.
    private HashSet<int> _knownParamHashes;

    protected virtual void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (groundController == null)
            groundController = GetComponentInParent<AnimalGroundController>();
        if (groundingSystem == null)
            groundingSystem = GetComponentInParent<AnimalGroundingSystem>();

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

        // Build the set of parameter hashes this Animator actually has so the
        // SafeSet helpers can skip writes for missing ones.
        _knownParamHashes = new HashSet<int>();
        if (animator != null)
        {
            foreach (var p in animator.parameters)
                _knownParamHashes.Add(p.nameHash);
        }
    }

    /// <summary>SetBool that silently skips if the parameter isn't on this Animator.</summary>
    protected void SafeSetBool(int hash, bool value)
    {
        if (_knownParamHashes != null && _knownParamHashes.Contains(hash))
            animator.SetBool(hash, value);
    }

    /// <summary>SetFloat that silently skips if the parameter isn't on this Animator.</summary>
    protected void SafeSetFloat(int hash, float value)
    {
        if (_knownParamHashes != null && _knownParamHashes.Contains(hash))
            animator.SetFloat(hash, value);
    }

    /// <summary>SetInteger that silently skips if the parameter isn't on this Animator.</summary>
    protected void SafeSetInteger(int hash, int value)
    {
        if (_knownParamHashes != null && _knownParamHashes.Contains(hash))
            animator.SetInteger(hash, value);
    }

    /// <summary>SetTrigger that silently skips if the parameter isn't on this Animator.</summary>
    protected void SafeSetTrigger(int hash)
    {
        if (_knownParamHashes != null && _knownParamHashes.Contains(hash))
            animator.SetTrigger(hash);
    }

    protected virtual void LateUpdate()
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

        // ─── Everyone reads NetworkVariables → Animator ──

        // Shared
        SafeSetBool(isGroundedHash, netIsGrounded.Value);
        SafeSetFloat(forwardSpeedHash, netForwardSpeed.Value);

        // Ground
        SafeSetBool(isWalkingHash, netIsWalking.Value);
        SafeSetBool(isRunningHash, netIsRunning.Value);
        //SafeSetBool(isPlayingJumpHash, netIsPlayingJump.Value);
        //SafeSetBool(isTakingOffHash, netIsTakingOff.Value);
        SafeSetBool(isTurningLeftHash, netIsTurningLeft.Value);
        SafeSetBool(isTurningRightHash, netIsTurningRight.Value);
        SafeSetFloat(turnSpeedHash, netTurnSpeed.Value);
        if (IsOwner)
        {
            SafeSetFloat(gaitSpeedHash, groundController.GaitSpeed);
            SafeSetFloat(turnAngleHash, groundController.TurnAngle);
        }
        else
        {
            SafeSetFloat(gaitSpeedHash, netGaitSpeed.Value);
            SafeSetFloat(turnAngleHash, netTurnAngle.Value);
        }
    }

    /// <summary>
    /// Only update NetworkVariables when values actually change.
    /// This prevents flooding the network with redundant updates.
    /// Subclasses should call base.UpdateNetworkVariables() then add their own.
    /// </summary>
    protected virtual void UpdateNetworkVariables()
    {
        // Shared
        if (groundingSystem != null)
        {
            bool isGrounded = groundingSystem.IsGrounded;
            if (netIsGrounded.Value != isGrounded)
                netIsGrounded.Value = isGrounded;
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