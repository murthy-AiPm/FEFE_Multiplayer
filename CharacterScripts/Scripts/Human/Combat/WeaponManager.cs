using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages weapon equipping, holstering, swapping, and visual attachment.
/// Owns the weapon slot state machine and syncs equipped weapon across network.
/// 
/// Slot system:
///   Slot 0 = Fists (always available)
///   Slot 1 = Primary melee (1H/2H + optional shield)
///   Slot 2 = Secondary ranged (bow)
///   
/// Controls:
///   Press 1 → toggle primary (holster current → equip primary, or holster primary → fists)
///   Press 2 → toggle secondary (holster current → equip bow, or holster bow → fists)
///   Press H → holster current → fists
/// </summary>
public class WeaponManager : NetworkBehaviour
{
    [Header("Loadout (set at spawn)")]
    [SerializeField] private CombatLoadout loadout;

    [Header("Attach Points (assign on character model)")]
    [SerializeField] private Transform rightHandAttach;
    [SerializeField] private Transform leftHandAttach;
    [SerializeField] private Transform backHolster;    // 2H / bow holster
    [SerializeField] private Transform hipHolster;     // 1H holster
    [SerializeField] private Transform shieldBackHolster; // shield when using bow

    // ─── State ───
    public enum EquipState { Idle, Equipping, Holstering }

    private NetworkVariable<int> _activeSlot = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int ActiveSlot => _activeSlot.Value;
    public EquipState CurrentEquipState { get; private set; } = EquipState.Idle;
    public WeaponData ActiveWeapon => GetWeaponForSlot(ActiveSlot);

    // Instantiated weapon visuals
    private Dictionary<WeaponData, GameObject> _weaponInstances = new Dictionary<WeaponData, GameObject>();
    private GameObject _shieldInstance;

    // Transition tracking
    private int _pendingSlot = -1; // slot we're transitioning to

    // Events — CombatController and Animator listen to these
    public event Action<WeaponData> OnWeaponEquipped;      // new weapon fully equipped
    public event Action<WeaponData> OnWeaponHolstered;     // weapon fully holstered
    public event Action<int, int> OnSlotChanged;           // (newSlot, oldSlot)
    public event Action OnEquipStart;                       // begin equip animation
    public event Action OnHolsterStart;                     // begin holster animation

    // ─── Animator Parameters (set these on your Animator/Animancer) ───
    // CombatController reads ActiveWeapon to determine what clips to play.

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _activeSlot.OnValueChanged += OnActiveSlotChanged;

        if (loadout != null)
            SpawnWeaponVisuals();

