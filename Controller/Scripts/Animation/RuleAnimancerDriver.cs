using System;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

[DefaultExecutionOrder(50)]
[RequireComponent(typeof(AnimancerComponent))]
public class RuleAnimancerDriver : MonoBehaviour
{
    [Header("Assets")]
    [SerializeField] private AnimationSetBase animationSet;
    [SerializeField] private AnimationRuleSet ruleSet; // still used for your other non-attack rules (locomotion, actions, etc.)

    [Header("Wiring (optional if auto-found)")]
    [SerializeField] private ThirdPersonController tps;
    [SerializeField] private InputController input;
    [SerializeField] private ClientAuthoritativeAnimancerSync networkSync; // NEW: for attack syncing
    [SerializeField] private MountController mountController;

    [Header("Fades")]
    [SerializeField] private float baseFade = 0.12f;
    [SerializeField] private float actionFade = 0.08f;
    [SerializeField] private float attackFade = 0.05f;
    [SerializeField] private float cancelFade = 0.06f;

    [Header("Witcher-Style Attack Settings")]
    [Tooltip("Max time between clicks to consider it a double click.")]
    [SerializeField] private float doubleClickWindow = 0.25f;

    [Tooltip("After the 2nd click, how long must primary be held to start a combo.")]
    [SerializeField] private float comboHoldThreshold = 0.12f;

    [Tooltip("Fallback weapon name if you don't have a weapon system wired yet.")]
    [SerializeField] private string defaultWeaponName = "Sword";

    [Header("Weapon Attack Profiles (data)")]
    [SerializeField] private List<WeaponAttackProfile> weapons = new List<WeaponAttackProfile>();

    private AnimancerComponent _animancer;

    // Layer locks (don't let locomotion override action/attack).
    private readonly Dictionary<AnimLayer, AnimancerState> _lockedState = new Dictionary<AnimLayer, AnimancerState>();
    private readonly Dictionary<AnimLayer, bool> _isLocked = new Dictionary<AnimLayer, bool>();

    // Action runtime (set by external interaction system)
    private int _actionId;
    private bool _actionStartEdge;

    // Attack runtime
    private AttackRuntime _attack = new AttackRuntime();

    // NEW: Flag to prevent remotes from running Witcher input logic
    private bool _isRemoteClient = false;

    public AnimancerComponent Animancer => _animancer;
    public bool IsLocked => IsLayerLocked(AnimLayer.Attack) || IsLayerLocked(AnimLayer.Action);

    // Make AttackMode public so sync script can use it
    public enum AttackMode
    {
        None = 0,
        Single = 1,
        Combo = 2
    }

    private void Awake()
    {
        _animancer = GetComponent<AnimancerComponent>();
        if (tps == null) tps = GetComponentInParent<ThirdPersonController>();
        if (input == null) input = GetComponentInParent<InputController>();
        if (networkSync == null) networkSync = GetComponentInParent<ClientAuthoritativeAnimancerSync>();
        if (mountController == null) mountController = GetComponentInParent<MountController>();
        // Check if we're a remote client
        var netObj = GetComponentInParent<Unity.Netcode.NetworkBehaviour>();
        if (netObj != null)
        {
            _isRemoteClient = netObj.IsSpawned && !netObj.IsOwner;
        }
    }

    /// <summary>
    /// Call this from your interaction code when you start an action like chest/ballista.
    /// Example: driver.StartAction(1) // chest
    /// </summary>
    public void StartAction(int actionId)
    {
        _actionId = actionId;
        _actionStartEdge = true; // one-frame edge
    }

