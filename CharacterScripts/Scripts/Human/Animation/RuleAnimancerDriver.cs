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
    [SerializeField] private AnimationRuleSet ruleSet;

    [Header("Wiring (optional if auto-found)")]
    [SerializeField] private ThirdPersonController tps;
    [SerializeField] private InputController input;
    [SerializeField] private ClientAuthoritativeAnimancerSync networkSync;
    [SerializeField] private MountController mountController;

    [Header("Fades")]
    [SerializeField] private float baseFade = 0.12f;
    [SerializeField] private float actionFade = 0.08f;
    [SerializeField] private float attackFade = 0.05f;
    [SerializeField] private float cancelFade = 0.06f;

    [Header("Hitbox Integration")]
    [SerializeField] private HitboxController activeHitbox;

    [Header("Layer Masks")]
    [Tooltip("Upper body mask for Action layer (unsheathe, interactions). " +
             "Create via Assets > Create > Avatar Mask, enable spine/arms/head only.")]
    [SerializeField] private AvatarMask actionLayerMask;

    [Tooltip("Optional mask for Attack layer. Leave null for full body attacks.")]
    [SerializeField] private AvatarMask attackLayerMask;

    [Tooltip("Fade duration when a masked layer finishes and fades out.")]
    [SerializeField] private float layerFadeOutDuration = 0.15f;

    [Header("Root Motion")]
    [Tooltip("When enabled, root motion from the current Base rule (if flagged) " +
             "will drive the CharacterController via OnAnimatorMove.")]
    [SerializeField] private bool allowRootMotion = true;

    [Header("Combat Integration")]
    [SerializeField] private CombatController combatController;
    [SerializeField] private WeaponManager weaponManager;

    [Header("Witcher-Style Attack Settings")]
    [SerializeField] private float doubleClickWindow = 0.25f;
    [SerializeField] private float comboHoldThreshold = 0.12f;
    [SerializeField] private string defaultWeaponName = "Sword";

    [Header("Weapon Attack Profiles (data)")]
    [SerializeField] private List<WeaponAttackProfile> weapons = new List<WeaponAttackProfile>();

    // ───────────────────── Runtime ─────────────────────

    private AnimancerComponent _animancer;
    private Animator _animator;
    private CharacterController _charController;

    // Animancer layers (cached on Awake)
    private AnimancerLayer _baseLayer;
    private AnimancerLayer _actionLayer;
    private AnimancerLayer _attackLayer;

    // Layer locks
    private readonly Dictionary<AnimLayer, AnimancerState> _lockedState = new();
    private readonly Dictionary<AnimLayer, bool> _isLocked = new();

    // Action runtime
    private int _actionId;
    private bool _actionStartEdge;

    // Attack runtime
    private AttackRuntime _attack = new();

    // Root motion
    private bool _rootMotionActive = false;

    // Network
    private bool _isRemoteClient = false;

    // ───────────────────── Public API ─────────────────────

    public AnimancerComponent Animancer => _animancer;
    public bool IsLocked => IsLayerLocked(AnimLayer.Attack) || IsLayerLocked(AnimLayer.Action);

    /// <summary>
    /// True when root motion is currently driving movement.
    /// Your ThirdPersonController should check this and skip its own movement when true.
    /// </summary>
    public bool RootMotionActive => _rootMotionActive;
    public void SetActiveHitbox(HitboxController hitbox)
    {
        activeHitbox = hitbox;
    }
    public enum AttackMode
    {
        None = 0,
        Single = 1,
        Combo = 2
    }

    // ───────────────────── Init ─────────────────────

    private void Awake()
    {
        _animancer = GetComponent<AnimancerComponent>();
        _animator = GetComponent<Animator>();
        _charController = GetComponentInParent<CharacterController>();

        if (tps == null) tps = GetComponentInParent<ThirdPersonController>();
        if (input == null) input = GetComponentInParent<InputController>();
        if (networkSync == null) networkSync = GetComponentInParent<ClientAuthoritativeAnimancerSync>();
        if (mountController == null) mountController = GetComponentInParent<MountController>();
        if (combatController == null)combatController = GetComponentInParent<CombatController>();
        if (weaponManager == null) weaponManager = GetComponentInParent<WeaponManager>();
        var netObj = GetComponentInParent<Unity.Netcode.NetworkBehaviour>();
        if (netObj != null)
            _isRemoteClient = netObj.IsSpawned && !netObj.IsOwner;

        SetupLayers();
    }

    /// <summary>
    /// Creates Animancer layers and applies AvatarMasks.
    /// Call once in Awake. Layers are created on first access to _animancer.Layers[n].
    /// </summary>
    private void SetupLayers()
    {
        _baseLayer = _animancer.Layers[0];
        _baseLayer.SetDebugName("Base");

        _actionLayer = _animancer.Layers[1];
        _actionLayer.SetDebugName("Action");
        if (actionLayerMask != null)
            _actionLayer.SetMask(actionLayerMask);

        _attackLayer = _animancer.Layers[2];
        _attackLayer.SetDebugName("Attack");
        if (attackLayerMask != null)
            _attackLayer.SetMask(attackLayerMask);
    }

    /// <summary>
    /// Maps our AnimLayer enum to the corresponding Animancer layer.
    /// </summary>
    private AnimancerLayer GetAnimancerLayer(AnimLayer layer)
    {
        return layer switch
        {
            AnimLayer.Base => _baseLayer,
            AnimLayer.Action => _actionLayer,
            AnimLayer.Attack => _attackLayer,
            _ => _baseLayer,
        };
    }

    // ───────────────────── Root Motion (CharacterController) ─────────────────────

    /// <summary>
    /// Called by Unity every frame the Animator updates.
    /// We intercept root motion delta and apply it to CharacterController.
    /// </summary>
    private void OnAnimatorMove()
    {
        if (!allowRootMotion || !_rootMotionActive || _animator == null)
            return;

        if (_charController != null && _charController.enabled)
        {
            // Apply root motion position delta
            Vector3 delta = _animator.deltaPosition;

            // Optionally add gravity if not grounded
            if (tps != null && !tps.isgrounded)
                delta.y += Physics.gravity.y * Time.deltaTime;

            _charController.Move(delta);

            // Apply root motion rotation
            transform.rotation *= _animator.deltaRotation;
        }
    }

    // ───────────────────── Action entry point ─────────────────────

    public void StartAction(int actionId)
    {
        _actionId = actionId;
        _actionStartEdge = true;
    }

    // ───────────────────── Main Loop ─────────────────────

    private void LateUpdate()
    {

        if (_animancer == null || animationSet == null || ruleSet == null || tps == null)
            return;

        if (input == null && mountController == null)
            return;

        var ctx = new AnimationContext
        {
            tps = tps,
            input = input,
            snapshot = input != null ? input.Snapshot : default,
            mountController = mountController,
            combatController = combatController,    // NEW
            weaponManager = weaponManager,           // NEW
            ActionId = _actionId,
            ActionStart = _actionStartEdge,
        };
        _actionStartEdge = false;

        // 0) If mid-combo, cancel if required inputs released
        //if (_attack.mode == AttackMode.Combo)
        //{
        //    bool primaryHeld = ctx.snapshot.primaryHeld;
        //    bool shiftHeld = ctx.Modified;

        //    if (!primaryHeld || (_attack.comboIsHeavy && !shiftHeld))
        //        CancelCurrentAttack();
        //}

        // 1) Attack locked → skip everything (full body, frame-critical)
        if (IsLayerLocked(AnimLayer.Attack))
            return;
        bool combatBusy = combatController != null &&
            (combatController.IsDodging || combatController.IsBlocking ||
             combatController.IsBowDrawing || combatController.IsBowAiming);
        // 2) Witcher-style attacks (owner only)
        if (!_isRemoteClient && HandleWitcherAttacks(ctx))
            return;

        // 3) Rule evaluation: Attack > Action > Base
        //    Action lock only prevents new Action rules, NOT Base layer updates.
        //    This allows locomotion to keep running under a masked Action (e.g. unsheathe over combat walk).
        if (TryPlayBestRule(ctx, AnimLayer.Attack)) return;
        if (!IsLayerLocked(AnimLayer.Action))
            TryPlayBestRule(ctx, AnimLayer.Action);
        TryPlayBestRule(ctx, AnimLayer.Base);

        if (_actionId != 0 && !IsLayerLocked(AnimLayer.Action))
            _actionId = 0;
    }

    // ═════════════════════════════════════════════════════
    //  RULE EVALUATION (now layer-aware)
    // ═════════════════════════════════════════════════════

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

        // Check if this clip is already playing on the correct layer
        var animLayer = GetAnimancerLayer(layer);
        if (IsPlayingClipOnLayer(animLayer, transition.Clip)) return true;

        // Apply per-rule mask override if specified
        if (best.maskOverride != null)
            animLayer.SetMask(best.maskOverride);

        var fade = layer switch
        {
            AnimLayer.Attack => attackFade,
            AnimLayer.Action => actionFade,
            _ => baseFade,
        };

        // Play on the correct Animancer layer
        var state = animLayer.Play(transition, fade);

        // Handle root motion from clip-level flag
        {
            bool wantRoot = allowRootMotion && animationSet.IsRootMotion(best.animationKey);
            _rootMotionActive = wantRoot;
            if (_animator != null)
                _animator.applyRootMotion = wantRoot;
        }

        if (best.lockUntilEnd)
            LockLayerUntilEnd(layer, state);

        return true;
    }

    // ═════════════════════════════════════════════════════
    //  WITCHER-STYLE ATTACK LOGIC (unchanged logic, layer-aware playback)
    // ═════════════════════════════════════════════════════

    [Serializable]
    private class WeaponAttackProfile
    {
        public string weaponName = "Sword";

        [Header("Random Singles (each click triggers one random)")]
        public List<string> singleAttackKeys = new();

        [Header("Combo Sequences (double click + hold triggers)")]
        public List<string> lightComboKeys = new();
        public List<string> heavyComboKeys = new();
    }

    private class AttackRuntime
    {
        public AttackMode mode = AttackMode.None;

        // Combo tracking
        public bool comboIsHeavy;
        public string weaponName;
        public List<string> comboKeys;
        public int comboIndex;

        // Click buffer: if player clicks during an attack, queue the next one
        public bool bufferedClick;
        public bool bufferedHeavy;

        public void ResetAll()
        {
            mode = AttackMode.None;
            comboIsHeavy = false;
            weaponName = null;
            comboKeys = null;
            comboIndex = 0;
            bufferedClick = false;
            bufferedHeavy = false;
        }
    }

    private bool HandleWitcherAttacks(AnimationContext ctx)
    {
        bool down = ctx.snapshot.primaryDown;

        // Stamina check
        if (combatController != null && !combatController.CanAttack())
            return false;

        // If currently attacking, buffer the click for combo continuation
        if (IsLayerLocked(AnimLayer.Attack))
        {
            if (down)
            {
                _attack.bufferedClick = true;
                _attack.bufferedHeavy = ctx.Modified;
            }
            return false; // Let the lock check in LateUpdate handle it
        }

        // Not attacking — start a new attack on click
        if (down)
        {
            bool heavy = ctx.Modified;
            SnapRotationToCamera(); // Face camera direction on each attack
            return StartSequentialAttack(ctx, heavy);
        }

        return false;
    }

    // ─── New: Start attack using combo sequence (not random) ───

    private bool StartSequentialAttack(AnimationContext ctx, bool heavy)
    {
        string weapon = GetEquippedWeaponName(ctx);
        var profile = GetWeaponProfile(weapon);

        if (profile == null)
        {
            Debug.LogWarning($"[Attack] No weapon profile found for '{weapon}'.");
            return false;
        }

        var keys = heavy ? profile.heavyComboKeys : profile.lightComboKeys;

        // Fallback to single attacks if no combo keys
        if (keys == null || keys.Count == 0)
        {
            if (profile.singleAttackKeys != null && profile.singleAttackKeys.Count > 0)
            {
                int r = UnityEngine.Random.Range(0, profile.singleAttackKeys.Count);
                string singleKey = profile.singleAttackKeys[r];
                if (!TryPlayAttackKey(singleKey, AttackMode.Single, false, 0))
                    return false;
                combatController?.ConsumeAttackStamina(false);
                return true;
            }
            return false;
        }

        // Start or continue combo
        _attack.mode = AttackMode.Combo;
        _attack.comboIsHeavy = heavy;
        _attack.weaponName = weapon;
        _attack.comboKeys = keys;
        _attack.bufferedClick = false;
        _attack.bufferedHeavy = false;

        // If already in a combo for this weapon, advance to next step
        // Otherwise start from 0
        if (_attack.comboIndex > 0 && string.Equals(_attack.weaponName, weapon))
        {
            // Continue combo from where we left off (handled by OnEnd callback)
        }
        else
        {
            _attack.comboIndex = 0;
        }

        combatController?.ConsumeAttackStamina(heavy);
        return PlayComboIndex(_attack.comboIndex);
    }

    // ─── New: Snap player rotation to camera direction ───

    private void SnapRotationToCamera()
    {
        if (_isRemoteClient) return;

        // Get camera reference
        Transform cam = null;
        if (tps != null)
        {
            // Try to get camera from ThirdPersonController or main camera
            cam = Camera.main?.transform;
        }

        if (cam == null) return;

        // Snap player to face camera's forward direction (Y axis only)
        float cameraYaw = cam.eulerAngles.y;
        Transform root = transform.parent != null ? transform.parent : transform;
        root.rotation = Quaternion.Euler(0f, cameraYaw, 0f);
    }

    private string GetEquippedWeaponName(AnimationContext ctx)
    {
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

    //private bool StartRandomSingle(AnimationContext ctx)
    //{
    //    string weapon = GetEquippedWeaponName(ctx);
    //    var profile = GetWeaponProfile(weapon);

    //    if (profile == null || profile.singleAttackKeys == null || profile.singleAttackKeys.Count == 0)
    //    {
    //        Debug.LogWarning($"[Attack] No singleAttackKeys configured for weapon '{weapon}'.");
    //        return false;
    //    }

    //    int r = UnityEngine.Random.Range(0, profile.singleAttackKeys.Count);
    //    string key = profile.singleAttackKeys[r];

    //    if (!TryPlayAttackKey(key, AttackMode.Single, false, 0))
    //        return false;
    //    combatController?.ConsumeAttackStamina(false);
    //    return true;
    //}

    //private bool StartCombo(AnimationContext ctx)
    //{
    //    string weapon = GetEquippedWeaponName(ctx);
    //    var profile = GetWeaponProfile(weapon);

    //    if (profile == null)
    //    {
    //        Debug.LogWarning($"[Combo] No weapon profile found for '{weapon}'.");
    //        return false;
    //    }

    //    bool heavy = ctx.Modified;
    //    var keys = heavy ? profile.heavyComboKeys : profile.lightComboKeys;

    //    if (keys == null || keys.Count == 0)
    //    {
    //        Debug.LogWarning($"[Combo] No {(heavy ? "heavy" : "light")} combo keys configured for weapon '{weapon}'.");
    //        return false;
    //    }

    //    _attack.mode = AttackMode.Combo;
    //    _attack.comboIsHeavy = heavy;
    //    _attack.weaponName = weapon;
    //    _attack.comboKeys = keys;
    //    _attack.comboIndex = 0;

    //    combatController?.ConsumeAttackStamina(heavy);
    //    return PlayComboIndex(0);
    //}

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

        // ► Play on the Attack Animancer layer
        var state = _attackLayer.Play(transition, attackFade);
        state.Time = 0;

        _attack.mode = mode;

        // Root motion from clip-level flag
        bool wantRoot = allowRootMotion && animationSet.IsRootMotion(key);
        _rootMotionActive = wantRoot;
        if (_animator != null)
            _animator.applyRootMotion = wantRoot;

        if (activeHitbox != null)
            activeHitbox.EnableHitbox();

        LockAttackUntilEnd(state);

        // Network sync (owner only)
        if (networkSync != null && !_isRemoteClient)
            networkSync.OwnerStartAttack(key, mode, isHeavy, comboIndex);

        return true;
    }

    /// <summary>
    /// Called by ClientAuthoritativeAnimancerSync on remote clients.
    /// Bypasses Witcher input logic and directly plays the attack on the Attack layer.
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

        if (mode == AttackMode.Combo)
        {
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
                _attack.mode = AttackMode.Single;
            }
        }
        else
        {
            _attack.mode = mode;
        }

        // ► Play on the Attack Animancer layer
        var state = _attackLayer.Play(transition, attackFade);
        state.Time = 0;

        // Root motion from clip-level flag
        bool wantRoot = allowRootMotion && animationSet.IsRootMotion(attackKey);
        _rootMotionActive = wantRoot;
        if (_animator != null)
            _animator.applyRootMotion = wantRoot;

        LockAttackUntilEnd(state);
    }

    // ═════════════════════════════════════════════════════
    //  LAYER LOCKING
    // ═════════════════════════════════════════════════════

    private void LockAttackUntilEnd(AnimancerState state)
    {
        _isLocked[AnimLayer.Attack] = true;
        _lockedState[AnimLayer.Attack] = state;

        state.Events.OnEnd = () =>
        {
            if (activeHitbox != null)
                activeHitbox.DisableHitbox();

            _isLocked[AnimLayer.Attack] = false;
            _lockedState[AnimLayer.Attack] = null;

            // Fade out attack layer so Base takes over
            _attackLayer.StartFade(0, layerFadeOutDuration);

            // Single attack — just end
            if (_attack.mode == AttackMode.Single)
            {
                _attack.ResetAll();
                DisableRootMotion();
                return;
            }

            // Combo — check if player buffered a click during the attack
            if (_attack.mode == AttackMode.Combo)
            {
                if (_attack.bufferedClick)
                {
                    _attack.bufferedClick = false;

                    // If switching between heavy/light mid-combo, restart
                    if (_attack.bufferedHeavy != _attack.comboIsHeavy)
                    {
                        string weapon = _attack.weaponName;
                        var profile = GetWeaponProfile(weapon);
                        if (profile != null)
                        {
                            var newKeys = _attack.bufferedHeavy ? profile.heavyComboKeys : profile.lightComboKeys;
                            if (newKeys != null && newKeys.Count > 0)
                            {
                                _attack.comboIsHeavy = _attack.bufferedHeavy;
                                _attack.comboKeys = newKeys;
                                _attack.comboIndex = 0;
                            }
                        }
                    }

                    int next = _attack.comboIndex + 1;

                    // Loop back to start if we've reached the end of combo
                    if (_attack.comboKeys == null || next >= _attack.comboKeys.Count)
                        next = 0;

                    // Snap rotation again for the next attack
                    SnapRotationToCamera();

                    // Consume stamina for next attack
                    combatController?.ConsumeAttackStamina(_attack.comboIsHeavy);

                    PlayComboIndex(next);
                    return;
                }

                // No buffered click — combo ends
                _attack.ResetAll();
                DisableRootMotion();
            }
        };
    }

    private void CancelCurrentAttack()
    {
        if (activeHitbox != null)
            activeHitbox.DisableHitbox();
        if (_lockedState.TryGetValue(AnimLayer.Attack, out var st) && st != null)
        {
            try { st.Stop(); } catch { /* ignore */ }
        }

        _isLocked[AnimLayer.Attack] = false;
        _lockedState[AnimLayer.Attack] = null;
        _attack.ResetAll();

        // Fade out attack layer
        _attackLayer.StartFade(0, cancelFade);

        DisableRootMotion();
    }

    /// <summary>
    /// Turns off root motion. Called when attacks/actions end.
    /// The next Base layer rule evaluation will re-enable it if needed.
    /// </summary>
    private void DisableRootMotion()
    {
        _rootMotionActive = false;
        if (_animator != null)
            _animator.applyRootMotion = false;
    }

    private void LockLayerUntilEnd(AnimLayer layer, AnimancerState state)
    {
        _isLocked[layer] = true;
        _lockedState[layer] = state;

        state.Events.OnEnd = () =>
        {
            _isLocked[layer] = false;
            _lockedState[layer] = null;

            // Fade out the masked layer when action/attack finishes
            if (layer != AnimLayer.Base)
            {
                var animLayer = GetAnimancerLayer(layer);
                animLayer.StartFade(0, layerFadeOutDuration);
            }

            if (layer == AnimLayer.Action)
            {
                if (input != null)
                    input.isSheating = false;
            }

            // If a root-motion base rule just finished, turn off root motion
            if (layer == AnimLayer.Base)
                DisableRootMotion();
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

    // ═════════════════════════════════════════════════════
    //  CONDITION MATCHING (unchanged)
    // ═════════════════════════════════════════════════════

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
                BoolParam.IsMounted => ctx.IsMounted,
                BoolParam.IsTransitioning => ctx.IsTransitioning,
                BoolParam.Crouching => ctx.Crouching,
                BoolParam.Dodging => ctx.Dodging,
                BoolParam.Blocking => ctx.Blocking,
                BoolParam.BowDrawing => ctx.BowDrawing,
                BoolParam.BowAiming => ctx.BowAiming,
                BoolParam.Equipping => ctx.Equipping,
                BoolParam.Holstering => ctx.Holstering,
                BoolParam.WeaponSlot0 => ctx.ActiveWeaponSlot == 0,
                BoolParam.WeaponSlot1 => ctx.ActiveWeaponSlot == 1,
                BoolParam.WeaponSlot2 => ctx.ActiveWeaponSlot == 2,
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

    /// <summary>
    /// Checks if a specific clip is already playing on a given Animancer layer.
    /// </summary>
    private bool IsPlayingClipOnLayer(AnimancerLayer layer, AnimationClip clip)
    {
        var current = layer.CurrentState;
        return current != null && current.Clip == clip;
    }

    public void SetActiveWeapon(string profileName)
    {
        defaultWeaponName = profileName;
    }
}