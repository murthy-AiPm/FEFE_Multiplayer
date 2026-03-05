using System.Reflection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class ClientAuthoritativeAnimancerSync : NetworkBehaviour
{
    [Header("Auto-wired if left empty")]
    [SerializeField] private InputController input;
    [SerializeField] private ThirdPersonController tps;
    [SerializeField] private RuleAnimancerDriver animDriver;
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private CombatController combatController;

    // ---- Networked "AnimationContext" pieces ----
    private readonly NetworkVariable<bool> nvMoving =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvCombatMode =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvModified =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvSecondaryHeld =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvHoverMode =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvSheathing =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvGrounded =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvFreeFall =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<FixedString32Bytes> nvDirections =
        new(new FixedString32Bytes("None"), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Snapshot fields
    private readonly NetworkVariable<bool> nvPrimaryDown =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvPrimaryHeld =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvPrimaryUp =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvJumpDown =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvJumpHeld =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvActionHeld =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Action edge
    private readonly NetworkVariable<int> nvActionId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<int> nvActionSeq =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Attack state networking
    private readonly NetworkVariable<NetworkedAttackState> nvAttackState =
        new(new NetworkedAttackState(), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<int> nvAttackSeq =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Jump edge
    private readonly NetworkVariable<int> nvJumpSeq =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ---- Combat State Syncing ----
    private readonly NetworkVariable<bool> nvEquipping =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvHolstering =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvDodging =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvBlocking =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvBowDrawing =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvBowAiming =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> nvFistCombatMode =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ---- FIX 1: Bow release edge — sequence counter so a single-frame
    //             primaryUp is never dropped between 20 Hz ticks. --------
    private readonly NetworkVariable<int> nvBowReleaseSeq =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private int _lastSeenBowReleaseSeq;

    // ---- FIX 2: Owner's camera pitch for spine IK on remote puppets. ---
    //             Packed as a short (degrees * 10) to save bandwidth.
    private readonly NetworkVariable<float> nvAimPitch =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Public accessor so RuleAnimancerDriver can read it without reflection
    public float RemoteAimPitch => nvAimPitch.Value;

    // Reflection to set InputController.Snapshot
    private FieldInfo _snapshotBackingField;

    private int _lastSeenActionSeq;
    private int _lastSeenAttackSeq;
    private int _lastSeenJumpSeq;

    // Track whether bow was aiming last tick so we catch the release edge
    private bool _wasAiming;

    // Throttling
    private const float NETWORK_UPDATE_INTERVAL = 0.05f; // 20 Hz
    private float nextNetworkUpdateTime;

    private void Awake()
    {
        AutoWire();
        CacheSnapshotSetter();
    }

    private void AutoWire()
    {
        if (!input) input = GetComponentInChildren<InputController>(true);
        if (!tps) tps = GetComponentInChildren<ThirdPersonController>(true);
        if (!animDriver) animDriver = GetComponentInChildren<RuleAnimancerDriver>(true);
        if (!weaponManager) weaponManager = GetComponentInChildren<WeaponManager>(true);
        if (!combatController) combatController = GetComponentInChildren<CombatController>(true);
    }

    private void CacheSnapshotSetter()
    {
        if (input == null) return;
        _snapshotBackingField = typeof(InputController).GetField(
            "<Snapshot>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        AutoWire();
        CacheSnapshotSetter();

        if (!IsOwner)
        {
            ApplyToRemoteHolders();
            _lastSeenActionSeq     = nvActionSeq.Value;
            _lastSeenAttackSeq     = nvAttackSeq.Value;
            _lastSeenJumpSeq       = nvJumpSeq.Value;
            _lastSeenBowReleaseSeq = nvBowReleaseSeq.Value;
        }
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            if (Time.time >= nextNetworkUpdateTime)
            {
                nextNetworkUpdateTime = Time.time + NETWORK_UPDATE_INTERVAL;
                WriteFromOwner();
            }
        }
        else
        {
            ApplyToRemoteHolders();
            ApplyRemoteActionEdge();
            ApplyRemoteAttackEdge();
            ApplyRemoteJumpEdge();
            ApplyRemoteBowReleaseEdge();  // FIX 1
        }
    }

    private void WriteFromOwner()
    {
        if (input == null || tps == null) return;

        // Input state
        if (nvMoving.Value     != input.isMoving)          nvMoving.Value          = input.isMoving;
        if (nvCombatMode.Value != input.isCombatMode)      nvCombatMode.Value      = input.isCombatMode;
        if (nvModified.Value   != input.isModified)        nvModified.Value        = input.isModified;
        if (nvSecondaryHeld.Value != input.isSecondaryAttack) nvSecondaryHeld.Value = input.isSecondaryAttack;
        if (nvHoverMode.Value  != input.isHoverMode)       nvHoverMode.Value       = input.isHoverMode;
        if (nvSheathing.Value  != input.isSheating)        nvSheathing.Value       = input.isSheating;

        if (nvGrounded.Value   != tps.isgrounded)          nvGrounded.Value        = tps.isgrounded;
        if (nvFreeFall.Value   != tps.isfreeFall)          nvFreeFall.Value        = tps.isfreeFall;

        var dirStr = new FixedString32Bytes(string.IsNullOrEmpty(input.directions) ? "None" : input.directions);
        if (!nvDirections.Value.Equals(dirStr)) nvDirections.Value = dirStr;

        var s = input.Snapshot;
        if (nvPrimaryDown.Value != s.primaryDown) nvPrimaryDown.Value = s.primaryDown;
        if (nvPrimaryHeld.Value != s.primaryHeld) nvPrimaryHeld.Value = s.primaryHeld;
        if (nvPrimaryUp.Value   != s.primaryUp)   nvPrimaryUp.Value   = s.primaryUp;
        if (nvJumpDown.Value    != s.jumpDown)     nvJumpDown.Value    = s.jumpDown;
        if (nvJumpHeld.Value    != s.jumpHeld)     nvJumpHeld.Value    = s.jumpHeld;
        if (nvActionHeld.Value  != s.actionHeld)   nvActionHeld.Value  = s.actionHeld;

        if (s.jumpDown) nvJumpSeq.Value++;

        // ── FIX 1: Bow release edge ──────────────────────────────────────
        // Detect the falling edge of BowAiming (owner just released the draw)
        // and bump a sequence counter so remotes always see the event even if
        // the bool flips back to false between two 20 Hz write ticks.
        if (combatController != null)
        {
            bool isAimingNow = combatController.IsBowAiming;
            if (_wasAiming && !isAimingNow)
                nvBowReleaseSeq.Value++;
            _wasAiming = isAimingNow;
        }

        // ── FIX 2: Aim pitch ─────────────────────────────────────────────
        // Sync camera pitch every tick so remote puppets can drive spine IK
        // with the owner's actual look angle instead of the observer's camera.
        if (Camera.main != null)
        {
            float pitch = Camera.main.transform.eulerAngles.x;
            if (pitch > 180f) pitch -= 360f;            // wrap to [-180, 180]
            // Only send when it changed by more than 0.5° to reduce noise
            if (Mathf.Abs(nvAimPitch.Value - pitch) > 0.5f)
                nvAimPitch.Value = pitch;
        }

        // ── Combat State ─────────────────────────────────────────────────
        if (weaponManager != null)
        {
            bool equipping  = weaponManager.CurrentEquipState == WeaponManager.EquipState.Equipping;
            bool holstering = weaponManager.CurrentEquipState == WeaponManager.EquipState.Holstering;
            if (nvEquipping.Value  != equipping)  nvEquipping.Value  = equipping;
            if (nvHolstering.Value != holstering) nvHolstering.Value = holstering;
        }

        if (combatController != null)
        {
            if (nvDodging.Value       != combatController.IsDodging)       nvDodging.Value       = combatController.IsDodging;
            if (nvBlocking.Value      != combatController.IsBlocking)      nvBlocking.Value      = combatController.IsBlocking;
            if (nvBowDrawing.Value    != combatController.IsBowDrawing)    nvBowDrawing.Value    = combatController.IsBowDrawing;
            if (nvBowAiming.Value     != combatController.IsBowAiming)     nvBowAiming.Value     = combatController.IsBowAiming;
            if (nvFistCombatMode.Value != combatController.IsFistCombatMode) nvFistCombatMode.Value = combatController.IsFistCombatMode;
        }
    }

    private void ApplyToRemoteHolders()
    {
        if (input != null)
        {
            input.isMoving         = nvMoving.Value;
            input.isCombatMode     = nvCombatMode.Value;
            input.isModified       = nvModified.Value;
            input.isSecondaryAttack = nvSecondaryHeld.Value;
            input.isHoverMode      = nvHoverMode.Value;
            input.isSheating       = nvSheathing.Value;

            input.directions = nvDirections.Value.ToString();

            if (_snapshotBackingField != null)
            {
                var snap = new InputSnapshot
                {
                    primaryDown = nvPrimaryDown.Value,
                    primaryHeld = nvPrimaryHeld.Value,
                    primaryUp   = nvPrimaryUp.Value,
                    jumpDown    = nvJumpDown.Value,
                    jumpHeld    = nvJumpHeld.Value,
                    actionHeld  = nvActionHeld.Value,
                };
                _snapshotBackingField.SetValue(input, snap);
            }
        }

        if (tps != null)
        {
            tps.isgrounded = nvGrounded.Value;
            tps.isfreeFall = nvFreeFall.Value;
        }

        // ── Combat State to puppets ───────────────────────────────────────
        if (weaponManager != null)
        {
            if (nvEquipping.Value)
                weaponManager.SetRemoteEquipState(WeaponManager.EquipState.Equipping);
            else if (nvHolstering.Value)
                weaponManager.SetRemoteEquipState(WeaponManager.EquipState.Holstering);
            else
                weaponManager.SetRemoteEquipState(WeaponManager.EquipState.Idle);
        }

        if (combatController != null)
        {
            combatController.SetRemoteCombatState(
                nvDodging.Value,
                nvBlocking.Value,
                nvBowDrawing.Value,
                nvBowAiming.Value,
                nvFistCombatMode.Value
            );
        }
    }

    private void ApplyRemoteActionEdge()
    {
        if (animDriver == null) return;

        int seq = nvActionSeq.Value;
        if (seq == _lastSeenActionSeq) return;
        _lastSeenActionSeq = seq;

        int actionId = nvActionId.Value;
        if (actionId != 0)
            animDriver.StartAction(actionId);
    }

    private void ApplyRemoteAttackEdge()
    {
        if (animDriver == null) return;

        int seq = nvAttackSeq.Value;
        if (seq == _lastSeenAttackSeq) return;
        _lastSeenAttackSeq = seq;

        var attackState = nvAttackState.Value;
        animDriver.PlayNetworkedAttack(
            attackState.attackKey.ToString(),
            (RuleAnimancerDriver.AttackMode)attackState.mode,
            attackState.isHeavy,
            attackState.comboIndex
        );
    }

    private void ApplyRemoteJumpEdge()
    {
        if (input == null || _snapshotBackingField == null) return;

        int seq = nvJumpSeq.Value;
        if (seq == _lastSeenJumpSeq) return;
        _lastSeenJumpSeq = seq;

        var snap = new InputSnapshot
        {
            primaryDown = nvPrimaryDown.Value,
            primaryHeld = nvPrimaryHeld.Value,
            primaryUp   = nvPrimaryUp.Value,
            jumpDown    = true,
            jumpHeld    = nvJumpHeld.Value,
            actionHeld  = nvActionHeld.Value,
        };
        _snapshotBackingField.SetValue(input, snap);
    }

    // ── FIX 1: Bow release edge ───────────────────────────────────────────
    // When the release sequence ticks, inject a one-frame primaryUp=true into
    // the remote puppet's snapshot so the Bow/Release rule fires correctly.
    private void ApplyRemoteBowReleaseEdge()
    {
        if (input == null || _snapshotBackingField == null) return;

        int seq = nvBowReleaseSeq.Value;
        if (seq == _lastSeenBowReleaseSeq) return;
        _lastSeenBowReleaseSeq = seq;

        // Inject primaryUp for exactly one frame so the rule evaluator sees it
        var snap = new InputSnapshot
        {
            primaryDown = false,
            primaryHeld = false,
            primaryUp   = true,         // <-- the release edge
            jumpDown    = nvJumpDown.Value,
            jumpHeld    = nvJumpHeld.Value,
            actionHeld  = nvActionHeld.Value,
        };
        _snapshotBackingField.SetValue(input, snap);
    }

    // ─── Public API for owner ─────────────────────────────────────────────

    public void OwnerStartAction(int actionId)
    {
        if (!IsOwner) return;
        nvActionId.Value = actionId;
        nvActionSeq.Value++;
    }

    public void OwnerClearAction()
    {
        if (!IsOwner) return;
        nvActionId.Value = 0;
    }

    public void OwnerStartAttack(string attackKey, RuleAnimancerDriver.AttackMode mode, bool isHeavy, int comboIndex)
    {
        if (!IsOwner) return;
        nvAttackState.Value = new NetworkedAttackState
        {
            attackKey  = new FixedString64Bytes(attackKey),
            mode       = (byte)mode,
            isHeavy    = isHeavy,
            comboIndex = comboIndex
        };
        nvAttackSeq.Value++;
    }
}

/// <summary>
/// Networked representation of an attack state.
/// </summary>
public struct NetworkedAttackState : INetworkSerializable
{
    public FixedString64Bytes attackKey;
    public byte mode;
    public bool isHeavy;
    public int comboIndex;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref attackKey);
        serializer.SerializeValue(ref mode);
        serializer.SerializeValue(ref isHeavy);
        serializer.SerializeValue(ref comboIndex);
    }
}
