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
    [Tooltip("Seconds before current loop clip ends to start the next random clip. The incoming clip's start masks the outgoing clip's quiet tail so chained non-seamless clips sound continuous.")]
    [SerializeField] private float fireBreathLoopOverlap = 0.5f;
    [Tooltip("Max seconds to play any single fire breath loop clip. Caps long clips with trailing silence/fade so crossfade triggers on audible content, not on the silent tail. Set to a very large value (e.g. 999) to disable capping and use full clip length.")]
    [SerializeField] private float fireBreathLoopMaxClipDuration = 3f;

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
    // Two AudioSources alternate — when one nears end of clip, the other starts the next random clip.
    // Overlap masks the quiet tails of non-seamless fire breath clips.
    private GameObject _fireBreathSourceHolder;
    private AudioSource _fireBreathSourceA;
    private AudioSource _fireBreathSourceB;
    private AudioSource _activeFireBreathSource;
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

        if (_isBreathingFire)
            UpdateFireBreathCrossfade();
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

    // ─── Fire Breath Loop (two AudioSources alternating with overlap) ───

    private void StartFireBreathLoop()
    {
        if (_fireBreathSourceHolder != null) return;

        var db = ProximitySoundManager.Instance?.Database;
        if (db == null) return;

        var entry = db.GetSound(fireBreathLoopSound);
        if (entry == null) return;

        _fireBreathSourceHolder = new GameObject("DragonFireBreathLoop");
        _fireBreathSourceHolder.transform.SetParent(transform);
        _fireBreathSourceHolder.transform.localPosition = Vector3.zero;

        _fireBreathSourceA = CreateFireBreathSource(db, entry);
        _fireBreathSourceB = CreateFireBreathSource(db, entry);

        // Play first random clip on A
        PlayRandomFireBreathClip(entry, _fireBreathSourceA);
        _activeFireBreathSource = _fireBreathSourceA;
    }

    private AudioSource CreateFireBreathSource(SoundDatabase db, SoundEntry entry)
    {
        var source = _fireBreathSourceHolder.AddComponent<AudioSource>();
        source.loop = false; // not looped — we chain clips manually for overlap
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.outputAudioMixerGroup = ProximitySoundManager.Instance?.GetMixerGroup(SoundCategory.Loud);
        db.GetDistanceForSound(entry, out float minDist, out float maxDist);
        source.minDistance = minDist;
        source.maxDistance = maxDist;
        return source;
    }

    private void PlayRandomFireBreathClip(SoundEntry entry, AudioSource source)
    {
        var clip = entry.GetRandomClip();
        if (clip == null) return;
        source.clip = clip;
        source.volume = entry.GetRandomVolume();
        source.Play();
    }

    /// <summary>
    /// Called each frame while fire breath is active. When the active source has fewer
    /// than fireBreathLoopOverlap seconds of clip left, start the next random clip on
    /// the other source so the incoming sound masks the outgoing clip's tail.
    /// </summary>
    private void UpdateFireBreathCrossfade()
    {
        if (_activeFireBreathSource == null) return;
        if (_activeFireBreathSource.clip == null) return;

        // If active somehow stopped (clip ended without overlap kicking in), restart on it immediately
        if (!_activeFireBreathSource.isPlaying)
        {
            var dbNow = ProximitySoundManager.Instance?.Database;
            var entryNow = dbNow?.GetSound(fireBreathLoopSound);
            if (entryNow != null)
                PlayRandomFireBreathClip(entryNow, _activeFireBreathSource);
            return;
        }

        // Cap effective clip length so trailing silence/fade in long clips never plays.
        float activeEffective = Mathf.Min(_activeFireBreathSource.clip.length, fireBreathLoopMaxClipDuration);
        float remaining = activeEffective - _activeFireBreathSource.time;
        if (remaining > fireBreathLoopOverlap) return;

        // Time to start the next clip on the other source
        var other = (_activeFireBreathSource == _fireBreathSourceA) ? _fireBreathSourceB : _fireBreathSourceA;
        if (other == null) return;

        // If the other source is still running the silent tail of its previous clip
        // (past the effective cap), force-stop so we can start the new clip cleanly.
        if (other.isPlaying && other.clip != null)
        {
            float otherEffective = Mathf.Min(other.clip.length, fireBreathLoopMaxClipDuration);
            if (other.time >= otherEffective)
                other.Stop();
        }

        if (other.isPlaying) return; // next clip already queued and still within its audible window

        var db = ProximitySoundManager.Instance?.Database;
        var entry = db?.GetSound(fireBreathLoopSound);
        if (entry == null) return;

        PlayRandomFireBreathClip(entry, other);
        _activeFireBreathSource = other;
    }

    private void StopFireBreathLoop()
    {
        if (_fireBreathSourceHolder != null)
        {
            Destroy(_fireBreathSourceHolder);
            _fireBreathSourceHolder = null;
            _fireBreathSourceA = null;
            _fireBreathSourceB = null;
            _activeFireBreathSource = null;
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
