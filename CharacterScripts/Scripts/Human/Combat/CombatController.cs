using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lightweight combat coordinator for humanoid defenders.
/// 
/// Does NOT handle attack input or animation — that's RuleAnimancerDriver's job.
/// This script handles:
///   - Dodge / roll with i-frames
///   - Block state
///   - Stamina gating (checks before attacks go through)
///   - Weapon slot switching (delegates to WeaponManager)
///   - Feeding combat state to AnimationContext for rule evaluation
///   - Bow draw/aim state
///   
/// CombatController sets flags that RuleAnimancerDriver reads via AnimationContext.
/// </summary>
public class CombatController : NetworkBehaviour
{
    public enum CombatState
    {
        None,
        Dodging,
        Blocking,
        BowDrawing,
        BowAiming,
        Dead
    }

    [Header("Dependencies")]
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private HumanoidController humanoidController;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private RuleAnimancerDriver animancerDriver;

    [Header("Dodge")]
    [SerializeField] private float dodgeSpeed = 8f;
    [SerializeField] private float dodgeDuration = 0.5f;
    [SerializeField] private float dodgeStaminaCost = 15f;
    [SerializeField] private float dodgeCooldown = 0.3f;
    [SerializeField] private float iFrameDuration = 0.25f;

    [Header("Block")]
    [SerializeField] private float blockStaminaDrain = 3f; // per second while holding block

    [Header("Stamina Costs")]
    [SerializeField] private float fistStaminaCost = 8f;

    // ─── State ───
    public CombatState State { get; private set; } = CombatState.None;
    public bool IsInvincible { get; private set; }
    public bool IsDodging => State == CombatState.Dodging;
    public bool IsBlocking => State == CombatState.Blocking;
    public bool IsBowDrawing => State == CombatState.BowDrawing;
    public bool IsBowAiming => State == CombatState.BowAiming;
    public bool IsDead => State == CombatState.Dead;

    // Timing
    private float _dodgeTimer;
    private float _dodgeCooldownTimer;
    private float _iFrameTimer;
    private float _bowDrawTimer;
    private Vector3 _dodgeDirection;

    // Input cache
    private InputSnapshot _input;

