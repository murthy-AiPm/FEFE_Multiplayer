using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Central cross-vital math for the dragon. Server-authoritative.
///
/// Responsibilities:
///  - Stamina drain during firebreath / high-thrust flight (continuous).
///  - Stamina drain on melee impact (one-shot via OnMeleeAttackServer).
///  - Stamina regen with grounded/airborne tier and torso-HP scaling.
///  - Pause head/wings/torso auto-regen during sprint/firebreath/airborne.
///  - Owner-readable gates (CanFireBreath, MaxFlightThrust, WingsBroken, MeleeReducedDamage).
///
/// Reads owner-pushed state via the Net* accessors on DragonAnimatorController and
/// DragonCombatController so we work correctly even when the dragon's owner is a
/// remote client (server still has live state to drive the math).
/// </summary>
public class DragonStaminaController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] private DragonAnimatorController dragonAnimatorController;
    [SerializeField] private DragonCombatController combatController;
    [SerializeField] private AnimalGroundingSystem groundingSystem;

    [Header("Stamina Drain (per second)")]
    [SerializeField] private float fireBreathDrainRate = 80f;
    [SerializeField] private float highThrustDrainRate = 50f;
    [Tooltip("FlightThrust above this drains stamina. Below = free cruise/glide/hover.")]
    [SerializeField] private float highThrustThreshold = 0.7f;
    [Tooltip("Sprint gait threshold (NetGaitSpeed > this = sprinting).")]
    [SerializeField] private float sprintGaitThreshold = 0.9f;

    [Header("Stamina Drain (one-shot)")]
    [SerializeField] private float meleeDrainPerAttack = 50f;

    [Header("Stamina Regen (per second)")]
    [SerializeField] private float regenGroundedIdleRate = 80f;
    [SerializeField] private float regenAirborneLowRate = 30f;
    [Tooltip("Seconds after the most recent stamina drain before regen starts.")]
    [SerializeField] private float regenDelay = 1f;

    [Header("Cross-Vital Curves (threshold + floor)")]
    [Tooltip("Above this normalized HP fraction, no penalty / full regen.")]
    [SerializeField] private float curveThreshold = 0.5f;
    [Tooltip("Multiplier when HP is at zero. e.g. 0.3 = costs scale to ~3.33x / regen scales to 30%.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float curveFloor = 0.3f;

    [Header("Zone HP Regen Gating")]
    [Tooltip("Pause head/wings/torso auto-regen while sprinting / firebreathing / airborne. Recovery requires landing + idle.")]
    [SerializeField] private bool gateZoneRegen = true;

    [Header("Network Throttle")]
    [Tooltip("How often accumulated continuous drain/regen flushes to VitalManager. Lower = smoother UI but more sync traffic.")]
    [SerializeField] private float flushInterval = 0.05f;

    // Cached vitals
    private Vital _stamina, _head, _wings, _torso;

    // Server-side accumulators
    private float _drainAccum;
    private float _regenAccum;
    private float _regenCooldown;
    private float _nextFlushTime;

    // ─── Public Read API (any client) ────────────────────
    public bool CanFireBreath => _stamina == null || !_stamina.IsDepleted;
    public bool MeleeReducedDamage => _stamina != null && _stamina.IsDepleted;
    public bool WingsBroken => _wings != null && _wings.IsDepleted;
    public float MaxFlightThrust => (_stamina != null && _stamina.IsDepleted) ? highThrustThreshold : 1f;
    public float StaminaNormalized => _stamina != null ? _stamina.Normalized : 0f;

    private void Awake()
    {
        if (vitalManager == null) vitalManager = GetComponent<VitalManager>();
        if (dragonAnimatorController == null) dragonAnimatorController = GetComponent<DragonAnimatorController>();
        if (combatController == null) combatController = GetComponent<DragonCombatController>();
        if (groundingSystem == null) groundingSystem = GetComponentInChildren<AnimalGroundingSystem>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CacheVitals();
    }

    private void CacheVitals()
    {
        if (vitalManager == null) return;
        _stamina = vitalManager.GetVital("stamina");
        _head    = vitalManager.GetVital("head");
        _wings   = vitalManager.GetVital("wings");
        _torso   = vitalManager.GetVital("torso");
    }

    private void Update()
    {
        // All math is server-authoritative.
        if (!IsServer || vitalManager == null || vitalManager.IsDead) return;

        // VitalManager.OnNetworkSpawn ordering — vitals may not be cached yet on first frame.
        if (_stamina == null) { CacheVitals(); if (_stamina == null) return; }

        float dt = Time.deltaTime;

        bool grounded     = groundingSystem != null && groundingSystem.IsGrounded;
        bool inFlight     = dragonAnimatorController != null && dragonAnimatorController.NetFlightMode;
        float thrust      = dragonAnimatorController != null ? dragonAnimatorController.NetFlightThrust : 0f;
        bool firebreathing = combatController != null && combatController.NetIsBreathingFire;
        bool sprinting    = dragonAnimatorController != null && dragonAnimatorController.NetGaitSpeed > sprintGaitThreshold;

        // ── 1. Active stamina drain ──
        float drain = 0f;
        if (firebreathing)
            drain += fireBreathDrainRate * CostScale(_head);
        if (inFlight && Mathf.Abs(thrust) > highThrustThreshold)
            drain += highThrustDrainRate * CostScale(_wings);

        if (drain > 0f)
        {
            _drainAccum += drain * dt;
            _regenCooldown = regenDelay;
        }

        // ── 2. Stamina regen (auto-regen disabled on the asset; we drive it here) ──
        if (_regenCooldown > 0f)
        {
            _regenCooldown -= dt;
        }
        else if (!firebreathing && !sprinting && _stamina.Current < _stamina.Max)
        {
            float baseRate;
            if (grounded)
                baseRate = regenGroundedIdleRate;
            else if (Mathf.Abs(thrust) <= highThrustThreshold)
                baseRate = regenAirborneLowRate;
            else
                baseRate = 0f; // airborne high-thrust: no stamina recovery

            if (baseRate > 0f)
                _regenAccum += baseRate * RegenScale(_torso) * dt;
        }

        // ── 3. Flush accumulated drain/regen at throttle interval ──
        if (Time.time >= _nextFlushTime)
        {
            _nextFlushTime = Time.time + flushInterval;
            FlushStamina();
        }

        // ── 4. Zone HP regen gating ──
        if (gateZoneRegen)
        {
            bool zonesLocked = !grounded || sprinting || firebreathing;
            _head?.SetRegenPaused(zonesLocked);
            _wings?.SetRegenPaused(zonesLocked);
            _torso?.SetRegenPaused(zonesLocked);
        }
    }

    private void FlushStamina()
    {
        if (_drainAccum > 0f)
        {
            vitalManager.ApplyDamage("stamina", _drainAccum);
            _drainAccum = 0f;
        }
        if (_regenAccum > 0f)
        {
            vitalManager.RestoreVital("stamina", _regenAccum);
            _regenAccum = 0f;
        }
    }

    /// <summary>Called on the server when a melee attack lands. Drains a flat amount of stamina.</summary>
    public void OnMeleeAttackServer()
    {
        if (!IsServer || vitalManager == null || _stamina == null) return;
        vitalManager.ApplyDamage("stamina", meleeDrainPerAttack);
        _regenCooldown = regenDelay;
    }

    /// <summary>Cost rises as HP falls. Above threshold = 1x; at zero HP = 1/curveFloor.</summary>
    private float CostScale(Vital v)
    {
        if (v == null) return 1f;
        float n = v.Normalized;
        if (n >= curveThreshold) return 1f;
        float t = curveThreshold > 0f ? n / curveThreshold : 0f;
        float multiplier = Mathf.Lerp(curveFloor, 1f, t);
        return multiplier > 0.0001f ? 1f / multiplier : 1f;
    }

    /// <summary>Regen rate falls as HP falls. Above threshold = 1x; at zero HP = curveFloor.</summary>
    private float RegenScale(Vital v)
    {
        if (v == null) return 1f;
        float n = v.Normalized;
        if (n >= curveThreshold) return 1f;
        float t = curveThreshold > 0f ? n / curveThreshold : 0f;
        return Mathf.Lerp(curveFloor, 1f, t);
    }
}
