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

    [Header("Hit Reaction")]
    [Tooltip("Key in AnimationSet for the hit flinch clip. Must have useRootMotion enabled.")]
    [SerializeField] private string hitReactionKey = "Hit/Flinch";
    private bool _isPlayingHitReaction = false;

    [Header("Parry")]
    [Tooltip("Key in AnimationSet for the humanoid parry clip.")]
    [SerializeField] private string parryKey = "Sword/Block";
    [SerializeField] private float parryFadeOutDelay = 0.35f;

    [Header("Death & Respawn")]
    [Tooltip("Key in AnimationSet for the death clip. Root motion OFF recommended.")]
    [SerializeField] private string deathAnimationKey = "Death/Fall";
    private bool _isDead = false;

    [Tooltip("Degrees per second to lerp toward attacker during flinch.")]
    [SerializeField] private float hitTurnLerpSpeed = 720f;

    [Header("Layer Masks")]
    [Tooltip("Upper body mask for Action layer (unsheathe, interactions). " +
             "Create via Assets > Create > Avatar Mask, enable spine/arms/head only.")]
    [SerializeField] private AvatarMask actionLayerMask;

    [Tooltip("Optional mask for Attack layer. Leave null for full body attacks.")]
    [SerializeField] private AvatarMask attackLayerMask;

    [Tooltip("Fade duration when a masked layer finishes and fades out.")]
    [SerializeField] private float layerFadeOutDuration = 0.15f;

    [Header("Bow Aim IK")]
    [SerializeField] private Vector3 spineAimAxis = Vector3.right;
    [SerializeField] private float spineChestWeight = 0.4f;
    [SerializeField] private float spineUpperChestWeight = 0.6f;

    [Header("Root Motion")]
    [Tooltip("When enabled, root motion from the current Base rule (if flagged) " +
             "will drive the CharacterController via OnAnimatorMove.")]
    [SerializeField] private bool allowRootMotion = true;

    [Header("Combat Integration")]
    [SerializeField] private CombatController combatController;
    [SerializeField] private WeaponManager weaponManager;

    [Header("Blend Tree Locomotion (optional)")]
    [Tooltip("When assigned, sword combat locomotion uses a 2D blend tree instead of directional rules.")]
    [SerializeField] private CombatLocomotionMixer combatMixer;

    [Header("Witcher-Style Attack Settings")]
    [SerializeField] private float doubleClickWindow = 0.25f;
    [SerializeField] private float comboHoldThreshold = 0.12f;
    [SerializeField] private string defaultWeaponName = "Sword";
    [Tooltip("Seconds of inactivity after which the single-attack sequence resets to the beginning.")]
    [SerializeField] private float singleAttackResetTime = 10f;

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

    // Dodge mixer one-shot tracking
    private bool _dodgeMixerStartedThisDodge;
    private bool _dodgeStepMixerStartedThisStep;

    /// <summary>True while a dodge/dodge-step mixer animation is still playing.</summary>
    public bool IsDodgeMixerActive => _dodgeMixerStartedThisDodge || _dodgeStepMixerStartedThisStep;

    // Network — evaluated lazily after spawn so IsOwner is valid
    private bool _isRemoteClient => networkSync != null && networkSync.IsSpawned && !networkSync.IsOwner;

    // ───────────────────── Public API ─────────────────────

    public AnimancerComponent Animancer => _animancer;
    public bool IsLocked => IsLayerLocked(AnimLayer.Attack) || IsLayerLocked(AnimLayer.Action);
    public bool IsAttackLocked => IsLayerLocked(AnimLayer.Attack);

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
        if (combatController == null) combatController = GetComponentInParent<CombatController>();
        if (weaponManager == null) weaponManager = GetComponentInParent<WeaponManager>();

        SetupLayers();

        // Initialize blend tree mixer if assigned
        if (combatMixer == null) combatMixer = GetComponentInChildren<CombatLocomotionMixer>(true);
        if (combatMixer != null) combatMixer.Initialize(_animancer);
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
        //      // 0) Dead — skip all animation logic
        if (_isDead) return;
        // 1) Hit reaction playing → skip all rule evaluation (hit owns Base)
        if (_isPlayingHitReaction)
            return;

        // Bow aim spine rotation
        // For the local owner: use Camera.main pitch directly.
        // For remote puppets: use the synced RemoteAimPitch from the owner.
        // Gate: only apply when bow is slot 2 AND player is aiming/drawing.
        bool bowIsActive = ctx.BowAiming || ctx.BowDrawing;
        if (combatController != null && ctx.ActiveWeaponSlot == 2 && bowIsActive)
        {
            float rawPitch;
            if (_isRemoteClient && networkSync != null)
            {
                rawPitch = networkSync.RemoteAimPitch;
            }
            else
            {
                rawPitch = Camera.main != null ? Camera.main.transform.eulerAngles.x : 0f;
                if (rawPitch > 180f) rawPitch -= 360f;
            }

            float aimPitch = -rawPitch; // invert: looking up = positive spine bend

            var chest      = _animator.GetBoneTransform(HumanBodyBones.Chest);
            var upperChest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);

            if (chest != null)
                chest.Rotate(Vector3.up, aimPitch * spineChestWeight, Space.Self);
            if (upperChest != null)
                upperChest.Rotate(Vector3.up, aimPitch * spineUpperChestWeight, Space.Self);
        }
        // 2) Attack locked → skip everything (full body, frame-critical)
        if (IsLayerLocked(AnimLayer.Attack))
        {
            TryPlayBestRule(ctx, AnimLayer.Base);
            return;
        }
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
        // No Attack rule matched: if the layer is still at full weight (e.g. a non-locking
        // Attack rule like Bow/Release just played and has no OnEnd cleanup), fade it out
        // and restore the default mask. Mid-fade frames (weight between 0 and 1) skip this
        // so we don't re-trigger StartFade every frame.
        bool attackFadingOut = !IsLayerLocked(AnimLayer.Attack) && _attackLayer.Weight > 0f && _attackLayer.Weight < 1f;
        if (!IsLayerLocked(AnimLayer.Attack) && !attackFadingOut && _attackLayer.Weight > 0f)
        {
            _attackLayer.SetMask(attackLayerMask);
            _attackLayer.StartFade(0, layerFadeOutDuration);
        }
        bool actionFadingOut = !IsLayerLocked(AnimLayer.Action) && _actionLayer.Weight > 0f && _actionLayer.Weight < 1f;
        if (!IsLayerLocked(AnimLayer.Action) && !actionFadingOut)
        {
            bool actionRulePlayed = TryPlayBestRule(ctx, AnimLayer.Action);
            // If no Action rule matched and the layer still has weight, fade it out and reset its mask
            if (!actionRulePlayed && _actionLayer.Weight > 0f)
            {
                _actionLayer.SetMask(actionLayerMask);
                _actionLayer.StartFade(0, layerFadeOutDuration);
            }
        }

        // ── Dodge / Dodge Step via blend tree mixer (one-shot) ──
        // Play once when dodge starts, block base rules while CombatController says we're dodging.
        if (combatMixer != null)
        {
            // Reset flags when CombatController exits dodge/dodgestep,
            // so the next press can trigger again.
            if (_dodgeMixerStartedThisDodge && !ctx.Dodging)
            {
                _dodgeMixerStartedThisDodge = false;
                DisableRootMotion();
            }
            if (_dodgeStepMixerStartedThisStep && !ctx.IsDodgeStep)
            {
                _dodgeStepMixerStartedThisStep = false;
                DisableRootMotion();
            }

            // Block base layer while dodge/dodgestep is active
            if (_dodgeMixerStartedThisDodge || _dodgeStepMixerStartedThisStep)
                return;

            // Start dodge mixer (once per dodge)
            if (ctx.Dodging && combatMixer.HasDodgeMixer && !_dodgeMixerStartedThisDodge)
            {
                // While sprinting in a direction the body already faces movement,
                // so always play the front-dodge clip — its root motion will carry
                // the dodge forward in the same direction the player is running.
                bool sprintInDir = ctx.Modified && ctx.snapshot.move.sqrMagnitude > 0.01f;
                Vector2 dodgeInput = sprintInDir ? new Vector2(0f, 1f) : ctx.snapshot.move;
                var mixerState = combatMixer.PlayDodge(_baseLayer, dodgeInput);
                if (mixerState != null)
                {
                    _dodgeMixerStartedThisDodge = true;
                    _rootMotionActive = allowRootMotion;
                    if (_animator != null) _animator.applyRootMotion = allowRootMotion;
                    return;
                }
            }

            // Start dodge step mixer (once per step)
            if (ctx.IsDodgeStep && combatMixer.HasDodgeStepMixer && !_dodgeStepMixerStartedThisStep)
            {
                var mixerState = combatMixer.PlayDodgeStep(_baseLayer, ctx.snapshot.move);
                if (mixerState != null)
                {
                    _dodgeStepMixerStartedThisStep = true;
                    _rootMotionActive = allowRootMotion;
                    if (_animator != null) _animator.applyRootMotion = allowRootMotion;
                    return;
                }
            }
        }

        // ── Blend tree locomotion: if the mixer wants control, it drives Base ──
        bool mixerActive = false;
        bool dodgeMixerOwnsBase = _dodgeMixerStartedThisDodge || _dodgeStepMixerStartedThisStep;
        if (!dodgeMixerOwnsBase && combatMixer != null && combatMixer.WantsControl(
                ctx.ActiveWeaponSlot, ctx.Moving, ctx.Dodging,
                ctx.Blocking, ctx.BowDrawing, ctx.BowAiming, ctx.IsMounted, ctx.IsDodgeStep, ctx.Modified))
        {
            combatMixer.UpdateAndPlay(_baseLayer, ctx.snapshot.move, ctx.ActiveWeaponSlot, ctx.BowDrawing || ctx.BowAiming);
            _rootMotionActive = false;
            if (_animator != null) _animator.applyRootMotion = false;
            mixerActive = true;
        }
        else
        {
            // Mixer lost control — reset so it fades in clean next time
            if (combatMixer != null) combatMixer.ResetActiveState();
        }

        if (!mixerActive)
        {
            // Skip rule-based dodge/dodgestep while the mixer owns them
            bool skipRuleBase = combatMixer != null &&
                ((ctx.Dodging && combatMixer.HasDodgeMixer) ||
                 (ctx.IsDodgeStep && combatMixer.HasDodgeStepMixer));
            if (!skipRuleBase)
                TryPlayBestRule(ctx, AnimLayer.Base);
        }

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

        // Apply per-rule mask override, or restore default layer mask, or clear to full body
        if (best.maskOverride != null)
            animLayer.SetMask(best.maskOverride);
        else if (layer == AnimLayer.Action && actionLayerMask != null)
            animLayer.SetMask(actionLayerMask);
        else if (layer == AnimLayer.Attack && attackLayerMask != null)
            animLayer.SetMask(attackLayerMask);
        else
            animLayer.SetMask(null);

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

        // Sequential attack tracking (light, heavy, single share same timeout logic)
        public int lightIndex = 0;
        public int heavyIndex = 0;
        public int singleIndex = 0;
        public float lastLightAttackTime = -999f;
        public float lastHeavyAttackTime = -999f;
        public float lastSingleAttackTime = -999f;

        public void ResetAll()
        {
            mode = AttackMode.None;
            comboIsHeavy = false;
            // weaponName intentionally kept — used for weapon-change detection in sequential single attacks
            comboKeys = null;
            comboIndex = 0;
            bufferedClick = false;
            bufferedHeavy = false;
            // singleIndex and lastSingleAttackTime intentionally kept
            // so the sequence persists across attacks within the timeout window
        }
    }

    private bool HandleWitcherAttacks(AnimationContext ctx)
    {
        // Skip attacks when mounted with combat disabled
        if (mountController != null && mountController.IsMounted
            && combatController != null && !combatController.allowMountedCombat)
            return false;

        bool down = ctx.snapshot.primaryDown;

        // Stamina check
        if (combatController != null && !combatController.CanAttack())
        {
            return false;
        }
      
        // If currently attacking, buffer the click for combo continuation
        if (IsLayerLocked(AnimLayer.Attack))
        {
            if (down)
            {
                Debug.Log($"[HandleWitcherAttacks] BLOCKED by Attack lock, buffering click");
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

    // ─── Sequential attack helper ───

    private bool PlayNextFromList(List<string> keys, ref int index, ref float lastTime, bool isHeavy)
    {
        if (keys == null || keys.Count == 0) return false;
        index = index % keys.Count;
        string key = keys[index];
        if (!TryPlayAttackKey(key, AttackMode.Single, isHeavy, 0))
            return false;
        index++;
        lastTime = Time.time;
        return true;
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

        bool weaponChanged = !string.Equals(_attack.weaponName, weapon);
        _attack.weaponName = weapon;
        combatController?.ConsumeAttackStamina(heavy);

        if (heavy)
        {
            var keys = profile.heavyComboKeys;
            if (keys != null && keys.Count > 0)
            {
                if (weaponChanged || (Time.time - _attack.lastHeavyAttackTime) > singleAttackResetTime)
                    _attack.heavyIndex = 0;
                return PlayNextFromList(keys, ref _attack.heavyIndex, ref _attack.lastHeavyAttackTime, true);
            }
        }
        else
        {
            var keys = profile.lightComboKeys;
            if (keys != null && keys.Count > 0)
            {
                if (weaponChanged || (Time.time - _attack.lastLightAttackTime) > singleAttackResetTime)
                    _attack.lightIndex = 0;
                return PlayNextFromList(keys, ref _attack.lightIndex, ref _attack.lastLightAttackTime, false);
            }
        }

        // Fallback to singleAttackKeys
        if (profile.singleAttackKeys != null && profile.singleAttackKeys.Count > 0)
        {
            if (weaponChanged || (Time.time - _attack.lastSingleAttackTime) > singleAttackResetTime)
                _attack.singleIndex = 0;
            return PlayNextFromList(profile.singleAttackKeys, ref _attack.singleIndex, ref _attack.lastSingleAttackTime, heavy);
        }

        return false;
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

    /// <summary>
    /// Called on remote clients when the owner starts equipping a weapon.
    /// Directly plays the correct equip clip on the Action layer, bypassing rule evaluation.
    /// </summary>
    public void PlayNetworkedEquip(int slot)
    {
        string key = slot switch
        {
            1 => "Sword/Equip",
            2 => "Bow/Equip",
            _ => null
        };
        if (string.IsNullOrEmpty(key)) return;
        if (!animationSet.TryGet(key, out var transition) || transition == null || transition.Clip == null)
        {
            Debug.LogWarning($"[PlayNetworkedEquip] Missing key '{key}' in AnimationSet.");
            return;
        }
        // Apply the rule's mask override (same as TryPlayBestRule would)
        if (transition.Clip != null && actionLayerMask != null)
            _actionLayer.SetMask(actionLayerMask);
        var state = _actionLayer.Play(transition, actionFade);
        state.Time = 0;
        LockLayerUntilEnd(AnimLayer.Action, state);
    }

    /// <summary>
    /// Called on remote clients when the owner fires a bow draw.
    /// Directly plays Bow/Draw on the Attack layer, bypassing rule evaluation.
    /// </summary>
    public void PlayNetworkedBowDraw()
    {
        const string key = "Bow/Draw";
        if (!animationSet.TryGet(key, out var transition) || transition == null || transition.Clip == null)
        {
            Debug.LogWarning($"[PlayNetworkedBowDraw] Missing key '{key}' in AnimationSet.");
            return;
        }
        var state = _attackLayer.Play(transition, attackFade);
        state.Time = 0;
        LockAttackUntilEnd(state);
    }

    // ═════════════════════════════════════════════════════
    //  LAYER LOCKING
    // ═════════════════════════════════════════════════════

    private void LockAttackUntilEnd(AnimancerState state)
    {
        _isLocked[AnimLayer.Attack] = true;
        _lockedState[AnimLayer.Attack] = state;

        state.Events.SetShouldNotModifyReason(null);
        state.Events.OnEnd = () =>
        {
            // Null out OnEnd first to prevent re-firing on looping clips
            state.Events.SetShouldNotModifyReason(null);
            state.Events.OnEnd = null;

            if (activeHitbox != null)
                activeHitbox.DisableHitbox();

            _isLocked[AnimLayer.Attack] = false;
            _lockedState[AnimLayer.Attack] = null;

            // Restore default attack layer mask (clears any per-rule mask override)
            _attackLayer.SetMask(attackLayerMask);

            // Fade out attack layer so Base takes over
            _attackLayer.StartFade(0, layerFadeOutDuration);

            _attack.ResetAll();
            DisableRootMotion();
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

        state.Events.SetShouldNotModifyReason(null);
        state.Events.OnEnd = () =>
        {
            // Null out OnEnd first to prevent re-firing on looping clips
            state.Events.SetShouldNotModifyReason(null);
            state.Events.OnEnd = null;

            _isLocked[layer] = false;
            _lockedState[layer] = null;

            // Fade out the masked layer when action/attack finishes
            if (layer != AnimLayer.Base)
            {
                var animLayer = GetAnimancerLayer(layer);

                // Restore default mask so it doesn't bleed onto the next rule
                if (layer == AnimLayer.Attack)
                    animLayer.SetMask(attackLayerMask);
                else if (layer == AnimLayer.Action)
                    animLayer.SetMask(actionLayerMask);

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
            // Safety: if the clip has played past its full length (looping clip),
            // force-unlock — the OnEnd callback should have fired but didn't
            if (state.IsPlaying && state.Time >= state.Length)
            {
                // Manually run cleanup that OnEnd should have done
                if (layer == AnimLayer.Attack)
                {
                    if (activeHitbox != null) activeHitbox.DisableHitbox();
                    _attackLayer.SetMask(attackLayerMask);
                    _attackLayer.StartFade(0, layerFadeOutDuration);
                    _attack.ResetAll();
                    DisableRootMotion();
                }
                _isLocked[layer] = false;
                _lockedState[layer] = null;
                return false;
            }

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
                BoolParam.IsDodgeStep => ctx.IsDodgeStep,
                BoolParam.Blocking => ctx.Blocking,
                BoolParam.BowDrawing => ctx.BowDrawing,
                BoolParam.BowAiming => ctx.BowAiming,
                BoolParam.Equipping => ctx.Equipping,
                BoolParam.Holstering => ctx.Holstering,
                BoolParam.WeaponSlot0 => ctx.ActiveWeaponSlot == 0,
                BoolParam.WeaponSlot1 => ctx.ActiveWeaponSlot == 1,
                BoolParam.WeaponSlot2 => ctx.ActiveWeaponSlot == 2,
                BoolParam.PendingSlot1 => ctx.PendingWeaponSlot == 1,
                BoolParam.PendingSlot2 => ctx.PendingWeaponSlot == 2,
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
            if (c.direction == Direction4.W  && d != "W")  return false;
            if (c.direction == Direction4.A  && d != "A")  return false;
            if (c.direction == Direction4.S  && d != "S")  return false;
            if (c.direction == Direction4.D  && d != "D")  return false;
            if (c.direction == Direction4.WA && d != "WA") return false;
            if (c.direction == Direction4.WD && d != "WD") return false;
            if (c.direction == Direction4.SA && d != "SA") return false;
            if (c.direction == Direction4.SD && d != "SD") return false;
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

    public void PlayParry()
    {
        if (_isDead) return;
        if (!animationSet.TryGet(parryKey, out var transition)
            || transition == null
            || transition.Clip == null)
        {
            Debug.LogWarning($"[Parry] Key '{parryKey}' not found in AnimationSet.");
            return;
        }

        if (IsLayerLocked(AnimLayer.Attack))
            CancelCurrentAttack();

        _actionLayer.SetMask(actionLayerMask);
        var state = _actionLayer.Play(transition, actionFade);
        state.Time = 0f;

        bool wantRoot = allowRootMotion && animationSet.IsRootMotion(parryKey);
        _rootMotionActive = wantRoot;
        if (_animator != null)
            _animator.applyRootMotion = wantRoot;

        _isLocked[AnimLayer.Action] = true;
        _lockedState[AnimLayer.Action] = state;

        StopCoroutine(nameof(ParryRoutine));
        StartCoroutine(ParryRoutine(state));
    }

    private System.Collections.IEnumerator ParryRoutine(AnimancerState state)
    {
        yield return new WaitForSeconds(parryFadeOutDelay);

        if (_lockedState.TryGetValue(AnimLayer.Action, out var lockedState) && lockedState == state)
        {
            _isLocked[AnimLayer.Action] = false;
            _lockedState[AnimLayer.Action] = null;
        }

        if (_actionLayer.CurrentState == state)
            _actionLayer.StartFade(0f, layerFadeOutDuration);

        DisableRootMotion();
    }

    /// <summary>
    /// Call this from your damage/health system (or a ClientRpc) when this
    /// character takes a hit. Plays the flinch animation on the Base layer
    /// and lerps the character to face the attacker.
    ///
    /// Works on both owner and remote clients — call it on whoever receives
    /// the damage notification.
    /// </summary>
    public void PlayHitReaction(Vector3 attackerWorldPos)
    {
        if (_isDead) return;
        if (!animationSet.TryGet(hitReactionKey, out var transition)
            || transition == null
            || transition.Clip == null)
        {
            Debug.LogWarning($"[HitReaction] Key '{hitReactionKey}' not found in AnimationSet.");
            return;
        }

        // Cancel any in-progress attack so the Attack layer doesn't stay locked
        if (IsLayerLocked(AnimLayer.Attack))
            CancelCurrentAttack();

        // Stop any existing hit reaction coroutine
        StopCoroutine(nameof(HitReactionRoutine));

        StartCoroutine(HitReactionRoutine(attackerWorldPos, transition));
    }

    private System.Collections.IEnumerator HitReactionRoutine(
        Vector3 attackerWorldPos,
        Animancer.ClipTransition transition)
    {
        _isPlayingHitReaction = true;

        // ── Root of the character (parent of the driver's GameObject) ──
        Transform root = transform.parent != null ? transform.parent : transform;

        // ── Direction to face (Y-plane only) ──
        Vector3 toAttacker = attackerWorldPos - root.position;
        toAttacker.y = 0f;
        Quaternion targetRot = toAttacker.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(toAttacker.normalized)
            : root.rotation;

        // ── Suppress Action layer so it doesn't bleed through the flinch ──
        float savedActionWeight = _actionLayer.Weight;
        bool actionWasLocked = IsLayerLocked(AnimLayer.Action);
        _actionLayer.StartFade(0f, 0.05f);

        // ── Suppress Attack layer (attack was already cancelled above) ──
        _attackLayer.StartFade(0f, 0.05f);

        // ── Force-unlock Base so the hit can override any ongoing Base lock ──
        _isLocked[AnimLayer.Base] = false;
        _lockedState[AnimLayer.Base] = null;

        // ── Enable root motion for this clip ──
        bool wantRoot = allowRootMotion && animationSet.IsRootMotion(hitReactionKey);
        _rootMotionActive = wantRoot;
        if (_animator != null)
            _animator.applyRootMotion = wantRoot;

        // ── Play flinch on Base layer ──
        var state = _baseLayer.Play(transition, baseFade);
        state.Time = 0f;

        float clipDuration = transition.Clip.length;

        // ── Lerp toward attacker during first half of clip ──
        float turnDuration = clipDuration * 0.5f;
        float elapsed = 0f;

        while (elapsed < turnDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / turnDuration);
            // Smooth step for a bit of easing
            t = t * t * (3f - 2f * t);
            root.rotation = Quaternion.Lerp(root.rotation, targetRot, Time.deltaTime * hitTurnLerpSpeed * (1f - t + 0.1f));
            yield return null;
        }

        // ── Wait for the rest of the clip ──
        float remaining = clipDuration - turnDuration;
        yield return new WaitForSeconds(remaining);

        // ── Cleanup ──
        _isPlayingHitReaction = false;
        DisableRootMotion();

        // Restore Action layer only if it wasn't locked before (sheathing etc.)
        if (!actionWasLocked && savedActionWeight > 0f)
            _actionLayer.StartFade(savedActionWeight, 0.15f);
    }
    /// <summary>
    /// Called via ClientRpc on all clients when this character dies.
    /// Kills all animation layers, plays death clip, freezes on last frame.
    /// </summary>
    public void PlayDeath()
    {
        if (_isDead) return;
        _isDead = true;

        // Stop any hit reaction coroutine
        StopCoroutine(nameof(HitReactionRoutine));
        _isPlayingHitReaction = false;

        // Kill root motion immediately — no sliding corpses
        DisableRootMotion();

        // Force-unlock all layers
        _isLocked[AnimLayer.Base] = false;
        _isLocked[AnimLayer.Action] = false;
        _isLocked[AnimLayer.Attack] = false;
        _lockedState[AnimLayer.Base] = null;
        _lockedState[AnimLayer.Action] = null;
        _lockedState[AnimLayer.Attack] = null;
        _attack.ResetAll();

        // Fade out Action and Attack layers immediately
        _actionLayer.StartFade(0f, 0.05f);
        _attackLayer.StartFade(0f, 0.05f);

        // Play death on Base
        if (!animationSet.TryGet(deathAnimationKey, out var transition)
            || transition == null || transition.Clip == null)
        {
            Debug.LogWarning($"[Death] Key '{deathAnimationKey}' not found in AnimationSet.");
            return;
        }

        var state = _baseLayer.Play(transition, baseFade);
        state.Time = 0f;

        // Freeze on last frame when done
        state.Events.SetShouldNotModifyReason(null);
        state.Events.OnEnd = () =>
        {
            state.Time = transition.Clip.length;
            state.Speed = 0f;
            state.Events.OnEnd = null;
        };
    }

    /// <summary>
    /// Called via ClientRpc on all clients when this character respawns.
    /// Clears all death state and lets the driver resume normal rule evaluation.
    /// </summary>
    public void PlayRespawn()
    {
        _isDead = false;
        _isPlayingHitReaction = false;

        // Clear all locks
        _isLocked[AnimLayer.Base] = false;
        _isLocked[AnimLayer.Action] = false;
        _isLocked[AnimLayer.Attack] = false;
        _lockedState[AnimLayer.Base] = null;
        _lockedState[AnimLayer.Action] = null;
        _lockedState[AnimLayer.Attack] = null;
        _attack.ResetAll();

        DisableRootMotion();

        // Restore layer weights so rule evaluation can take over again
        _actionLayer.StartFade(0f, 0f); // keep at 0, rules will bring it back as needed
        _attackLayer.StartFade(0f, 0f);

        // Unfreeze the base layer state if it was frozen by death
        if (_baseLayer.CurrentState != null)
            _baseLayer.CurrentState.Speed = 1f;

        // LateUpdate will now resume and pick up the correct idle/locomotion rule
    }

}