    private void LateUpdate()
    {
        if (_animancer == null || animationSet == null || ruleSet == null || tps == null || input == null)
            return;

        // Build context
        var ctx = new AnimationContext
        {
            tps = tps,
            input = input,
            snapshot = input.Snapshot,
            mountController = mountController,
            ActionId = _actionId,
            ActionStart = _actionStartEdge,
        };
        _actionStartEdge = false;

        // 0) If we're mid-combo, cancel immediately if required inputs released.
        if (_attack.mode == AttackMode.Combo)
        {
            bool primaryHeld = ctx.snapshot.primaryHeld;
            bool shiftHeld = ctx.Modified;

            if (!primaryHeld || (_attack.comboIsHeavy && !shiftHeld))
            {
                CancelCurrentAttack();
                // After cancelling, allow other rules to run this frame.
            }
        }

        // 1) If attack is locked/playing, IGNORE ALL attack inputs (no queueing, no chaining).
        //    (Combo sequencing happens via OnEnd only.)
        if (IsLayerLocked(AnimLayer.Attack))
            return;

        // 2) If action is locked, do nothing else.
        if (IsLayerLocked(AnimLayer.Action))
            return;

        // 3) Witcher-style attack input handler (random single OR doubleclick+hold combo).
        // ONLY RUN ON OWNER - remotes will receive attacks via PlayNetworkedAttack()
        if (!_isRemoteClient && HandleWitcherAttacks(ctx))
            return;

        // 4) Otherwise run your normal rules: Attack > Action > Base
        if (TryPlayBestRule(ctx, AnimLayer.Attack)) return;
        if (TryPlayBestRule(ctx, AnimLayer.Action)) return;
        TryPlayBestRule(ctx, AnimLayer.Base);

        if (_actionId != 0 && !IsLayerLocked(AnimLayer.Action))
            _actionId = 0;
    }

    // =========================
    // Witcher-style attack logic
    // =========================

    [Serializable]
    private class WeaponAttackProfile
    {
        public string weaponName = "Sword";

        [Header("Random Singles (each click triggers one random)")]
        public List<string> singleAttackKeys = new List<string>();

        [Header("Combo Sequences (double click + hold triggers)")]
        public List<string> lightComboKeys = new List<string>();
        public List<string> heavyComboKeys = new List<string>(); // Shift-modified
    }

    private class AttackRuntime
    {
        public AttackMode mode = AttackMode.None;

        // Pending single-attack to allow detecting double-click+hold without firing immediately.
        public bool pendingSingle;
        public float pendingStartTime;
        public bool sawSecondClick;
        public float secondClickTime;

        // Combo state
        public bool comboIsHeavy;
        public string weaponName;
        public List<string> comboKeys;
        public int comboIndex;

        public void ResetGesture()
        {
            pendingSingle = false;
            pendingStartTime = 0;
            sawSecondClick = false;
            secondClickTime = 0;
        }

        public void ResetAll()
        {
            mode = AttackMode.None;
            comboIsHeavy = false;
            weaponName = null;
            comboKeys = null;
            comboIndex = 0;
            ResetGesture();
        }
    }

    /// <summary>
    /// Returns true if we consumed the frame by starting an attack/combo (or waiting to decide).
    /// </summary>
    private bool HandleWitcherAttacks(AnimationContext ctx)
    {
        // Only care about primary for this simplified model.
        bool down = ctx.snapshot.primaryDown;
        bool held = ctx.snapshot.primaryHeld;
        bool up = ctx.snapshot.primaryUp;

        // No input? maybe we are waiting on a pending single to expire.
        if (!down && !held && !up)
        {
            // If we have a pending single and the window expired => fire it now.
            if (_attack.pendingSingle && Time.time - _attack.pendingStartTime >= doubleClickWindow)
            {
                _attack.ResetGesture();
                return StartRandomSingle(ctx);
            }
            return false;
        }

        // If we started pending and player released without second click, allow window expiry to fire single.
        // If player releases after second click but didn't hold enough, we'll fire a single on release.
        if (_attack.pendingSingle)
        {
            // Second click?
            if (down && !_attack.sawSecondClick && Time.time - _attack.pendingStartTime <= doubleClickWindow)
            {
                _attack.sawSecondClick = true;
                _attack.secondClickTime = Time.time;
                return true; // consume; now we wait for hold threshold to decide combo
            }

            // If we saw second click, check hold threshold to start combo
            if (_attack.sawSecondClick)
            {
                // If held long enough -> start combo
                if (held && Time.time - _attack.secondClickTime >= comboHoldThreshold)
                {
                    _attack.ResetGesture();
                    return StartCombo(ctx);
                }

                // If player released before hold threshold -> treat as single (no combo)
                if (up && Time.time - _attack.secondClickTime < comboHoldThreshold)
                {
                    _attack.ResetGesture();
                    return StartRandomSingle(ctx);
                }

                // Otherwise keep waiting
                return true;
            }

            // If window expired with no second click -> fire single immediately
            if (Time.time - _attack.pendingStartTime >= doubleClickWindow)
            {
                _attack.ResetGesture();
                return StartRandomSingle(ctx);
            }

            // Still waiting for possible second click
            return true;
        }

        // Not pending and attack not locked:
        // On first click DOWN, start pending window so we can detect double-click+hold.
        if (down)
        {
            _attack.pendingSingle = true;
            _attack.pendingStartTime = Time.time;
            _attack.sawSecondClick = false;
            _attack.secondClickTime = 0;
            return true; // consume this click while we decide
        }

        // If player is holding without a prior down we care about, ignore.
        return false;
    }