    // Events
    public event Action<CombatState> OnStateChanged;
    public event Action OnDodgeStarted;
    public event Action OnBlockStarted;
    public event Action OnBlockEnded;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (vitalManager != null)
            vitalManager.OnDeath += HandleDeath;
    }

    public override void OnNetworkDespawn()
    {
        if (vitalManager != null)
            vitalManager.OnDeath -= HandleDeath;

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsOwner || !IsSpawned) return;
        if (State == CombatState.Dead) return;

        _input = playerController.inputController.Snapshot;

        UpdateTimers();
        ProcessWeaponSwapInput();
        ProcessCombatStateInput();
        UpdateWeaponProfileOnDriver();
    }

    // ─── Weapon Swap Input ───

    private void ProcessWeaponSwapInput()
    {
        // Don't swap mid-dodge
        if (State == CombatState.Dodging) return;

        // Don't swap if RuleAnimancerDriver is locked in an attack
        if (animancerDriver != null && animancerDriver.IsLocked) return;

        if (_input.slot1Down)
            weaponManager.RequestSlotChange(1);
        else if (_input.slot2Down)
            weaponManager.RequestSlotChange(2);
        else if (_input.hoistWeaponsDown)
            weaponManager.RequestHolster();
    }

    // ─── Combat State Input ───

    private void ProcessCombatStateInput()
    {
        switch (State)
        {
            case CombatState.None:
                HandleIdleInput();
                break;

            case CombatState.Dodging:
                UpdateDodge();
                break;

            case CombatState.Blocking:
                UpdateBlock();
                break;

            case CombatState.BowDrawing:
                UpdateBowDraw();
                break;

            case CombatState.BowAiming:
                UpdateBowAim();
                break;
        }
    }

    // ─── Idle → Check for dodge, block, bow ───

    private void HandleIdleInput()
    {
        // Dodge: jump while in combat mode
        bool inCombat = weaponManager.ActiveSlot != 0;
        if (_input.jumpDown && inCombat && _dodgeCooldownTimer <= 0f)
        {
            TryDodge();
            return;
        }

        // Block: secondary held + primary melee equipped
        var weaponType = weaponManager.GetActiveWeaponType();
        if (_input.secondaryHeld && weaponManager.ActiveSlot == 1 &&
            (weaponType == WeaponType.OneHanded || weaponType == WeaponType.TwoHanded))
        {
            BeginBlock();
            return;
        }

        // Bow draw: primary pressed while bow equipped
        if (weaponType == WeaponType.Bow && _input.primaryDown)
        {
            BeginBowDraw();
            return;
        }

        // Attacks are handled by RuleAnimancerDriver — we just need to gate stamina.
        // The driver will call our CanAttack() / ConsumeAttackStamina() methods.
    }

    // ─── Dodge ───

    private void TryDodge()
    {
        if (vitalManager != null && !vitalManager.TryConsumeStamina(dodgeStaminaCost))
            return;

        // Direction: input direction or backward
        if (_input.movePressed && humanoidController != null && humanoidController.cam != null)
        {
            float targetAngle = Mathf.Atan2(_input.move.x, _input.move.y) * Mathf.Rad2Deg
                                + humanoidController.cam.eulerAngles.y;
            _dodgeDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        }
        else
        {
            _dodgeDirection = -humanoidController.transform.forward;
        }

        _dodgeDirection.Normalize();
        _dodgeTimer = dodgeDuration;
        _iFrameTimer = iFrameDuration;
        IsInvincible = true;

        SetState(CombatState.Dodging);
        OnDodgeStarted?.Invoke();
    }

    private void UpdateDodge()
    {
        _dodgeTimer -= Time.deltaTime;

        // Move during dodge
        if (humanoidController != null && humanoidController.controller != null)
            humanoidController.controller.Move(_dodgeDirection * dodgeSpeed * Time.deltaTime);

        // I-frames
        if (_iFrameTimer > 0f)
        {
            _iFrameTimer -= Time.deltaTime;
            if (_iFrameTimer <= 0f)
                IsInvincible = false;
        }

        if (_dodgeTimer <= 0f)
        {
            IsInvincible = false;
            _dodgeCooldownTimer = dodgeCooldown;
            SetState(CombatState.None);
        }
    }

    // ─── Block ───

    private void BeginBlock()
    {
        SetState(CombatState.Blocking);
        OnBlockStarted?.Invoke();
    }

    private void UpdateBlock()
    {
        if (!_input.secondaryHeld)
        {
            SetState(CombatState.None);
            OnBlockEnded?.Invoke();
            return;
        }

        // Drain stamina while blocking
        if (vitalManager != null)
        {
            var stamina = vitalManager.GetVital("stamina");
            if (stamina != null && stamina.Current <= 0f)
            {
                // Stamina depleted — forced to drop block
                SetState(CombatState.None);
                OnBlockEnded?.Invoke();
                return;
            }
            vitalManager.TryConsumeStamina(blockStaminaDrain * Time.deltaTime);
        }
    }

    // ─── Bow ───

    private void BeginBowDraw()
    {
        var weapon = weaponManager.ActiveWeapon;
        if (weapon == null) return;

        if (vitalManager != null && !vitalManager.TryConsumeStamina(weapon.staminaCostLight))
            return;

        _bowDrawTimer = weapon.drawTime;
        SetState(CombatState.BowDrawing);
    }

    private void UpdateBowDraw()
    {
        _bowDrawTimer -= Time.deltaTime;

        if (!_input.primaryHeld)
        {
            // Released early — cancel
            SetState(CombatState.None);
            return;
        }

        if (_bowDrawTimer <= 0f)
            SetState(CombatState.BowAiming);
    }

    private void UpdateBowAim()
    {
        // Face camera direction
        if (humanoidController != null && humanoidController.cam != null)
        {
            var camForward = humanoidController.cam.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude > 0.01f)
                humanoidController.transform.rotation = Quaternion.LookRotation(camForward);
        }

        if (_input.primaryUp || !_input.primaryHeld)
        {
            FireArrow();
            SetState(CombatState.None);
        }
    }

    private void FireArrow()
    {
        RequestFireArrowServerRpc();
    }

    [ServerRpc]
    private void RequestFireArrowServerRpc()
    {
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
    }

    // ─── Stamina Gating API (called by RuleAnimancerDriver before playing attacks) ───

    /// <summary>
    /// RuleAnimancerDriver should call this before executing an attack.
    /// Returns false if not enough stamina.
    /// </summary>
    public bool CanAttack()
    {
        if (State == CombatState.Dodging || State == CombatState.Dead) return false;
        if (State == CombatState.Blocking) return false; // must release block first

        // Check stamina
        var weapon = weaponManager.ActiveWeapon;
        float cost = weapon != null ? weapon.staminaCostLight : fistStaminaCost;
        var stamina = vitalManager?.GetVital("stamina");
        if (stamina != null && stamina.Current < cost) return false;

        return true;
    }

    /// <summary>
    /// Consume stamina for an attack. Call after CanAttack() returns true.
    /// </summary>
    public void ConsumeAttackStamina(bool isHeavy)
    {
        var weapon = weaponManager.ActiveWeapon;
        float cost;

        if (weapon != null)
            cost = isHeavy ? weapon.staminaCostHeavy : weapon.staminaCostLight;
        else
            cost = fistStaminaCost;

        vitalManager?.TryConsumeStamina(cost);
    }

    // ─── Weapon Profile Sync ───

    /// <summary>
    /// Updates the RuleAnimancerDriver's active weapon profile when weapon slot changes.
    /// Requires adding this public method to RuleAnimancerDriver:
    /// 
    ///   public void SetActiveWeapon(string profileName)
    ///   {
    ///       defaultWeaponName = profileName;
    ///   }
    /// 
    /// Until you add that method, this will use reflection as a fallback.
    /// </summary>
    private string _lastProfileName;

    private void UpdateWeaponProfileOnDriver()
    {
        if (animancerDriver == null || weaponManager == null) return;

        var weapon = weaponManager.ActiveWeapon;
        string profileName = weapon != null ? weapon.weaponProfileName : "Fist";

        // Skip if unchanged
        if (profileName == _lastProfileName) return;
        _lastProfileName = profileName;

        // Use the public method if available
        animancerDriver.SetActiveWeapon(profileName);
    }

    // ─── State Management ───

    private void SetState(CombatState newState)
    {
        if (State == newState) return;
        State = newState;
        OnStateChanged?.Invoke(newState);
    }

    private void UpdateTimers()
    {
        if (_dodgeCooldownTimer > 0f)
            _dodgeCooldownTimer -= Time.deltaTime;
    }

    private void HandleDeath()
    {
        SetState(CombatState.Dead);
        IsInvincible = false;
    }

    // ─── Public API for HumanoidController ───

    /// <summary>
    /// Is the player in a state that should prevent normal movement?
    /// </summary>
    public bool IsActionLocked()
    {
        return State == CombatState.Dodging ||
               (animancerDriver != null && animancerDriver.IsLocked);
    }

    /// <summary>
    /// Should movement speed be reduced?
    /// </summary>
    public bool IsSlowMovement()
    {
        return State == CombatState.Blocking ||
               State == CombatState.BowAiming ||
               State == CombatState.BowDrawing;
    }

    /// <summary>
    /// Should the character face camera direction?
    /// </summary>
    public bool ShouldFaceCamera()
    {
        return State == CombatState.BowAiming ||
               State == CombatState.BowDrawing ||
               State == CombatState.Blocking;
    }
}