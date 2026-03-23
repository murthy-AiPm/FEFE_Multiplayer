using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon combat controller. Handles:
/// - Press 1: toggle Attack Mode (plays AttackMode idle anim, enables head tracking)
/// - Head tracking: Neck1 bone follows camera yaw automatically while in Attack Mode
/// - Left click: melee attack (stationary only)
/// - Right click hold: upper body spine twist toward camera (disabled in Attack Mode)
///
/// Upper body twist is applied procedurally in LateUpdate() after the Animator,
/// distributed across Spine → Spine1 → Spine2 for a natural curve.
/// Head tracking is applied to Neck1 bone only.
///
/// Network sync:
/// - Spine twist angle: NetworkVariable (float), 20Hz
/// - Head angle: NetworkVariable (float), 20Hz
/// - Attack mode: NetworkVariable (bool)
/// - Melee attack: ServerRpc → ClientRpc trigger
/// </summary>
public class DragonCombatController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private AnimalGroundController groundController;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform cam;

    [Header("Spine Bones (for upper body twist)")]
    [Tooltip("Lowest spine bone in the twist chain (gets least rotation).")]
    [SerializeField] private Transform spineBone;
    [Tooltip("Middle spine bone.")]
    [SerializeField] private Transform spine1Bone;
    [Tooltip("Upper spine bone (gets most rotation).")]
    [SerializeField] private Transform spine2Bone;

    [Header("Head Bone (for head tracking)")]
    [Tooltip("Neck1 bone — the head-level bone that follows camera yaw in Attack Mode.")]
    [SerializeField] private Transform neck1Bone;

    [Header("Upper Body Twist")]
    [Tooltip("Maximum twist angle in degrees (each direction).")]
    [SerializeField] private float maxTwistAngle = 60f;
    [Tooltip("How fast the twist catches up to the target angle (deg/sec).")]
    [SerializeField] private float twistSpeed = 180f;
    [Tooltip("How fast the twist returns to center on release (deg/sec).")]
    [SerializeField] private float twistReturnSpeed = 120f;
    [Tooltip("Rotation weight for Spine bone (lowest). All three should sum to ~1.")]
    [SerializeField] private float spineWeight = 0.2f;
    [Tooltip("Rotation weight for Spine1 bone (middle).")]
    [SerializeField] private float spine1Weight = 0.3f;
    [Tooltip("Rotation weight for Spine2 bone (upper, gets most).")]
    [SerializeField] private float spine2Weight = 0.5f;

    [Header("Head Tracking")]
    [Tooltip("Maximum head turn angle in degrees (each direction).")]
    [SerializeField] private float maxHeadAngle = 80f;
    [Tooltip("How fast the head turns toward the camera (deg/sec).")]
    [SerializeField] private float headTurnSpeed = 200f;
    [Tooltip("How fast the head returns to center when exiting Attack Mode (deg/sec).")]
    [SerializeField] private float headReturnSpeed = 150f;

    [Header("Melee Attack")]
    [Tooltip("GaitSpeed must be below this to allow melee attack.")]
    [SerializeField] private float stationaryThreshold = 0.1f;

    [Header("Input")]
    [SerializeField] private KeyCode meleeKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode aimKey = KeyCode.Mouse1;
    [SerializeField] private KeyCode attackModeKey = KeyCode.Alpha1;

    // ─── Animator Hashes ─────────────────────────────────
    private int meleeAttackHash;
    private int attackModeHash;

    // ─── Twist State ─────────────────────────────────────
    private float _currentTwistAngle;
    private float _targetTwistAngle;
    private bool _isAiming;

    // ─── Head State ──────────────────────────────────────
    private float _currentHeadAngle;
    private float _targetHeadAngle;

    // ─── Attack Mode State ───────────────────────────────
    private bool _isAttackMode;

    // ─── Attack Twist (captured at attack fire, independent of head) ─
    private float _attackTwistAngle;

    // ─── Network ─────────────────────────────────────────
    private NetworkVariable<float> netTwistAngle = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<float> netHeadAngle = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> netIsAttackMode = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private const float NETWORK_UPDATE_INTERVAL = 0.05f; // 20Hz
    private float _nextNetworkUpdate;
    private const float FLOAT_EPSILON = 0.5f;

    // ═══════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═══════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInParent<Animator>();
        if (groundController == null)
            groundController = GetComponentInParent<AnimalGroundController>();
        if (rb == null)
            rb = GetComponentInParent<Rigidbody>();
        if (cam == null)
            cam = Camera.main?.transform;

        meleeAttackHash = Animator.StringToHash("MeleeAttack");
        attackModeHash  = Animator.StringToHash("AttackMode");
    }

    private void Update()
    {
        if (!IsOwner) return;

        HandleAttackModeToggle();
        HandleAimInput();
        HandleAttackInput();
        bool isStationary = groundController == null || groundController.GaitSpeed <= stationaryThreshold;
        UpdateTwistAngle(isStationary);
        UpdateHeadAngle(isStationary);
        SyncToNetwork();
    }

    private void LateUpdate()
    {
        // Owner uses local values; remotes use NetworkVariables
        float twist = IsOwner ? _currentTwistAngle : netTwistAngle.Value;
        float head  = IsOwner ? _currentHeadAngle  : netHeadAngle.Value;

        ApplySpineTwist(twist);
        ApplyHeadTurn(head);
    }

    // ═══════════════════════════════════════════════════════════════
    // ATTACK MODE TOGGLE
    // ═══════════════════════════════════════════════════════════════

    private void HandleAttackModeToggle()
    {
        if (!Input.GetKeyDown(attackModeKey)) return;

        _isAttackMode = !_isAttackMode;

        // If exiting attack mode, cancel any aiming too
        if (!_isAttackMode)
            _isAiming = false;

        SetAttackModeServerRpc(_isAttackMode);
    }

    [ServerRpc]
    private void SetAttackModeServerRpc(bool value)
    {
        netIsAttackMode.Value = value;
        SetAttackModeClientRpc(value);
    }

    [ClientRpc]
    private void SetAttackModeClientRpc(bool value)
    {
        if (animator != null)
            animator.SetBool(attackModeHash, value);
    }

    // ═══════════════════════════════════════════════════════════════
    // INPUT
    // ═══════════════════════════════════════════════════════════════

    private void HandleAimInput()
    {
        // Right-click aim is suppressed in attack mode
        // Do NOT touch _targetTwistAngle here — UpdateTwistAngle owns it in attack mode
        if (_isAttackMode)
        {
            _isAiming = false;
            return;
        }

        _isAiming = Input.GetKey(aimKey);

        if (_isAiming && cam != null && rb != null)
        {
            float cameraYaw = cam.eulerAngles.y;
            float bodyYaw   = rb.rotation.eulerAngles.y;
            float delta     = Mathf.DeltaAngle(cameraYaw, bodyYaw);
            _targetTwistAngle = Mathf.Clamp(delta, -maxTwistAngle, maxTwistAngle);
        }
        else
        {
            _targetTwistAngle = 0f;
        }
    }

    private void HandleAttackInput()
    {
        if (Input.GetKeyDown(meleeKey) && CanAttack())
            TriggerMeleeAttack();
    }

    private bool CanAttack()
    {
        if (groundController == null) return false;
        return groundController.GaitSpeed <= stationaryThreshold;
    }

    // ═══════════════════════════════════════════════════════════════
    // TWIST
    // ═══════════════════════════════════════════════════════════════

    private void UpdateTwistAngle(bool isStationary)
    {
        if (!isStationary)
        {
            // Moving — return spine to zero, blend tree handles body direction
            _targetTwistAngle = 0f;
            _currentTwistAngle = Mathf.MoveTowards(_currentTwistAngle, 0f, twistReturnSpeed * Time.deltaTime);
            return;
        }

        // During melee attack in attack mode, spine chases the captured attack angle (not head)
        if (_isAttackMode && animator != null &&
            animator.GetCurrentAnimatorStateInfo(0).IsTag("MeleeAttack"))
        {
            _targetTwistAngle = _attackTwistAngle;
        }
        // Spine holds its angle after attack — no auto-return to zero

        float speed = (_isAiming || (_isAttackMode && animator != null &&
            animator.GetCurrentAnimatorStateInfo(0).IsTag("MeleeAttack")))
            ? twistSpeed : twistReturnSpeed;
        _currentTwistAngle = Mathf.MoveTowards(_currentTwistAngle, _targetTwistAngle, speed * Time.deltaTime);
    }

    private void ApplySpineTwist(float twistAngle)
    {
        if (Mathf.Abs(twistAngle) < 0.01f) return;

        // Rotate around world Y (yaw only) by pre-multiplying in world space:
        // worldRot = Quaternion.AngleAxis(angle, Vector3.up) * bone.rotation
        // then convert back to local via parent.inverseRotation
        Quaternion yaw = Quaternion.AngleAxis(-twistAngle, Vector3.up);

        if (spineBone != null)
            spineBone.rotation = Quaternion.Slerp(spineBone.rotation,
                yaw * spineBone.rotation, spineWeight);
        if (spine1Bone != null)
            spine1Bone.rotation = Quaternion.Slerp(spine1Bone.rotation,
                yaw * spine1Bone.rotation, spine1Weight);
        if (spine2Bone != null)
            spine2Bone.rotation = Quaternion.Slerp(spine2Bone.rotation,
                yaw * spine2Bone.rotation, spine2Weight);
    }

    // ═══════════════════════════════════════════════════════════════
    // HEAD TRACKING
    // ═══════════════════════════════════════════════════════════════

    private void UpdateHeadAngle(bool isStationary)
    {
        if (!isStationary)
        {
            // Moving — return head to zero, blend tree handles body direction
            _targetHeadAngle = 0f;
            _currentHeadAngle = Mathf.MoveTowards(_currentHeadAngle, 0f, headReturnSpeed * Time.deltaTime);
            return;
        }

        bool isMeleeActive = _isAttackMode && animator != null &&
            animator.GetCurrentAnimatorStateInfo(0).IsTag("MeleeAttack");

        if (isMeleeActive)
        {
            // Head goes to zero during attack — spine handles the directional swing independently
            _targetHeadAngle = 0f;
        }
        else if (_isAttackMode && cam != null && rb != null)
        {
            float cameraYaw = cam.eulerAngles.y;
            float bodyYaw   = rb.rotation.eulerAngles.y;
            float delta     = Mathf.DeltaAngle(cameraYaw, bodyYaw);
            // Subtract spine twist so head doesn't double up — total rotation = camera direction
            float compensated = delta - _currentTwistAngle;
            _targetHeadAngle = Mathf.Clamp(compensated, -maxHeadAngle, maxHeadAngle);
        }
        else
        {
            _targetHeadAngle = 0f;
        }

        float speed = _isAttackMode ? headTurnSpeed : headReturnSpeed;
        _currentHeadAngle = Mathf.MoveTowards(_currentHeadAngle, _targetHeadAngle, speed * Time.deltaTime);
    }

    private void ApplyHeadTurn(float headAngle)
    {
        if (neck1Bone == null) return;
        if (Mathf.Abs(headAngle) < 0.01f) return;

        Quaternion yaw = Quaternion.AngleAxis(-headAngle, Vector3.up);
        neck1Bone.rotation = yaw * neck1Bone.rotation;
    }

    // ═══════════════════════════════════════════════════════════════
    // MELEE ATTACK
    // ═══════════════════════════════════════════════════════════════

    private void TriggerMeleeAttack()
    {
        // Capture current camera-to-body angle as the spine target for this attack
        if (cam != null && rb != null)
        {
            float cameraYaw = cam.eulerAngles.y;
            float bodyYaw   = rb.rotation.eulerAngles.y;
            _attackTwistAngle = Mathf.Clamp(
                Mathf.DeltaAngle(cameraYaw, bodyYaw), -maxTwistAngle, maxTwistAngle);
        }
        MeleeAttackServerRpc();
    }

    [ServerRpc]
    private void MeleeAttackServerRpc()
    {
        MeleeAttackClientRpc();
    }

    [ClientRpc]
    private void MeleeAttackClientRpc()
    {
        if (animator != null)
            animator.SetTrigger(meleeAttackHash);
    }

    // ═══════════════════════════════════════════════════════════════
    // NETWORK SYNC
    // ═══════════════════════════════════════════════════════════════

    private void SyncToNetwork()
    {
        if (Time.time < _nextNetworkUpdate) return;
        _nextNetworkUpdate = Time.time + NETWORK_UPDATE_INTERVAL;

        // Twist
        if (Mathf.Abs(netTwistAngle.Value - _currentTwistAngle) > FLOAT_EPSILON)
            netTwistAngle.Value = _currentTwistAngle;
        if (_currentTwistAngle == 0f && netTwistAngle.Value != 0f)
            netTwistAngle.Value = 0f;

        // Head
        if (Mathf.Abs(netHeadAngle.Value - _currentHeadAngle) > FLOAT_EPSILON)
            netHeadAngle.Value = _currentHeadAngle;
        if (_currentHeadAngle == 0f && netHeadAngle.Value != 0f)
            netHeadAngle.Value = 0f;
    }

    // ═══════════════════════════════════════════════════════════════
    // PUBLIC STATE
    // ═══════════════════════════════════════════════════════════════

    public bool IsAiming      => _isAiming;
    public bool IsAttackMode  => _isAttackMode;
    public float TwistAngle   => _currentTwistAngle;
    public float HeadAngle    => _currentHeadAngle;
}