        // Start with everything holstered
        HolsterAllVisuals();
    }

    public override void OnNetworkDespawn()
    {
        _activeSlot.OnValueChanged -= OnActiveSlotChanged;
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Initialize loadout at runtime (called by spawn handler after character selection).
    /// </summary>
    public void SetLoadout(CombatLoadout newLoadout)
    {
        loadout = newLoadout;
        if (IsSpawned)
        {
            DestroyWeaponVisuals();
            SpawnWeaponVisuals();
            HolsterAllVisuals();
        }
    }

    // ─── Input Processing (called by CombatController) ───

    /// <summary>
    /// Request to switch to a specific slot. Handles toggle logic.
    /// </summary>
    public void RequestSlotChange(int requestedSlot)
    {
        if (CurrentEquipState != EquipState.Idle) return; // busy transitioning

        int currentSlot = ActiveSlot;

        if (requestedSlot == currentSlot)
        {
            // Toggle off → go to fists
            if (currentSlot != 0)
                BeginTransition(0);
        }
        else
        {
            // Switch to requested slot
            BeginTransition(requestedSlot);
        }
    }

    /// <summary>
    /// Request to holster current weapon → fists.
    /// </summary>
    public void RequestHolster()
    {
        if (CurrentEquipState != EquipState.Idle) return;
        if (ActiveSlot == 0) return; // already fists

        BeginTransition(0);
    }

    // ─── Transition State Machine ───

    private void BeginTransition(int targetSlot)
    {
        _pendingSlot = targetSlot;

        if (ActiveSlot != 0)
        {
            // Currently have a weapon — holster first
            CurrentEquipState = EquipState.Holstering;
            OnHolsterStart?.Invoke();
        }
        else
        {
            // Currently fists — go straight to equip
            if (targetSlot != 0)
            {
                CurrentEquipState = EquipState.Equipping;
                OnEquipStart?.Invoke();
            }
            // else: fists → fists = no-op
        }
    }

    /// <summary>
    /// Called by Animation Event: weapon has been holstered (hand → holster point).
    /// </summary>
    public void OnAnimEvent_HolsterComplete()
    {
        var holsteredWeapon = ActiveWeapon;

        // Move visual to holster point
        AttachWeaponToHolster(holsteredWeapon);
        HandleShieldForSlot(0);

        if (IsOwner)
            SetActiveSlotServerRpc(0);

        OnWeaponHolstered?.Invoke(holsteredWeapon);

        // If we have a pending equip, start it
        if (_pendingSlot > 0)
        {
            CurrentEquipState = EquipState.Equipping;
            OnEquipStart?.Invoke();
        }
        else
        {
            CurrentEquipState = EquipState.Idle;
            _pendingSlot = -1;
        }
    }

    /// <summary>
    /// Called by Animation Event: weapon has been equipped (holster → hand).
    /// </summary>
    public void OnAnimEvent_EquipComplete()
    {
        int slot = _pendingSlot;
        if (slot < 0) slot = 1; // fallback

        var weapon = GetWeaponForSlot(slot);
        AttachWeaponToHand(weapon);
        HandleShieldForSlot(slot);

        if (IsOwner)
            SetActiveSlotServerRpc(slot);

        CurrentEquipState = EquipState.Idle;
        _pendingSlot = -1;

        OnWeaponEquipped?.Invoke(weapon);
    }

    /// <summary>
    /// Quick-set for when you don't have equip/holster animations yet.
    /// Instantly changes slot with no transition.
    /// </summary>
    public void InstantEquip(int slot)
    {
        var oldWeapon = ActiveWeapon;
        if (oldWeapon != null) AttachWeaponToHolster(oldWeapon);

        var newWeapon = GetWeaponForSlot(slot);
        if (newWeapon != null) AttachWeaponToHand(newWeapon);
        HandleShieldForSlot(slot);

        if (IsOwner)
            SetActiveSlotServerRpc(slot);

        CurrentEquipState = EquipState.Idle;
        _pendingSlot = -1;

        if (oldWeapon != null) OnWeaponHolstered?.Invoke(oldWeapon);
        if (newWeapon != null) OnWeaponEquipped?.Invoke(newWeapon);
    }

    // ─── Visual Attachment ───

    private void AttachWeaponToHand(WeaponData weapon)
    {
        if (weapon == null || !_weaponInstances.ContainsKey(weapon)) return;

        var instance = _weaponInstances[weapon];
        Transform hand = (weapon.weaponType == WeaponType.Bow) ? leftHandAttach : rightHandAttach;

        instance.transform.SetParent(hand);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.SetActive(true);
    }

    private void AttachWeaponToHolster(WeaponData weapon)
    {
        if (weapon == null || !_weaponInstances.ContainsKey(weapon)) return;

        var instance = _weaponInstances[weapon];
        Transform holster = GetHolsterPoint(weapon);

        instance.transform.SetParent(holster);
        instance.transform.localPosition = weapon.holsterLocalPos;
        instance.transform.localRotation = Quaternion.Euler(weapon.holsterLocalRot);
        instance.SetActive(true);
    }

    private void HandleShieldForSlot(int slot)
    {
        if (_shieldInstance == null) return;

        if (slot == 1 && loadout.HasShield)
        {
            // Shield in left hand
            _shieldInstance.transform.SetParent(leftHandAttach);
            _shieldInstance.transform.localPosition = Vector3.zero;
            _shieldInstance.transform.localRotation = Quaternion.identity;
            _shieldInstance.SetActive(true);
        }
        else
        {
            // Shield on back
            _shieldInstance.transform.SetParent(shieldBackHolster);
            _shieldInstance.transform.localPosition = Vector3.zero;
            _shieldInstance.transform.localRotation = Quaternion.identity;
            _shieldInstance.SetActive(true);
        }
    }

    private Transform GetHolsterPoint(WeaponData weapon)
    {
        if (weapon == null) return backHolster;

        switch (weapon.weaponType)
        {
            case WeaponType.OneHanded: return hipHolster;
            case WeaponType.TwoHanded: return backHolster;
            case WeaponType.Bow: return backHolster;
            default: return backHolster;
        }
    }

    private void HolsterAllVisuals()
    {
        foreach (var kvp in _weaponInstances)
        {
            if (kvp.Key != null && kvp.Value != null)
                AttachWeaponToHolster(kvp.Key);
        }
        HandleShieldForSlot(0);
    }

    // ─── Spawning / Cleanup ───

    private void SpawnWeaponVisuals()
    {
        if (loadout == null) return;

        SpawnWeaponInstance(loadout.primaryWeapon);
        SpawnWeaponInstance(loadout.secondaryWeapon);

        if (loadout.HasShield && loadout.shield.weaponPrefab != null)
        {
            _shieldInstance = Instantiate(loadout.shield.weaponPrefab);
            _shieldInstance.transform.SetParent(shieldBackHolster);
            _shieldInstance.transform.localPosition = Vector3.zero;
            _shieldInstance.transform.localRotation = Quaternion.identity;
        }
    }

    private void SpawnWeaponInstance(WeaponData weapon)
    {
        if (weapon == null || weapon.weaponPrefab == null) return;
        if (_weaponInstances.ContainsKey(weapon)) return;

        var instance = Instantiate(weapon.weaponPrefab);
        _weaponInstances[weapon] = instance;
    }

    private void DestroyWeaponVisuals()
    {
        foreach (var kvp in _weaponInstances)
        {
            if (kvp.Value != null) Destroy(kvp.Value);
        }
        _weaponInstances.Clear();

        if (_shieldInstance != null)
        {
            Destroy(_shieldInstance);
            _shieldInstance = null;
        }
    }

    // ─── Helpers ───

    public WeaponData GetWeaponForSlot(int slot)
    {
        if (loadout == null) return null;

        switch (slot)
        {
            case 0: return loadout.fistWeapon; // can be null (use default fist behavior)
            case 1: return loadout.primaryWeapon;
            case 2: return loadout.secondaryWeapon;
            default: return null;
        }
    }

    public CombatLoadout GetLoadout() => loadout;

    public WeaponType GetActiveWeaponType()
    {
        var weapon = ActiveWeapon;
        return weapon != null ? weapon.weaponType : WeaponType.None;
    }

    // ─── Network ───

    [ServerRpc]
    private void SetActiveSlotServerRpc(int slot)
    {
        _activeSlot.Value = slot;
    }

    private void OnActiveSlotChanged(int oldSlot, int newSlot)
    {
        OnSlotChanged?.Invoke(newSlot, oldSlot);

        // Non-owner puppets: instantly move visuals to match
        if (!IsOwner)
        {
            var oldWeapon = GetWeaponForSlot(oldSlot);
            var newWeapon = GetWeaponForSlot(newSlot);

            if (oldWeapon != null) AttachWeaponToHolster(oldWeapon);
            if (newWeapon != null) AttachWeaponToHand(newWeapon);
            HandleShieldForSlot(newSlot);
        }
    }
}
