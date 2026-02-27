using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages all vitals (health, stamina, etc.) on a player.
/// Server-authoritative: server ticks regen and applies damage.
/// Clients receive synced values via NetworkVariables.
/// 
/// Usage:
///   var health = vitalManager.GetVital("health");
///   health.Consume(25f);
///   float hpNormalized = health.Normalized; // for UI
/// </summary>
public class VitalManager : NetworkBehaviour
{
    [Header("Vital Definitions")]
    [SerializeField] private VitalDefinition[] vitalDefinitions;

    [Header("Dependencies")]
    [SerializeField] private ThirdPersonController tpsController; // for grounded check

    [Header("Debug (read only)")]
    [SerializeField] private float debugHealth;
    [SerializeField] private float debugStamina;

    // Runtime vitals (server + client)
    private Dictionary<string, Vital> _vitals = new Dictionary<string, Vital>();
    private List<Vital> _vitalsList = new List<Vital>(); // for iteration

    // Network sync — we use a fixed-size array approach.
    // Index matches vitalDefinitions order.
    // We sync current values as a simple NetworkList<float>.
    private NetworkList<float> _syncedValues;

    // Events
    public event Action<string, float, float> OnVitalChanged; // (vitalID, newVal, oldVal)
    public event Action<string> OnVitalDepleted;               // (vitalID)
    public event Action OnDeath;

    private bool _isDead;
    public bool IsDead => _isDead;

    private void Awake()
    {
        _syncedValues = new NetworkList<float>();
    }
    private void LateUpdate()
    {
        var health = GetVital("health");
        if (health != null) debugHealth = health.Current;

        var stamina = GetVital("stamina");
        if (stamina != null) debugStamina = stamina.Current;
    }
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        InitializeVitals();

        if (IsServer)
        {
            // Server populates initial synced values
            for (int i = 0; i < _vitalsList.Count; i++)
                _syncedValues.Add(_vitalsList[i].Current);
        }
        else
        {
            // Client listens for changes
            _syncedValues.OnListChanged += OnSyncedValuesChanged;

            // Apply initial values once the list is populated
            SyncFromNetwork();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
            _syncedValues.OnListChanged -= OnSyncedValuesChanged;

        base.OnNetworkDespawn();
    }

    private void InitializeVitals()
    {
        _vitals.Clear();
        _vitalsList.Clear();

        foreach (var def in vitalDefinitions)
        {
            if (def == null) continue;

            var vital = new Vital(def);
            _vitals[def.vitalID] = vital;
            _vitalsList.Add(vital);

            // Wire events
            string id = def.vitalID;
            vital.OnValueChanged += (newVal, oldVal) => OnVitalChanged?.Invoke(id, newVal, oldVal);
            vital.OnDepleted += () =>
            {
                OnVitalDepleted?.Invoke(id);
                if (def.killOnDepleted && !_isDead)
                {
                    _isDead = true;
                    OnDeath?.Invoke();
                }
            };
        }
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;

        bool isGrounded = tpsController != null && tpsController.isgrounded;

        // Tick regen on server
        for (int i = 0; i < _vitalsList.Count; i++)
        {
            float before = _vitalsList[i].Current;
            _vitalsList[i].TickRegen(Time.deltaTime, isGrounded);

            // Sync to network if changed
            if (Mathf.Abs(_vitalsList[i].Current - before) > 0.01f)
                _syncedValues[i] = _vitalsList[i].Current;
        }
    }

    // ─── Public API (call on server) ───

    public Vital GetVital(string vitalID)
    {
        _vitals.TryGetValue(vitalID, out var vital);
        return vital;
    }

    /// <summary>
    /// Apply damage to a vital. Server only.
    /// </summary>
    public float ApplyDamage(string vitalID, float amount)
    {
        if (!IsServer) return 0f;

        var vital = GetVital(vitalID);
        if (vital == null) return 0f;

        float consumed = vital.Consume(amount);
        int index = _vitalsList.IndexOf(vital);
        if (index >= 0 && index < _syncedValues.Count)
            _syncedValues[index] = vital.Current;

        return consumed;
    }

    /// <summary>
    /// Restore a vital (healing, food, etc.). Server only.
    /// </summary>
    public float RestoreVital(string vitalID, float amount)
    {
        if (!IsServer) return 0f;

        var vital = GetVital(vitalID);
        if (vital == null) return 0f;

        float restored = vital.Restore(amount);
        int index = _vitalsList.IndexOf(vital);
        if (index >= 0 && index < _syncedValues.Count)
            _syncedValues[index] = vital.Current;

        return restored;
    }

    /// <summary>
    /// Consume stamina (attacks, dodge, sprint). Server only.
    /// Returns true if there was enough stamina.
    /// </summary>
    public bool TryConsumeStamina(float amount)
    {
        var stamina = GetVital("stamina");
        if (stamina == null) return true; // no stamina vital = always succeed

        if (stamina.Current < amount) return false;

        stamina.Consume(amount);
        int index = _vitalsList.IndexOf(stamina);
        if (index >= 0 && index < _syncedValues.Count)
            _syncedValues[index] = stamina.Current;

        return true;
    }

    /// <summary>
    /// Pause/resume stamina regen (e.g. while sprinting).
    /// </summary>
    public void SetStaminaRegenPaused(bool paused)
    {
        var stamina = GetVital("stamina");
        stamina?.SetRegenPaused(paused);
    }

    /// <summary>
    /// Respawn: reset all vitals to start values. Server only.
    /// </summary>
    public void ResetAllVitals()
    {
        if (!IsServer) return;

        _isDead = false;
        for (int i = 0; i < _vitalsList.Count; i++)
        {
            _vitalsList[i].SetCurrent(_vitalsList[i].Definition.GetStartValue());
            _syncedValues[i] = _vitalsList[i].Current;
        }
    }

    // ─── Network Sync ───

    private void OnSyncedValuesChanged(NetworkListEvent<float> changeEvent)
    {
        SyncFromNetwork();
    }

    private void SyncFromNetwork()
    {
        for (int i = 0; i < _vitalsList.Count && i < _syncedValues.Count; i++)
        {
            _vitalsList[i].SetCurrent(_syncedValues[i]);
        }
    }
}
