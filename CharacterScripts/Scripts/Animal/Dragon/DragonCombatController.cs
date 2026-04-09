using UnityEngine;
using Unity.Netcode;

/// <summary>//
/// Dragon combat controller. Handles:
/// - Press 1: toggle Melee Attack Mode (attack idle, head yaw tracking, left-click melee)
/// - Press 2: toggle Fire Breath Mode (attack idle, head yaw+pitch tracking, hold left-click to breathe fire)
/// - Modes are mutually exclusive; pressing the active mode's key toggles it off.
///
/// Melee mode:
/// - Head (Neck1) tracks camera yaw while stationary
/// - Left-click: triggers melee attack, spine twists to camera direction during attack
///
/// Fire Breath mode:
/// - Head (Neck1) tracks camera yaw AND pitch while stationary
/// - Hold left-click: continuous fire breath (IsBreathingFire animator bool)
/// - No spine twist — neck alone handles aiming
///
/// Network sync:
/// - Spine twist angle: NetworkVariable (float), 20Hz
/// - Head yaw angle: NetworkVariable (float), 20Hz
/// - Head pitch angle: NetworkVariable (float), 20Hz
/// - Attack mode: NetworkVariable (int) — 0=none, 1=melee, 2=firebreath
/// - IsBreathingFire: NetworkVariable (bool)
/// - Melee attack: ServerRpc → ClientRpc trigger
/// </summary>
public class DragonCombatController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private AnimalGroundController groundController;
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform cam;

    [Header("Spine Bones (for melee upper body twist)")]
    [SerializeField] private Transform spineBone;
    [SerializeField] private Transform spine1Bone;
    [SerializeField] private Transform spine2Bone;

    [Header("Neck Bones (for head tracking — Neck closest to body, Neck4 closest to head)")]
    [SerializeField] private Transform neckBone;
    [SerializeField] private Transform neck1Bone;
    [SerializeField] private Transform neck2Bone;
    [SerializeField] private Transform neck3Bone;
    [SerializeField] private Transform neck4Bone;

    [Header("Neck Yaw Weights (should sum to ~1)")]
    [SerializeField] private float neckYawWeight  = 0.05f;
    [SerializeField] private float neck1YawWeight = 0.10f;
    [SerializeField] private float neck2YawWeight = 0.20f;
    [SerializeField] private float neck3YawWeight = 0.30f;
    [SerializeField] private float neck4YawWeight = 0.35f;

    [Header("Fire Breath VFX")]
    [Tooltip("VFX prefab to instantiate when breathing fire (particle system).")]
    [SerializeField] private GameObject fireBreathVFXPrefab;
    [Tooltip("Where to spawn the fire breath VFX (e.g. mouth bone).")]
    [SerializeField] private Transform fireBreathSpawnPoint;

    [Header("Melee Attack VFX")]
    [Tooltip("VFX prefab to instantiate on melee attack.")]
    [SerializeField] private GameObject meleeAttackVFXPrefab;
    [Tooltip("Where to spawn the melee VFX.")]
    [SerializeField] private Transform meleeAttackSpawnPoint;

    [Header("Sound")]
    [SerializeField] private DragonSoundPlayer soundPlayer;
    [Tooltip("Melee attack type for sound: claw, bite, or tail.")]
    [SerializeField] private string meleeAttackSoundType = "claw";

    [Header("Jaw Bone (procedural mouth open for fire breath)")]
    [SerializeField] private Transform jawBone;
    [Tooltip("Jaw local Z rotation when closed.")]
    [SerializeField] private float jawClosedZ = -106.534f;
    [Tooltip("Jaw local Z rotation when fully open.")]
    [SerializeField] private float jawOpenZ = -125f;
    [Tooltip("How fast the jaw opens/closes (deg/sec).")]
    [SerializeField] private float jawSpeed = 300f;

    [Header("Upper Body Twist (Melee Only)")]
    [SerializeField] private float maxTwistAngle = 60f;
    [SerializeField] private float twistSpeed = 180f;
    [SerializeField] private float twistReturnSpeed = 120f;
    [SerializeField] private float spineWeight = 0.2f;
    [SerializeField] private float spine1Weight = 0.3f;
    [SerializeField] private float spine2Weight = 0.5f;

    [Header("Head Tracking")]
    [SerializeField] private float maxHeadAngle = 80f;
    [SerializeField] private float maxHeadPitch = 45f;
    [SerializeField] private float headTurnSpeed = 200f;
    [SerializeField] private float headReturnSpeed = 150f;
    [Tooltip("Pitch offset to correct for bone rest pose. Positive = tilt head up.")]
    [SerializeField] private float headPitchOffset = 0f;

    [Header("Melee Attack")]
    [SerializeField] private float stationaryThreshold = 0.1f;

    [Header("Input")]
    [SerializeField] private KeyCode primaryKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode meleeModeKey = KeyCode.Alpha1;
    [SerializeField] private KeyCode fireBreathModeKey = KeyCode.Alpha2;

    // ─── Animator Hashes ─────────────────────────────────
    private int meleeAttackHash;
    private int attackModeHash;
    private int isBreathingFireHash;

    // ─── Twist State (melee only) ────────────────────────
    private float _currentTwistAngle;
    private float _targetTwistAngle;

    // ─── Head State ──────────────────────────────────────
    private float _currentHeadYaw;
    private float _targetHeadYaw;
    private float _currentHeadPitch;
    private float _targetHeadPitch;

    // ─── Attack Mode State ───────────────────────────────
    // 0 = none, 1 = melee, 2 = fire breath
    private int _attackMode;

    // ─── Fire Breath State ───────────────────────────────
    private bool _isBreathingFire;
    private float _currentJawZ;
    private GameObject _activeFireBreathInstance;

    // ─── Attack Twist (captured at melee fire, independent of head) ─
    private float _attackTwistAngle;

    // ─── Network ─────────────────────────────────────────
    private NetworkVariable<float> netTwistAngle = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<float> netHeadYaw = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<float> netHeadPitch = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<int> netAttackMode = new NetworkVariable<int>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> netIsBreathingFire = new NetworkVariable<bool>(
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

        meleeAttackHash      = Animator.StringToHash("MeleeAttack");
        attackModeHash       = Animator.StringToHash("AttackMode");
        isBreathingFireHash  = Animator.StringToHash("IsBreathingFire");

        _currentJawZ = jawClosedZ;
    }

    private void Update()
    {
        if (!IsOwner) return;

        HandleAttackModeToggle();
        HandleCombatInput();
        bool isStationary = groundController == null || groundController.GaitSpeed <= stationaryThreshold;
        UpdateTwistAngle(isStationary);
        UpdateHeadAngles(isStationary);
        SyncToNetwork();
    }

    private void LateUpdate()
    {
        // Owner uses local values; remotes use NetworkVariables
        float twist    = IsOwner ? _currentTwistAngle : netTwistAngle.Value;
        float headYaw  = IsOwner ? _currentHeadYaw    : netHeadYaw.Value;
        float headPitch = IsOwner ? _currentHeadPitch  : netHeadPitch.Value;

        ApplySpineTwist(twist);
        ApplyHeadTurn(headYaw, headPitch);
        ApplyJaw();
    }

    // ═══════════════════════════════════════════════════════════════
    // ATTACK MODE TOGGLE
    // ═══════════════════════════════════════════════════════════════

    private void HandleAttackModeToggle()
    {
        int newMode = _attackMode;

        if (Input.GetKeyDown(meleeModeKey))
            newMode = (_attackMode == 1) ? 0 : 1;
        else if (Input.GetKeyDown(fireBreathModeKey))
            newMode = (_attackMode == 2) ? 0 : 2;

        if (newMode == _attackMode) return;

        // Clean up previous mode
        if (_attackMode == 2 && _isBreathingFire)
        {
            _isBreathingFire = false;
            SetBreathingFireServerRpc(false);
        }

        _attackMode = newMode;
        netAttackMode.Value = newMode; // Owner writes directly
        SetAttackModeServerRpc(_attackMode);
    }

    [ServerRpc]
    private void SetAttackModeServerRpc(int value)
    {
        netAttackMode.Value = value;
        SetAttackModeClientRpc(value);
    }

    [ClientRpc]
    private void SetAttackModeClientRpc(int value)
    {
        if (animator != null)
            animator.SetInteger(attackModeHash, value);
    }

    // ═══════════════════════════════════════════════════════════════
    // COMBAT INPUT
    // ═══════════════════════════════════════════════════════════════

    private void HandleCombatInput()
    {
        if (_attackMode == 1)
        {
            // Melee mode: left-click triggers melee attack
            if (Input.GetKeyDown(primaryKey) && CanMeleeAttack())
                TriggerMeleeAttack();
        }
        else if (_attackMode == 2)
        {
            // Fire breath mode: hold left-click for continuous fire
            bool wantFire = Input.GetKey(primaryKey) && CanFireBreath();
            if (wantFire != _isBreathingFire)
            {
                _isBreathingFire = wantFire;
                netIsBreathingFire.Value = wantFire; // Owner writes directly
                SetBreathingFireServerRpc(_isBreathingFire);
            }
        }
    }

    private bool CanMeleeAttack()
    {
        if (groundController == null) return false;
        return groundController.GaitSpeed <= stationaryThreshold;
    }

    private bool IsStationary()
    {
        return groundController == null || groundController.GaitSpeed <= stationaryThreshold;
    }

    /// <summary>True when fire breath is allowed — stationary on ground OR in flight.</summary>
    private bool CanFireBreath()
    {
        if (flightController != null && flightController.IsFlightMode) return true;
        return IsStationary();
    }

    [ServerRpc]
    private void SetBreathingFireServerRpc(bool value)
    {
        netIsBreathingFire.Value = value;
        SetBreathingFireClientRpc(value);
    }

    [ClientRpc]
    private void SetBreathingFireClientRpc(bool value)
    {
        if (animator != null)
            animator.SetBool(isBreathingFireHash, value);

        // VFX: spawn / destroy fire breath particle effect on ALL clients
        if (value)
        {
            if (fireBreathVFXPrefab != null && _activeFireBreathInstance == null)
            {
                Transform parent = fireBreathSpawnPoint != null ? fireBreathSpawnPoint : transform;
                _activeFireBreathInstance = Instantiate(fireBreathVFXPrefab, parent.position, parent.rotation, parent);
            }
        }
        else
        {
            if (_activeFireBreathInstance != null)
            {
                Destroy(_activeFireBreathInstance);
                _activeFireBreathInstance = null;
            }
        }

        // SFX: fire breath start/end through DragonSoundPlayer
        if (soundPlayer != null)
        {
            if (value)
                soundPlayer.OnFireBreathStart();
            else
                soundPlayer.OnFireBreathEnd();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SPINE TWIST (Melee mode only)
    // ═══════════════════════════════════════════════════════════════

    private void UpdateTwistAngle(bool isStationary)
    {
        if (!isStationary || _attackMode == 0)
        {
            // Not stationary or not in melee mode — return spine to zero
            _targetTwistAngle = 0f;
            _currentTwistAngle = Mathf.MoveTowards(_currentTwistAngle, 0f, twistReturnSpeed * Time.deltaTime);
            return;
        }

        // During melee attack animation, spine chases the captured attack angle
        if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsTag("MeleeAttack"))
        {
            _targetTwistAngle = _attackTwistAngle;
        }
        // Spine holds its angle after attack — no auto-return to zero

        float speed = (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsTag("MeleeAttack"))
            ? twistSpeed : twistReturnSpeed;
        _currentTwistAngle = Mathf.MoveTowards(_currentTwistAngle, _targetTwistAngle, speed * Time.deltaTime);
    }

    private void ApplySpineTwist(float twistAngle)
    {
        if (Mathf.Abs(twistAngle) < 0.01f) return;

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

    private void UpdateHeadAngles(bool isStationary)
    {
        bool inFlight = flightController != null && flightController.IsFlightMode;
        bool canTrack = isStationary || inFlight;

        // ── YAW ──
        if (!canTrack || _attackMode == 0)
        {
            // Moving or no attack mode — return head to zero
            _targetHeadYaw = 0f;
            _currentHeadYaw = Mathf.MoveTowards(_currentHeadYaw, 0f, headReturnSpeed * Time.deltaTime);
        }
        else if (_attackMode == 1)
        {
            // Melee mode — yaw only
            bool isMeleeActive = animator != null &&
                animator.GetCurrentAnimatorStateInfo(0).IsTag("MeleeAttack");

            if (isMeleeActive)
            {
                _targetHeadYaw = 0f;
            }
            else if (cam != null && rb != null)
            {
                float cameraYaw = cam.eulerAngles.y;
                float bodyYaw   = rb.rotation.eulerAngles.y;
                float delta     = Mathf.DeltaAngle(cameraYaw, bodyYaw);
                float compensated = delta - _currentTwistAngle;
                _targetHeadYaw = Mathf.Clamp(compensated, -maxHeadAngle, maxHeadAngle);
            }

            float yawSpeed = headTurnSpeed;
            _currentHeadYaw = Mathf.MoveTowards(_currentHeadYaw, _targetHeadYaw, yawSpeed * Time.deltaTime);
        }
        else if (_attackMode == 2)
        {
            // Fire breath mode — yaw tracks camera, compensated for spine twist
            if (cam != null && rb != null)
            {
                float cameraYaw = cam.eulerAngles.y;
                float bodyYaw   = rb.rotation.eulerAngles.y;
                float delta     = Mathf.DeltaAngle(cameraYaw, bodyYaw);
                float compensated = delta - _currentTwistAngle;
                _targetHeadYaw = Mathf.Clamp(compensated, -maxHeadAngle, maxHeadAngle);
            }

            _currentHeadYaw = Mathf.MoveTowards(_currentHeadYaw, _targetHeadYaw, headTurnSpeed * Time.deltaTime);
        }

        // ── PITCH (fire breath mode only) ──
        if (_attackMode == 2 && canTrack && cam != null)
        {
            float cameraPitch = cam.eulerAngles.x;
            if (cameraPitch > 180f) cameraPitch -= 360f;
            // Camera pitch: positive = looking down, negative = looking up
            // Head should match: positive pitch = head down, negative = head up
            _targetHeadPitch = Mathf.Clamp(cameraPitch + headPitchOffset, -maxHeadPitch, maxHeadPitch);
            _currentHeadPitch = Mathf.MoveTowards(_currentHeadPitch, _targetHeadPitch, headTurnSpeed * Time.deltaTime);
        }
        else
        {
            _targetHeadPitch = 0f;
            _currentHeadPitch = Mathf.MoveTowards(_currentHeadPitch, 0f, headReturnSpeed * Time.deltaTime);
        }
    }

    private void ApplyHeadTurn(float headYaw, float headPitch)
    {
        bool hasYaw   = Mathf.Abs(headYaw)   > 0.01f;
        bool hasPitch = Mathf.Abs(headPitch)  > 0.01f;

        if (!hasYaw && !hasPitch) return;

        // Step 1: Distribute yaw across all neck bones for a natural curve
        if (hasYaw)
        {
            ApplyNeckYaw(neckBone,  headYaw, neckYawWeight);
            ApplyNeckYaw(neck1Bone, headYaw, neck1YawWeight);
            ApplyNeckYaw(neck2Bone, headYaw, neck2YawWeight);
            ApplyNeckYaw(neck3Bone, headYaw, neck3YawWeight);
            ApplyNeckYaw(neck4Bone, headYaw, neck4YawWeight);
        }

        // Step 2: Apply pitch to Neck3 only (head-end bone)
        // Start from the body's right axis (which works twist-free), then rotate
        // it by the head yaw so pitch stays relative to where the head is pointing
        if (hasPitch && rb != null && neck3Bone != null)
        {
            Quaternion yawRotation = Quaternion.AngleAxis(-headYaw, Vector3.up);
            Vector3 pitchAxis = yawRotation * (rb.rotation * Vector3.right);
            Quaternion pitch = Quaternion.AngleAxis(headPitch, pitchAxis);
            neck3Bone.rotation = pitch * neck3Bone.rotation;
        }
    }

    private void ApplyNeckYaw(Transform bone, float headYaw, float weight)
    {
        if (bone == null) return;
        Quaternion yaw = Quaternion.AngleAxis(-headYaw * weight, Vector3.up);
        bone.rotation = yaw * bone.rotation;
    }

    // ═══════════════════════════════════════════════════════════════
    // JAW (procedural mouth open for fire breath)
    // ═══════════════════════════════════════════════════════════════

    private void ApplyJaw()
    {
        if (jawBone == null) return;

        bool breathing = IsOwner ? _isBreathingFire : netIsBreathingFire.Value;
        float targetZ = breathing ? jawOpenZ : jawClosedZ;
        _currentJawZ = Mathf.MoveTowards(_currentJawZ, targetZ, jawSpeed * Time.deltaTime);

        Vector3 euler = jawBone.localEulerAngles;
        euler.z = _currentJawZ;
        jawBone.localEulerAngles = euler;
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

        // VFX: spawn melee effect on ALL clients
        if (meleeAttackVFXPrefab != null)
        {
            Transform parent = meleeAttackSpawnPoint != null ? meleeAttackSpawnPoint : transform;
            GameObject fx = Instantiate(meleeAttackVFXPrefab, parent.position, parent.rotation, parent);
            Destroy(fx, 3f);
        }

        // SFX: melee attack sound through DragonSoundPlayer
        if (soundPlayer != null)
            soundPlayer.OnMeleeAttack(meleeAttackSoundType);
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

        // Head yaw
        if (Mathf.Abs(netHeadYaw.Value - _currentHeadYaw) > FLOAT_EPSILON)
            netHeadYaw.Value = _currentHeadYaw;
        if (_currentHeadYaw == 0f && netHeadYaw.Value != 0f)
            netHeadYaw.Value = 0f;

        // Head pitch
        if (Mathf.Abs(netHeadPitch.Value - _currentHeadPitch) > FLOAT_EPSILON)
            netHeadPitch.Value = _currentHeadPitch;
        if (_currentHeadPitch == 0f && netHeadPitch.Value != 0f)
            netHeadPitch.Value = 0f;
    }

    // ═══════════════════════════════════════════════════════════════
    // PUBLIC STATE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>0=none, 1=melee, 2=firebreath</summary>
    public int AttackMode      => _attackMode;
    public bool IsBreathingFire => _isBreathingFire;
    public float TwistAngle    => _currentTwistAngle;
    public float HeadYaw       => _currentHeadYaw;
    public float HeadPitch     => _currentHeadPitch;
}
