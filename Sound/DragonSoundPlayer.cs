using UnityEngine;
using Unity.Netcode;

// ─────────────────────────────────────────────────────────
// DragonSoundPlayer.cs — Dragon-specific sound effects
//
// Attach to dragon prefab root (same GameObject as Animator).
//
// Sound triggering:
//   - Wing flaps, footsteps, etc. → Animation events call PlaySound(string)
//     (Function: PlaySound, String: e.g. "WingFlap", "DragonWalk")
//   - Fire breath, melee, roar, etc. → Called from ability scripts
//   - Landing/takeoff → Auto-detected via flight state transitions
// ─────────────────────────────────────────────────────────

public class DragonSoundPlayer : NetworkBehaviour
{
    [Header("References (auto-found if null)")]
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private AnimalGroundController groundController;

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

    [Header("Wing Flap Motion Gate")]
    [Tooltip("Wing bone to track for flap motion detection. Drag a wing bone here — ideally near the shoulder where flap rotation is largest.")]
    [SerializeField] private Transform wingBone;
    [Tooltip("Sound name that will be gated by wing motion. Must match the string used in animation events.")]
    [SerializeField] private string wingFlapSoundName = "WingFlap";
    [Tooltip("Minimum smoothed wing angular velocity (deg/sec) to allow wing flap sounds. 80 works for the default dragon rig (glide peaks at ~30, flap bottoms at ~160).")]
    [SerializeField] private float wingMotionThreshold = 80f;
    [Tooltip("Smoothing factor for wing motion. Higher = more responsive to sudden motion changes, lower = smoother average.")]
    [SerializeField] private float wingMotionSmoothing = 8f;

    // ─── State ───
    private bool _wasFlying;
    private bool _isBreathingFire;
    private AudioSource _fireBreathLoopSource; // persistent source for looping fire breath
    private Quaternion _lastWingRotation;
    private float _wingActivity; // smoothed angular velocity in deg/sec

    private void Awake()
    {
        if (flightController == null) flightController = GetComponent<DragonFlightController>();
        if (groundController == null) groundController = GetComponent<AnimalGroundController>();
        if (wingBone != null) _lastWingRotation = wingBone.localRotation;
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (ProximitySoundManager.Instance == null) return;

        UpdateFlightStateTransitions();
    }

    // ─── Wing Motion Tracking ───

    /// <summary>
    /// Runs on ALL clients (not just owner) so gating works for remote dragons too.
    /// LateUpdate so we read the wing bone AFTER the animator has written to it.
    /// </summary>
    private void LateUpdate()
    {
        if (wingBone == null) return;

        Quaternion current = wingBone.localRotation;
        float deltaAngle = Quaternion.Angle(_lastWingRotation, current);
        float angularVelocity = deltaAngle / Mathf.Max(Time.deltaTime, 0.0001f);

        // Low-pass filter the angular velocity so single-frame dips at flap apex don't kill the sound
        _wingActivity = Mathf.Lerp(_wingActivity, angularVelocity, wingMotionSmoothing * Time.deltaTime);
        _lastWingRotation = current;
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

    // ─── Animation Event Handler ───

    /// <summary>
    /// Called by animation events. Add an event in the animation clip with:
    ///   Function: PlaySound
    ///   String:   sound name matching a SoundDatabase entry (e.g. "WingFlap", "DragonWalk")
    ///
    /// Wing flap sounds are gated by actual wing bone motion — if the wings aren't
    /// moving (gliding, descending without flap), the sound is suppressed even if
    /// the animation event fires during a crossfade.
    /// </summary>
    public void PlaySound(string soundName)
    {
        if (string.IsNullOrEmpty(soundName)) return;
        if (ProximitySoundManager.Instance == null) return;

        // Motion gate for wing flap sounds — ignore events during glide / descent
        if (soundName == wingFlapSoundName && wingBone != null && _wingActivity < wingMotionThreshold)
            return;

        ProximitySoundManager.Instance.PlaySound(soundName, transform.position);
    }

    // ─── Helpers ───

    private bool IsFlying()
    {
        if (flightController == null) return false;
        return flightController.IsFlightMode;
    }
}
