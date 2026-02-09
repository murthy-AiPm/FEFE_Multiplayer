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

    // Snapshot fields used by your rules/combos (PrimaryDown, JumpDown, ActionHeld, etc.)
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

    // ActionId (interactions like chest/ballista) + a "sequence" to create a one-shot edge on remotes.
    private readonly NetworkVariable<int> nvActionId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<int> nvActionSeq =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // NEW: Attack state networking
    private readonly NetworkVariable<NetworkedAttackState> nvAttackState =
        new(new NetworkedAttackState(), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<int> nvAttackSeq =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // NEW: Jump edge detection (same pattern as attack/action)
    private readonly NetworkVariable<int> nvJumpSeq =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Reflection to set InputController.Snapshot (private setter).
    private FieldInfo _snapshotBackingField;

    private int _lastSeenActionSeq;
    private int _lastSeenAttackSeq;
    private int _lastSeenJumpSeq;
    // Throttling
    private const float NETWORK_UPDATE_INTERVAL = 0.05f; // 20 Hz
    private float nextNetworkUpdateTime;
    private const float EPSILON = 0.01f;

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
    }

    private void CacheSnapshotSetter()
    {
        if (input == null) return;

        // Auto-property backing field name: "<Snapshot>k__BackingField"
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

        // Non-owners should apply received values into their local "data holder" components.
        if (!IsOwner)
        {
            // Apply once immediately.
            ApplyToRemoteHolders();

            // Track action edge.
            _lastSeenActionSeq = nvActionSeq.Value;
            _lastSeenAttackSeq = nvAttackSeq.Value;
            _lastSeenJumpSeq = nvJumpSeq.Value;
        }
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            // Throttle: only write every 0.05s instead of every frame
            if (Time.time >= nextNetworkUpdateTime)
            {
                nextNetworkUpdateTime = Time.time + NETWORK_UPDATE_INTERVAL;
                WriteFromOwner();
            }
        }
        else
        {
            // Remotes read and inject.
            ApplyToRemoteHolders();
            ApplyRemoteActionEdge();
            ApplyRemoteAttackEdge();
            ApplyRemoteJumpEdge();
        }
    }

    private void WriteFromOwner()
    {
        if (input == null || tps == null) return;

        // Only update if changed
        if (nvMoving.Value != input.isMoving) nvMoving.Value = input.isMoving;
        if (nvCombatMode.Value != input.isCombatMode) nvCombatMode.Value = input.isCombatMode;
        if (nvModified.Value != input.isModified) nvModified.Value = input.isModified;
        if (nvSecondaryHeld.Value != input.isSecondaryAttack) nvSecondaryHeld.Value = input.isSecondaryAttack;
        if (nvHoverMode.Value != input.isHoverMode) nvHoverMode.Value = input.isHoverMode;
        if (nvSheathing.Value != input.isSheating) nvSheathing.Value = input.isSheating;

        if (nvGrounded.Value != tps.isgrounded) nvGrounded.Value = tps.isgrounded;
        if (nvFreeFall.Value != tps.isfreeFall) nvFreeFall.Value = tps.isfreeFall;

        var dirStr = new FixedString32Bytes(string.IsNullOrEmpty(input.directions) ? "None" : input.directions);
        if (!nvDirections.Value.Equals(dirStr)) nvDirections.Value = dirStr;

        var s = input.Snapshot;
        if (nvPrimaryDown.Value != s.primaryDown) nvPrimaryDown.Value = s.primaryDown;
        if (nvPrimaryHeld.Value != s.primaryHeld) nvPrimaryHeld.Value = s.primaryHeld;
        if (nvPrimaryUp.Value != s.primaryUp) nvPrimaryUp.Value = s.primaryUp;
        if (nvJumpDown.Value != s.jumpDown) nvJumpDown.Value = s.jumpDown;
        if (nvJumpHeld.Value != s.jumpHeld) nvJumpHeld.Value = s.jumpHeld;
        if (nvActionHeld.Value != s.actionHeld) nvActionHeld.Value = s.actionHeld;

        if (s.jumpDown) nvJumpSeq.Value++;
    }

    private void ApplyToRemoteHolders()
    {
        if (input != null)
        {
            input.isMoving = nvMoving.Value;
            input.isCombatMode = nvCombatMode.Value;
            input.isModified = nvModified.Value;
            input.isSecondaryAttack = nvSecondaryHeld.Value;
            input.isHoverMode = nvHoverMode.Value;
            input.isSheating = nvSheathing.Value;

            input.directions = nvDirections.Value.ToString();

            // Inject a snapshot for rules that read ctx.snapshot.*
            if (_snapshotBackingField != null)
            {
                var snap = new InputSnapshot
                {
                    primaryDown = nvPrimaryDown.Value,
                    primaryHeld = nvPrimaryHeld.Value,
                    primaryUp = nvPrimaryUp.Value,
                    jumpDown = nvJumpDown.Value,
                    jumpHeld = nvJumpHeld.Value,
                    actionHeld = nvActionHeld.Value,
                };

                _snapshotBackingField.SetValue(input, snap);
            }
        }

        if (tps != null)
        {
            tps.isgrounded = nvGrounded.Value;
            tps.isfreeFall = nvFreeFall.Value;
        }

        // We don't need to set anything on animDriver directly because it reads from input/tps.
        // But we DO handle the action edge separately (below).
    }

    private void ApplyRemoteActionEdge()
    {
        if (animDriver == null) return;

        // If the owner bumped the sequence, we replay the StartAction edge locally.
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

        // If the owner bumped the attack sequence, replay the attack on remote.
        int seq = nvAttackSeq.Value;
        if (seq == _lastSeenAttackSeq) return;
        _lastSeenAttackSeq = seq;

        var attackState = nvAttackState.Value;

        // Tell the anim driver to play this specific attack
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

        // If the owner bumped the jump sequence, inject a one-frame jumpDown edge.
        int seq = nvJumpSeq.Value;
        if (seq == _lastSeenJumpSeq) return;
        _lastSeenJumpSeq = seq;

        // Force jumpDown to true for this frame
        var snap = new InputSnapshot
        {
            primaryDown = nvPrimaryDown.Value,
            primaryHeld = nvPrimaryHeld.Value,
            primaryUp = nvPrimaryUp.Value,
            jumpDown = true,  // Force this to true when sequence changes
            jumpHeld = nvJumpHeld.Value,
            actionHeld = nvActionHeld.Value,
        };

        _snapshotBackingField.SetValue(input, snap);
    }

    /// <summary>
    /// Call this ON OWNER when you start an interaction (chest/ballista/etc).
    /// This replicates ActionId and causes remotes to trigger the one-frame StartAction edge.
    /// </summary>
    public void OwnerStartAction(int actionId)
    {
        if (!IsOwner) return;

        nvActionId.Value = actionId;
        nvActionSeq.Value++; // edge trigger for others
    }

    /// <summary>
    /// Optional: owner clears the action (if your gameplay wants it).
    /// Your RuleAnimancerDriver also clears internally after lock ends, but this can help.
    /// </summary>
    public void OwnerClearAction()
    {
        if (!IsOwner) return;
        nvActionId.Value = 0;
    }

    /// <summary>
    /// Call this from RuleAnimancerDriver when an attack starts (owner only).
    /// This syncs the attack to remote clients.
    /// </summary>
    public void OwnerStartAttack(string attackKey, RuleAnimancerDriver.AttackMode mode, bool isHeavy, int comboIndex)
    {
        if (!IsOwner) return;

        nvAttackState.Value = new NetworkedAttackState
        {
            attackKey = new FixedString64Bytes(attackKey),
            mode = (byte)mode,
            isHeavy = isHeavy,
            comboIndex = comboIndex
        };
        nvAttackSeq.Value++; // Trigger edge for remotes
    }
}

/// <summary>
/// Networked representation of an attack state.
/// Must be a struct implementing INetworkSerializable.
/// </summary>
public struct NetworkedAttackState : INetworkSerializable
{
    public FixedString64Bytes attackKey;  // e.g., "Sword_Attack1"
    public byte mode;                      // 0=None, 1=Single, 2=Combo
    public bool isHeavy;                   // Light vs heavy combo
    public int comboIndex;                 // Which step in the combo sequence

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref attackKey);
        serializer.SerializeValue(ref mode);
        serializer.SerializeValue(ref isHeavy);
        serializer.SerializeValue(ref comboIndex);
    }
}