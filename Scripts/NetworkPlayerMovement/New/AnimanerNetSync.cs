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

    // Reflection to set InputController.Snapshot (private setter).
    private FieldInfo _snapshotBackingField;

    private int _lastSeenActionSeq;

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
        }
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            // Owner writes. Keep this in Update so it matches your input snapshot timing.
            WriteFromOwner();
        }
        else
        {
            // Remotes read and inject.
            ApplyToRemoteHolders();
            ApplyRemoteActionEdge();
        }
    }

    private void WriteFromOwner()
    {
        if (input == null || tps == null) return;

        // These drive BoolParam checks in your rules.
        nvMoving.Value = input.isMoving;
        nvCombatMode.Value = input.isCombatMode;
        nvModified.Value = input.isModified;
        nvSecondaryHeld.Value = input.isSecondaryAttack;
        nvHoverMode.Value = input.isHoverMode;
        nvSheathing.Value = input.isSheating;

        nvGrounded.Value = tps.isgrounded;
        nvFreeFall.Value = tps.isfreeFall;

        nvDirections.Value = new FixedString32Bytes(string.IsNullOrEmpty(input.directions) ? "None" : input.directions);

        // These drive InputEdge checks + combo buffering in your RuleAnimancerDriver.
        var s = input.Snapshot;
        nvPrimaryDown.Value = s.primaryDown;
        nvPrimaryHeld.Value = s.primaryHeld;
        nvPrimaryUp.Value = s.primaryUp;

        nvJumpDown.Value = s.jumpDown;
        nvJumpHeld.Value = s.jumpHeld;

        nvActionHeld.Value = s.actionHeld;
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

        // We don’t need to set anything on animDriver directly because it reads from input/tps.
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
}