    private string GetEquippedWeaponName(AnimationContext ctx)
    {
        // Plug your real weapon system here later.
        // For now, fall back to serialized default.
        return string.IsNullOrWhiteSpace(defaultWeaponName) ? "Sword" : defaultWeaponName;
    }

    private WeaponAttackProfile GetWeaponProfile(string weaponName)
    {
        for (int i = 0; i < weapons.Count; i++)
        {
            if (weapons[i] != null && string.Equals(weapons[i].weaponName, weaponName, StringComparison.Ordinal))
                return weapons[i];
        }
        return null;
    }

    private bool StartRandomSingle(AnimationContext ctx)
    {
        string weapon = GetEquippedWeaponName(ctx);
        var profile = GetWeaponProfile(weapon);

        if (profile == null || profile.singleAttackKeys == null || profile.singleAttackKeys.Count == 0)
        {
            Debug.LogWarning($"[Attack] No singleAttackKeys configured for weapon '{weapon}'.");
            return false;
        }

        int r = UnityEngine.Random.Range(0, profile.singleAttackKeys.Count);
        string key = profile.singleAttackKeys[r];

        if (!TryPlayAttackKey(key, AttackMode.Single, false, 0))
            return false;

        return true;
    }

    private bool StartCombo(AnimationContext ctx)
    {
        string weapon = GetEquippedWeaponName(ctx);
        var profile = GetWeaponProfile(weapon);

        if (profile == null)
        {
            Debug.LogWarning($"[Combo] No weapon profile found for '{weapon}'.");
            return false;
        }

        bool heavy = ctx.Modified; // LeftShift modifier at combo start
        var keys = heavy ? profile.heavyComboKeys : profile.lightComboKeys;

        if (keys == null || keys.Count == 0)
        {
            Debug.LogWarning($"[Combo] No {(heavy ? "heavy" : "light")} combo keys configured for weapon '{weapon}'.");
            return false;
        }

        _attack.mode = AttackMode.Combo;
        _attack.comboIsHeavy = heavy;
        _attack.weaponName = weapon;
        _attack.comboKeys = keys;
        _attack.comboIndex = 0;

        return PlayComboIndex(0);
    }

    private bool PlayComboIndex(int index)
    {
        if (_attack.comboKeys == null || index < 0 || index >= _attack.comboKeys.Count)
        {
            _attack.ResetAll();
            return false;
        }

        _attack.comboIndex = index;
        string key = _attack.comboKeys[index];

        if (!TryPlayAttackKey(key, AttackMode.Combo, _attack.comboIsHeavy, index))
        {
            _attack.ResetAll();
            return false;
        }

        return true;
    }

