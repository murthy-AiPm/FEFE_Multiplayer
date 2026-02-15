using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Main combat state machine for humanoid characters.
/// Reads input from InputSnapshot, coordinates WeaponManager and animations.
/// 
/// States: Idle, Attacking, HeavyAttacking, Dodging, Blocking, Drawing (bow), Aiming
/// 
/// Works with Animancer — plays clips directly from WeaponData ScriptableObjects.
/// If you don't have Animancer yet, swap the PlayClip calls for Animator triggers.
/// </summary>
public class CombatController : NetworkBehaviour
{
    public enum CombatState
    {
        Idle,
        Equipping,
        Holstering,
        Attacking,
        HeavyAttacking,
        Dodging,
        Blocking,
        Drawing,   // bow: pulling back
        Aiming     // bow: held at full draw
    }

    [Header("Dependencies")]
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private HumanoidController humanoidController;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private Animator animator; // fallback if no Animancer

    [Header("Dodge")]
    [SerializeField] private float dodgeSpeed = 8f;
    [SerializeField] private float dodgeDuration = 0.5f;
    [SerializeField] private float dodgeStaminaCost = 15f;
    [SerializeField] private float dodgeCooldown = 0.3f;
    [SerializeField] private float iFrameDuration = 0.25f; // invincibility frames
    [SerializeField] private AnimationClip dodgeClip;

    [Header("Fist Combat")]
    [SerializeField] private float fistDamage = 5f;
    [SerializeField] private float fistStaminaCost = 8f;
    [SerializeField] private float fistRange = 1.5f;
    [SerializeField] private int fistComboLength = 3; // punch, punch, kick pattern
    [SerializeField] private float fistComboWindow = 0.4f;
    [SerializeField] private AnimationClip[] fistAttackClips; // punch1, punch2, kick

    // ─── State ───
    public CombatState State { get; private set; } = CombatState.Idle;
    public bool InCombatMode => weaponManager.ActiveSlot != 0 || State != CombatState.Idle;
    public bool IsInvincible { get; private set; }
    public int CurrentComboIndex { get; private set; }

    // Timing
    private float _stateTimer;
    private float _comboWindowTimer;
    private bool _comboQueued;
    private float _dodgeCooldownTimer;
    private float _iFrameTimer;

    // Dodge direction
    private Vector3 _dodgeDirection;

    // Input cache
    private InputSnapshot _input;

    // Events — for animation, UI, effects
    public event Action<CombatState> OnStateChanged;
    public event Action<int, WeaponData> OnAttackStarted;     // (comboIndex, weapon)
    public event Action OnComboWindowOpen;
    public event Action OnComboReset;
    public event Action<bool> OnCombatModeChanged;

    // Animator parameter hashes (for fallback Animator approach)
    private static readonly int AnimWeaponType = Animator.StringToHash("WeaponType");
    private static readonly int AnimInCombat = Animator.StringToHash("InCombatMode");
    private static readonly int AnimComboIndex = Animator.StringToHash("ComboIndex");
    private static readonly int AnimAttack = Animator.StringToHash("Attack");
    private static readonly int AnimHeavyAttack = Animator.StringToHash("HeavyAttack");
    private static readonly int AnimDodge = Animator.StringToHash("Dodge");
    private static readonly int AnimBlock = Animator.StringToHash("Block");
    private static readonly int AnimIsBlocking = Animator.StringToHash("IsBlocking");
    private static readonly int AnimEquip = Animator.StringToHash("Equip");
    private static readonly int AnimHolster = Animator.StringToHash("Holster");
    private static readonly int AnimBowDraw = Animator.StringToHash("BowDraw");
    private static readonly int AnimBowRelease = Animator.StringToHash("BowRelease");

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Listen to weapon manager events
        if (weaponManager != null)
        {
            weaponManager.OnEquipStart += OnEquipStarted;
            weaponManager.OnHolsterStart += OnHolsterStarted;
            weaponManager.OnWeaponEquipped += OnWeaponReady;
            weaponManager.OnWeaponHolstered += OnWeaponPutAway;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (weaponManager != null)
        {
            weaponManager.OnEquipStart -= OnEquipStarted;
            weaponManager.OnHolsterStart -= OnHolsterStarted;
            weaponManager.OnWeaponEquipped -= OnWeaponReady;
            weaponManager.OnWeaponHolstered -= OnWeaponPutAway;
        }
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsOwner || !IsSpawned) return;
        if (vitalManager != null && vitalManager.IsDead) return;

        _input = playerController.inputController.Snapshot;

