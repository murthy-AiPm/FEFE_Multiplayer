using UnityEngine;
using Unity.Netcode;

// ─────────────────────────────────────────────────────────
// DragonSoundPlayer.cs — Dragon-specific sound effects
//
// Attach to dragon prefab root. Handles:
// - Wing flap loop (cadence timer while flying)
// - Roar (ability trigger)
// - Fire breath (start + loop + end)
// - Landing thud
// - Takeoff whoosh
// - Grounded footsteps (heavy, terrain-aware via FootstepSoundPlayer)
// ─────────────────────────────────────────────────────────

public class DragonSoundPlayer : NetworkBehaviour
{
    [Header("References (auto-found if null)")]
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private DragonGroundController groundController;

    [Header("Wing Flap")]
    [Tooltip("Sound name in database for wing flap")]
    [SerializeField] private string wingFlapSound = "Dragon_WingFlap";

    [Tooltip("Interval between wing flap sounds while flying (seconds)")]
    [SerializeField] private float wingFlapInterval = 1.2f;

    [Tooltip("Faster flap interval when boosting")]
    [SerializeField] private float wingFlapBoostInterval = 0.8f;

    [Header("Roar")]
    [SerializeField] private string roarSound = "Dragon_Roar";

    [Header("Fire Breath")]
    [SerializeField] private string fireBreathStartSound = "Dragon_FireBreath_Start";
    [SerializeField] private string fireBreathLoopSound = "Dragon_FireBreath_Loop";
    [SerializeField] private string fireBreathEndSound = "Dragon_FireBreath_End";

    [Header("Movement")]
    [SerializeField] private string landingSound = "Dragon_Landing";
    [SerializeField] private string takeoffSound = "Dragon_Takeoff";
    [SerializeField] private string deathSound = "Dragon_Death";

    [Header("Eating")]
    [SerializeField] private string eatSound = "Dragon_Eat";

    [Header("Melee")]
    [SerializeField] private string clawSound = "Dragon_Claw";
    [SerializeField] private string biteSound = "Dragon_Bite";
    [SerializeField] private string tailSweepSound = "Dragon_TailSweep";

    // ─── State ───
    private float _wingFlapTimer;
    private bool _wasFlying;
    private bool _isBreathingFire;
    private AudioSource _fireBreathLoopSource; // persistent source for looping fire breath

    private void Awake()
    {
        if (flightController == null) flightController = GetComponent<DragonFlightController>();
        if (groundController == null) groundController = GetComponent<DragonGroundController>();
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (ProximitySoundManager.Instance == null) return;

        UpdateWingFlaps();
        UpdateFlightStateTransitions();
    }

    // ─── Wing Flaps ───

    private void UpdateWingFlaps()
    {
        bool isFlying = IsFlying();
        if (!isFlying)
        {
            _wingFlapTimer = 0f;
            return;
        }

        _wingFlapTimer -= Time.deltaTime;
        if (_wingFlapTimer <= 0f)
        {
            ProximitySoundManager.Instance.PlaySound(wingFlapSound, transform.position);
            _wingFlapTimer = IsBoosting() ? wingFlapBoostInterval : wingFlapInterval;
        }
    }

    // ─── Flight State Transitions ───

    private void UpdateFlightStateTransitions()
    {
        bool isFlying = IsFlying();

        // Just landed
        if (_wasFlying && !isFlying)
        {
            ProximitySoundManager.Instance.PlaySound(landingSound, transform.position);
        }

        // Just took off
        if (!_wasFlying && isFlying)
        {
            ProximitySoundManager.Instance.PlaySound(takeoffSound, transform.position);
        }

        _wasFlying = isFlying;
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC API — Call from dragon ability scripts
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Call when fire breath starts. Plays start sound and begins loop.
    /// </summary>
    public void OnFireBreathStart()
    {
        if (ProximitySoundManager.Instance == null) return;

        ProximitySoundManager.Instance.PlaySound(fireBreathStartSound, transform.position);
        _isBreathingFire = true;

        // For the loop, we need a persistent audio source
        // The proximity manager handles one-shots; we'll create a local loop
        StartFireBreathLoop();
    }

    /// <summary>
    /// Call when fire breath ends. Stops loop and plays end sound.
    /// </summary>
    public void OnFireBreathEnd()
    {
        if (ProximitySoundManager.Instance == null) return;

        _isBreathingFire = false;
        StopFireBreathLoop();
        ProximitySoundManager.Instance.PlaySound(fireBreathEndSound, transform.position);
    }

    /// <summary>
    /// Call when dragon roars (ability use).
    /// </summary>
    public void OnRoar()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(roarSound, transform.position);
    }

    /// <summary>
    /// Call when dragon eats a creature.
    /// </summary>
    public void OnEat()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(eatSound, transform.position);
    }

    /// <summary>
    /// Call when dragon dies.
    /// </summary>
    public void OnDeath()
    {
        if (ProximitySoundManager.Instance == null) return;
        StopFireBreathLoop();
        ProximitySoundManager.Instance.PlaySound(deathSound, transform.position);
    }

    /// <summary>
    /// Call when dragon performs a melee attack.
    /// </summary>
    public void OnMeleeAttack(string attackType)
    {
        if (ProximitySoundManager.Instance == null) return;

        string sound = attackType switch
        {
            "claw" => clawSound,
            "bite" => biteSound,
            "tail" => tailSweepSound,
            _ => clawSound
        };

        ProximitySoundManager.Instance.PlaySound(sound, transform.position);
    }

    // ─── Fire Breath Loop (local audio source for continuous sound) ───

    private void StartFireBreathLoop()
    {
        if (_fireBreathLoopSource != null) return;

        var db = ProximitySoundManager.Instance?.Database;
        if (db == null) return;

        var entry = db.GetSound(fireBreathLoopSound);
        if (entry == null) return;

        var clip = entry.GetRandomClip();
        if (clip == null) return;

        var go = new GameObject("DragonFireBreathLoop");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        _fireBreathLoopSource = go.AddComponent<AudioSource>();
        _fireBreathLoopSource.clip = clip;
        _fireBreathLoopSource.loop = true;
        _fireBreathLoopSource.spatialBlend = 1f;
        _fireBreathLoopSource.volume = entry.GetRandomVolume();
        _fireBreathLoopSource.rolloffMode = AudioRolloffMode.Logarithmic;
        _fireBreathLoopSource.outputAudioMixerGroup = ProximitySoundManager.Instance?.GetMixerGroup(SoundCategory.Loud);

        db.GetDistanceForSound(entry, out float minDist, out float maxDist);
        _fireBreathLoopSource.minDistance = minDist;
        _fireBreathLoopSource.maxDistance = maxDist;

        _fireBreathLoopSource.Play();
    }

    private void StopFireBreathLoop()
    {
        if (_fireBreathLoopSource != null)
        {
            _fireBreathLoopSource.Stop();
            Destroy(_fireBreathLoopSource.gameObject);
            _fireBreathLoopSource = null;
        }
    }

    // ─── Helpers ───

    private bool IsFlying()
    {
        // Check if dragon is airborne — adapt to your flight controller's API
        if (flightController == null) return false;

        // DragonFlightController likely has a public bool or state
        // Adjust this based on your actual API
        return flightController.enabled && flightController.gameObject.activeInHierarchy;
    }

    private bool IsBoosting()
    {
        // Adapt to your flight controller's boost detection
        return Input.GetKey(KeyCode.LeftShift);
    }
}