    private bool TryPlayAttackKey(string key, AttackMode mode, bool isHeavy, int comboIndex)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!animationSet.TryGet(key, out var transition) || transition == null || transition.Clip == null)
        {
            Debug.LogError($"[Attack] Missing key in AnimationSet: '{key}'");
            return false;
        }

        var state = _animancer.Play(transition, attackFade);
        state.Time = 0;

        _attack.mode = mode;

        // Lock attack layer until end.
        LockAttackUntilEnd(state);

        // NEW: Notify network sync (owner only)
        if (networkSync != null && !_isRemoteClient)
        {
            networkSync.OwnerStartAttack(key, mode, isHeavy, comboIndex);
        }

        return true;
    }

    /// <summary>
    /// NEW: Called by ClientAuthoritativeAnimancerSync on remote clients to play the attack.
    /// This bypasses all the Witcher input logic and directly plays the attack.
    /// </summary>
    public void PlayNetworkedAttack(string attackKey, AttackMode mode, bool isHeavy, int comboIndex)
    {
        if (string.IsNullOrWhiteSpace(attackKey))
            return;

        if (!animationSet.TryGet(attackKey, out var transition) || transition == null || transition.Clip == null)
        {
            Debug.LogError($"[PlayNetworkedAttack] Missing key in AnimationSet: '{attackKey}'");
            return;
        }

        // Set up the attack runtime state so combo advancement works correctly
        if (mode == AttackMode.Combo)
        {
            // Find the weapon profile that contains this attack key
            WeaponAttackProfile profile = null;
            bool foundInHeavy = false;

            foreach (var weapon in weapons)
            {
                if (weapon.lightComboKeys != null && weapon.lightComboKeys.Contains(attackKey))
                {
                    profile = weapon;
                    foundInHeavy = false;
                    break;
                }
                if (weapon.heavyComboKeys != null && weapon.heavyComboKeys.Contains(attackKey))
                {
                    profile = weapon;
                    foundInHeavy = true;
                    break;
                }
            }

            if (profile != null)
            {
                _attack.mode = AttackMode.Combo;
                _attack.comboIsHeavy = foundInHeavy;
                _attack.weaponName = profile.weaponName;
                _attack.comboKeys = foundInHeavy ? profile.heavyComboKeys : profile.lightComboKeys;
                _attack.comboIndex = comboIndex;
            }
            else
            {
                Debug.LogWarning($"[PlayNetworkedAttack] Could not find weapon profile for combo attack '{attackKey}'");
                _attack.mode = AttackMode.Single; // Fallback to single
            }
        }
        else
        {
            _attack.mode = mode;
        }

        // Play the animation
        var state = _animancer.Play(transition, attackFade);
        state.Time = 0;

        // Lock attack layer until end
        LockAttackUntilEnd(state);
    }

    private void LockAttackUntilEnd(AnimancerState state)
    {
        _isLocked[AnimLayer.Attack] = true;
        _lockedState[AnimLayer.Attack] = state;

        state.Events.OnEnd = () =>
        {
            _isLocked[AnimLayer.Attack] = false;
            _lockedState[AnimLayer.Attack] = null;

            // Single attacks: nothing else to do.
            if (_attack.mode == AttackMode.Single)
            {
                _attack.mode = AttackMode.None;
                return;
            }

            // Combo attacks: advance automatically IF required inputs are still held.
            if (_attack.mode == AttackMode.Combo)
            {
                // Primary must still be held; and for heavy combos shift must still be held.
                bool primaryHeld = input.Snapshot.primaryHeld;
                bool shiftHeld = input.isModified; // you already use this for ctx.Modified

                if (!primaryHeld || (_attack.comboIsHeavy && !shiftHeld))
                {
                    _attack.ResetAll();
                    return;
                }

                int next = _attack.comboIndex + 1;
                if (_attack.comboKeys != null && next < _attack.comboKeys.Count)
                {
                    PlayComboIndex(next);
                    return;
                }

                // Combo finished
                _attack.ResetAll();
            }
        };
    }

    private void CancelCurrentAttack()
    {
        // Fade out current state (if any) and unlock.
        if (_lockedState.TryGetValue(AnimLayer.Attack, out var st) && st != null)
        {
            try { st.Stop(); } catch { /* ignore */ }
        }

        _isLocked[AnimLayer.Attack] = false;
        _lockedState[AnimLayer.Attack] = null;
        _attack.ResetAll();
    }

    // =========================
    // Your existing rule system
    // =========================

    private bool TryPlayBestRule(AnimationContext ctx, AnimLayer layer)
    {
        AnimationRule best = null;
        int bestPriority = int.MinValue;

        var rules = ruleSet.rules;
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r == null) continue;
            if (r.layer != layer) continue;

            if (!MatchesAll(ctx, r)) continue;

            if (r.priority > bestPriority)
            {
                bestPriority = r.priority;
                best = r;
            }
        }

        if (best == null) return false;

        if (!animationSet.TryGet(best.animationKey, out var transition) || transition == null || transition.Clip == null)
            return false;

        if (IsLayerLocked(layer)) return true;
        if (IsPlayingClip(transition.Clip)) return true;

        var fade = layer == AnimLayer.Attack ? attackFade : layer == AnimLayer.Action ? actionFade : baseFade;
        var state = _animancer.Play(transition, fade);

        if (best.lockUntilEnd)
            LockLayerUntilEnd(layer, state);

        return true;
    }

    private bool MatchesAll(AnimationContext ctx, AnimationRule rule)
    {
        var list = rule.all;
        for (int i = 0; i < list.Count; i++)
        {
            if (!ConditionMatches(ctx, list[i]))
                return false;
        }
        return true;
    }

    private bool ConditionMatches(AnimationContext ctx, RuleCondition c)
    {
        if (c.useBool)
        {
            bool val = c.boolParam switch
            {
                BoolParam.Moving => ctx.Moving,
                BoolParam.CombatMode => ctx.CombatMode,
                BoolParam.Modified => ctx.Modified,
                BoolParam.SecondaryHeld => ctx.SecondaryHeld,
                BoolParam.HoverMode => ctx.HoverMode,
                BoolParam.Grounded => ctx.Grounded,
                BoolParam.FreeFall => ctx.FreeFall,
                BoolParam.Sheathing => ctx.Sheathing,
                BoolParam.IsMounted => ctx.IsMounted,
                BoolParam.IsTransitioning => ctx.IsTransitioning,
                _ => false
            };

            if (val != c.boolValue) return false;
        }

        if (c.useInput)
        {
            bool ok = c.input switch
            {
                InputEdge.PrimaryDown => TriggerOk(ctx, c.triggerMode, ctx.snapshot.primaryDown, ctx.snapshot.primaryHeld, ctx.snapshot.primaryUp),
                InputEdge.JumpDown => TriggerOk(ctx, c.triggerMode, ctx.snapshot.jumpDown, ctx.snapshot.jumpHeld, false),
                InputEdge.ActionHeld => TriggerOk(ctx, c.triggerMode, ctx.snapshot.actionHeld, ctx.snapshot.actionHeld, false),
                _ => false
            };

            if (!ok) return false;
        }

        if (c.useDirection)
        {
            if (c.direction == Direction4.Any) return true;

            var d = ctx.DirString;
            if (c.direction == Direction4.W && d != "W") return false;
            if (c.direction == Direction4.A && d != "A") return false;
            if (c.direction == Direction4.S && d != "S") return false;
            if (c.direction == Direction4.D && d != "D") return false;
        }

        if (c.useActionId)
        {
            if (ctx.ActionId != c.actionIdEquals) return false;
        }

        return true;
    }

    private bool TriggerOk(AnimationContext ctx, TriggerMode mode, bool down, bool held, bool up)
    {
        return mode switch
        {
            TriggerMode.None => true,
            TriggerMode.Down => down,
            TriggerMode.Held => held,
            TriggerMode.Up => up,
            _ => false
        };
    }

    private bool IsPlayingClip(AnimationClip clip)
    {
        var current = _animancer.States.Current;
        return current != null && current.Clip == clip;
    }

    private void LockLayerUntilEnd(AnimLayer layer, AnimancerState state)
    {
        _isLocked[layer] = true;
        _lockedState[layer] = state;

        state.Events.OnEnd = () =>
        {
            _isLocked[layer] = false;
            _lockedState[layer] = null;

            if (layer == AnimLayer.Action)
            {
                input.isSheating = false;
            }
        };
    }

    private bool IsLayerLocked(AnimLayer layer)
    {
        if (!_isLocked.TryGetValue(layer, out var locked) || !locked)
            return false;

        if (_lockedState.TryGetValue(layer, out var state) && state != null)
        {
            if (state.IsPlaying)
                return true;
        }

        _isLocked[layer] = false;
        _lockedState[layer] = null;
        return false;
    }
}