        UpdateTimers();
        ProcessWeaponSwapInput();
        ProcessCombatInput();
        UpdateAnimatorParams();
    }

    // ─── Input Processing ───

    private void ProcessWeaponSwapInput()
    {
        if (State == CombatState.Attacking || State == CombatState.HeavyAttacking ||
            State == CombatState.Dodging) return; // can't swap mid-action

        if (_input.slot1Down)
            weaponManager.RequestSlotChange(1);
        else if (_input.slot2Down)
            weaponManager.RequestSlotChange(2);
        else if (_input.hoistWeaponsDown)
            weaponManager.RequestHolster();
    }

    private void ProcessCombatInput()
    {
        switch (State)
        {
            case CombatState.Idle:
                HandleIdleInput();
                break;

            case CombatState.Attacking:
            case CombatState.HeavyAttacking:
                HandleAttackingState();
                break;

            case CombatState.Dodging:
                HandleDodgingState();
                break;

            case CombatState.Blocking:
                HandleBlockingState();
                break;

            case CombatState.Drawing:
                HandleDrawingState();
                break;

            case CombatState.Aiming:
                HandleAimingState();
                break;

            case CombatState.Equipping:
            case CombatState.Holstering:
                // Wait for animation event callbacks
                break;
        }
    }

    // ─── State Handlers ───

    private void HandleIdleInput()
    {
        // Dodge (jump + combat mode, or dedicated dodge key)
        if (_input.jumpDown && InCombatMode && _dodgeCooldownTimer <= 0f)
        {
            TryDodge();
            return;
        }

        var weapon = weaponManager.ActiveWeapon;
        var weaponType = weaponManager.GetActiveWeaponType();

        // Bow handling
        if (weaponType == WeaponType.Bow)
        {
            if (_input.primaryDown)
                BeginBowDraw();
            return;
        }

        // Block (secondary held + has shield or 2H)
        if (_input.secondaryHeld && weaponManager.ActiveSlot == 1)
        {
            BeginBlock();
            return;
        }

        // Light attack
        if (_input.primaryDown)
        {
            TryLightAttack();
            return;
        }

        // Heavy attack (hold primary — we detect hold duration)
        // For simplicity: heavy = secondary + primary when no shield
        // Or we can add a separate heavy input later
    }

    private void HandleAttackingState()
    {
        _stateTimer -= Time.deltaTime;

        // Combo window — buffer next attack input
        if (_comboWindowTimer > 0)
        {
            _comboWindowTimer -= Time.deltaTime;

            if (_input.primaryDown && !_comboQueued)
            {
                _comboQueued = true;
            }
        }

        // Dodge cancel (at any point during attack)
        if (_input.jumpDown && _dodgeCooldownTimer <= 0f)
        {
            TryDodge();
            return;
        }

        // Attack ended (via timer — animation events are preferred, this is fallback)
        if (_stateTimer <= 0f)
        {
            if (_comboQueued)
            {
                _comboQueued = false;
                AdvanceCombo();
            }
            else
            {
                ResetCombo();
                SetState(CombatState.Idle);
            }
        }
    }

    private void HandleDodgingState()
    {
        _stateTimer -= Time.deltaTime;

        // Move character during dodge
        if (humanoidController != null && humanoidController.controller != null)
        {
            humanoidController.controller.Move(_dodgeDirection * dodgeSpeed * Time.deltaTime);
        }

        // I-frame tracking
        if (_iFrameTimer > 0f)
        {
            _iFrameTimer -= Time.deltaTime;
            if (_iFrameTimer <= 0f)
                IsInvincible = false;
        }

        if (_stateTimer <= 0f)
        {
            IsInvincible = false;
            _dodgeCooldownTimer = dodgeCooldown;
            SetState(CombatState.Idle);
        }
    }

    private void HandleBlockingState()
    {
        // Release block when secondary is released
        if (!_input.secondaryHeld)
        {
            SetState(CombatState.Idle);
            return;
        }

        // Can still move while blocking (at reduced speed via HumanoidController)
    }

    private void HandleDrawingState()
    {
        _stateTimer -= Time.deltaTime;

        // Cancel draw
        if (!_input.primaryHeld)
        {
            // Released too early — cancel
            SetState(CombatState.Idle);
            return;
        }

        // Fully drawn
        if (_stateTimer <= 0f)
        {
            SetState(CombatState.Aiming);
        }
    }

    private void HandleAimingState()
    {
        // Release to fire
        if (_input.primaryUp || !_input.primaryHeld)
        {
            FireArrow();
            SetState(CombatState.Idle);
            return;
        }

        // Player faces camera direction while aiming
        if (humanoidController != null && humanoidController.cam != null)
        {
            var camForward = humanoidController.cam.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude > 0.01f)
                humanoidController.transform.rotation = Quaternion.LookRotation(camForward);
        }
    }

    // ─── Actions ───

    private void TryLightAttack()
    {
        var weapon = weaponManager.ActiveWeapon;
        float staminaCost = weapon != null ? weapon.staminaCostLight : fistStaminaCost;

        // Check stamina
        if (vitalManager != null && !vitalManager.TryConsumeStamina(staminaCost))
            return; // not enough stamina

        CurrentComboIndex = 0;
        _comboQueued = false;
        PerformAttack(false);
    }

    private void AdvanceCombo()
    {
        var weapon = weaponManager.ActiveWeapon;
        int maxCombo = weapon != null ? weapon.comboLength : fistComboLength;

        CurrentComboIndex++;
        if (CurrentComboIndex >= maxCombo)
            CurrentComboIndex = 0; // loop back

        float staminaCost = weapon != null ? weapon.staminaCostLight : fistStaminaCost;
        if (vitalManager != null && !vitalManager.TryConsumeStamina(staminaCost))
        {
            ResetCombo();
            SetState(CombatState.Idle);
            return;
        }

        PerformAttack(false);
    }

    private void PerformAttack(bool isHeavy)
    {
        var weapon = weaponManager.ActiveWeapon;
        SetState(isHeavy ? CombatState.HeavyAttacking : CombatState.Attacking);

        // Determine clip duration for state timer
        AnimationClip clip = GetAttackClip(weapon, isHeavy);
        float speed = weapon != null ? weapon.attackSpeed : 1f;
        float duration = clip != null ? clip.length / speed : 0.5f;

        _stateTimer = duration;

        // Combo window opens in the last portion of the animation
        float comboWindow = weapon != null ? weapon.comboWindowDuration : fistComboWindow;
        _comboWindowTimer = 0f; // set by animation event or estimated

        // Set animator
        if (animator != null)
        {
            animator.SetInteger(AnimComboIndex, CurrentComboIndex);
            animator.SetTrigger(isHeavy ? AnimHeavyAttack : AnimAttack);
        }

        OnAttackStarted?.Invoke(CurrentComboIndex, weapon);

        // Notify server for validation
        RequestAttackServerRpc(CurrentComboIndex, isHeavy, weaponManager.ActiveSlot);
    }

    private void TryDodge()
    {
        if (vitalManager != null && !vitalManager.TryConsumeStamina(dodgeStaminaCost))
            return;

        // Dodge direction: input direction or backward
        Vector3 moveDir = Vector3.zero;
        if (_input.movePressed && humanoidController != null && humanoidController.cam != null)
        {
            float targetAngle = Mathf.Atan2(_input.move.x, _input.move.y) * Mathf.Rad2Deg
                                + humanoidController.cam.eulerAngles.y;
            moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        }
        else
        {
            moveDir = -humanoidController.transform.forward; // dodge backward
        }

        _dodgeDirection = moveDir.normalized;
        _stateTimer = dodgeDuration;
        _iFrameTimer = iFrameDuration;
        IsInvincible = true;

        SetState(CombatState.Dodging);

        if (animator != null)
            animator.SetTrigger(AnimDodge);
    }

    private void BeginBlock()
    {
        SetState(CombatState.Blocking);

        if (animator != null)
            animator.SetBool(AnimIsBlocking, true);
    }

    private void BeginBowDraw()
    {
        var weapon = weaponManager.ActiveWeapon;
        if (weapon == null) return;

        float staminaCost = weapon.staminaCostLight;
        if (vitalManager != null && !vitalManager.TryConsumeStamina(staminaCost))
            return;

        _stateTimer = weapon.drawTime;
        SetState(CombatState.Drawing);

        if (animator != null)
            animator.SetTrigger(AnimBowDraw);
    }

    private void FireArrow()
    {
        var weapon = weaponManager.ActiveWeapon;
        if (weapon == null || weapon.weaponType != WeaponType.Bow) return;

        if (animator != null)
            animator.SetTrigger(AnimBowRelease);

        // Request server to spawn arrow
        RequestFireArrowServerRpc();
    }

    // ─── Animation Event Callbacks ───
    // Wire these in Unity Animation window on your clips

    /// <summary>
    /// Called by animation event when the combo window opens (late in attack anim).
    /// </summary>
    public void OnAnimEvent_ComboWindowOpen()
    {
        var weapon = weaponManager.ActiveWeapon;
        float window = weapon != null ? weapon.comboWindowDuration : fistComboWindow;
        _comboWindowTimer = window;
        OnComboWindowOpen?.Invoke();
    }

    /// <summary>
    /// Called by animation event when attack animation ends.
    /// </summary>
    public void OnAnimEvent_AttackEnd()
    {
        if (State == CombatState.Attacking || State == CombatState.HeavyAttacking)
        {
            if (_comboQueued)
            {
                _comboQueued = false;
                AdvanceCombo();
            }
            else
            {
                ResetCombo();
                SetState(CombatState.Idle);
            }
        }
    }

    /// <summary>
    /// Called by animation event when dodge animation ends.
    /// </summary>
    public void OnAnimEvent_DodgeEnd()
    {
        if (State == CombatState.Dodging)
        {
            IsInvincible = false;
            _dodgeCooldownTimer = dodgeCooldown;
            SetState(CombatState.Idle);
        }
    }

    // ─── Helpers ───

    private void SetState(CombatState newState)
    {
        if (State == newState) return;

        // Exit old state
        if (State == CombatState.Blocking && animator != null)
            animator.SetBool(AnimIsBlocking, false);

        var oldState = State;
        State = newState;
        OnStateChanged?.Invoke(newState);

        // Notify combat mode change
        bool wasCombat = oldState != CombatState.Idle;
        bool isCombat = newState != CombatState.Idle;
        if (wasCombat != isCombat)
            OnCombatModeChanged?.Invoke(isCombat);
    }

    private void ResetCombo()
    {
        CurrentComboIndex = 0;
        _comboQueued = false;
        _comboWindowTimer = 0;
        OnComboReset?.Invoke();
    }

    private void UpdateTimers()
    {
        if (_dodgeCooldownTimer > 0f)
            _dodgeCooldownTimer -= Time.deltaTime;
    }

    private void UpdateAnimatorParams()
    {
        if (animator == null) return;

        animator.SetInteger(AnimWeaponType, (int)weaponManager.GetActiveWeaponType());
        animator.SetBool(AnimInCombat, InCombatMode);
    }

    private AnimationClip GetAttackClip(WeaponData weapon, bool isHeavy)
    {
        if (weapon == null)
        {
            // Fist clips
            if (fistAttackClips != null && fistAttackClips.Length > 0)
                return fistAttackClips[Mathf.Clamp(CurrentComboIndex, 0, fistAttackClips.Length - 1)];
            return null;
        }

        if (isHeavy) return weapon.heavyAttackClip;
        return weapon.GetLightAttackClip(CurrentComboIndex);
    }

    // ─── WeaponManager Callbacks ───

    private void OnEquipStarted()
    {
        SetState(CombatState.Equipping);
        if (animator != null) animator.SetTrigger(AnimEquip);
    }

    private void OnHolsterStarted()
    {
        SetState(CombatState.Holstering);
        if (animator != null) animator.SetTrigger(AnimHolster);
    }

    private void OnWeaponReady(WeaponData weapon)
    {
        SetState(CombatState.Idle);
    }

    private void OnWeaponPutAway(WeaponData weapon)
    {
        // State transition handled by WeaponManager (may chain into equip)
    }

    // ─── Network RPCs ───

    [ServerRpc]
    private void RequestAttackServerRpc(int comboIndex, bool isHeavy, int weaponSlot)
    {
        // Server validates and triggers hit detection
        // HitboxController handles the actual damage via its own animation events
    }

    [ServerRpc]
    private void RequestFireArrowServerRpc()
    {
        // Server spawns authoritative arrow projectile
        var weapon = weaponManager.GetWeaponForSlot(2);
        if (weapon == null || weapon.arrowPrefab == null) return;

        var spawnPos = transform.position + Vector3.up * 1.5f + transform.forward * 0.5f;
        var arrow = Instantiate(weapon.arrowPrefab, spawnPos, transform.rotation);

        var rb = arrow.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = transform.forward * weapon.arrowSpeed;

        var netObj = arrow.GetComponent<NetworkObject>();
        if (netObj != null)
            netObj.Spawn();

        // Arrow handles its own collision / damage via ArrowProjectile component
    }

    // ─── Public API for HumanoidController ───

    /// <summary>
    /// Is the player currently in an action that should prevent movement?
    /// </summary>
    public bool IsActionLocked()
    {
        return State == CombatState.Attacking ||
               State == CombatState.HeavyAttacking ||
               State == CombatState.Equipping ||
               State == CombatState.Holstering;
    }

    /// <summary>
    /// Should movement speed be reduced? (blocking, aiming)
    /// </summary>
    public bool IsSlowMovement()
    {
        return State == CombatState.Blocking ||
               State == CombatState.Aiming ||
               State == CombatState.Drawing;
    }

    /// <summary>
    /// Should the character face camera direction? (aiming, blocking with lock-on)
    /// </summary>
    public bool ShouldFaceCamera()
    {
        return State == CombatState.Aiming ||
               State == CombatState.Drawing ||
               State == CombatState.Blocking;
    }
}
