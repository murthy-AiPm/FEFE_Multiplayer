using System;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

/// <summary>
/// Drives 2D Cartesian blend trees for combat locomotion (walk + run).
/// Supports multiple weapon profiles (sword, bow, etc.) each with their own clip keys.
/// Network-friendly: sync a single Vector2 for the blend parameter.
/// </summary>
public class CombatLocomotionMixer : MonoBehaviour
{
    [Header("Animation Set (same one the driver uses)")]
    [SerializeField] private AnimationSetBase animationSet;

    [Header("Weapon Locomotion Profiles")]
    [SerializeField] private List<WeaponLocomotionProfile> weaponProfiles = new List<WeaponLocomotionProfile>();

    [Header("Blend Settings")]
    [SerializeField] private float fadeDuration = 0.15f;
    [Tooltip("How fast the blend parameter tracks input (lower = smoother).")]
    [SerializeField] private float parameterSmoothTime = 0.1f;

    // ── Runtime ──
    private Dictionary<int, MixerPair> _mixersBySlot;
    private bool _initialized;

    // Smoothing
    private Vector2 _smoothParam;
    private Vector2 _paramVelocity;

    // Track which mixer is currently active so we don't re-play it
    private CartesianMixerState _activeMixer;

    /// <summary>
    /// The smoothed blend parameter. Network sync reads this on the owner
    /// and writes it on remote clients.
    /// </summary>
    public Vector2 BlendParameter => _smoothParam;

    // ───────────────────── Data ─────────────────────

    [Serializable]
    public class WeaponLocomotionProfile
    {
        [Tooltip("Which weapon slot this profile applies to (1 = sword, 2 = bow).")]
        public int weaponSlot = 1;

        [Header("Walk Clip Keys (8 directions — leave empty to skip diagonals)")]
        public string walkFwd       = "Sword/Walk/Fwd";
        public string walkBack      = "Sword/Strafe/Back";
        public string walkLeft      = "Sword/Strafe/Left";
        public string walkRight     = "Sword/Strafe/Right";
        public string walkFwdLeft   = "Sword/Strafe/Front/Left";
        public string walkFwdRight  = "Sword/Strafe/Front/Right";
        public string walkBackLeft  = "Sword/Strafe/Back/Left";
        public string walkBackRight = "Sword/Strafe/Back/Right";

        [Header("Run Clip Keys (8 directions — leave empty to skip diagonals)")]
        public string runFwd       = "Sword/Run";
        public string runBack      = "Sword/Run/Back";
        public string runLeft      = "Sword/Run/Left";
        public string runRight     = "Sword/Run/Right";
        public string runFwdLeft   = "";
        public string runFwdRight  = "";
        public string runBackLeft  = "Sword/Run/BackLeft";
        public string runBackRight = "";
    }

    private class MixerPair
    {
        public CartesianMixerState walk;
        public CartesianMixerState run;
    }

    // ───────────────────── Init ─────────────────────

    /// <summary>
    /// Must be called after AnimancerComponent is available (e.g. from the driver's Awake).
    /// Builds mixers for each weapon profile.
    /// </summary>
    public void Initialize(AnimancerComponent animancer)
    {
        if (_initialized) return;
        if (animationSet == null)
        {
            Debug.LogError("[CombatLocomotionMixer] No AnimationSetBase assigned.");
            return;
        }

        _mixersBySlot = new Dictionary<int, MixerPair>();

        foreach (var profile in weaponProfiles)
        {
            var pair = new MixerPair
            {
                walk = BuildMixer(profile.walkFwd, profile.walkBack,
                    profile.walkLeft, profile.walkRight,
                    profile.walkFwdLeft, profile.walkFwdRight,
                    profile.walkBackLeft, profile.walkBackRight),

                run = BuildMixer(profile.runFwd, profile.runBack,
                    profile.runLeft, profile.runRight,
                    profile.runFwdLeft, profile.runFwdRight,
                    profile.runBackLeft, profile.runBackRight),
            };

            if (pair.walk != null || pair.run != null)
            {
                _mixersBySlot[profile.weaponSlot] = pair;
                Debug.Log($"[CombatLocomotionMixer] Built mixer for slot {profile.weaponSlot}" +
                          $" (walk: {(pair.walk != null ? "OK" : "NONE")}, run: {(pair.run != null ? "OK" : "NONE")})");
            }
        }

        _initialized = _mixersBySlot.Count > 0;
    }

    private CartesianMixerState BuildMixer(
        string fwd, string back, string left, string right,
        string fwdLeft, string fwdRight, string backLeft, string backRight)
    {
        // Build list of valid clips — skip empty keys gracefully
        var entries = new List<(AnimationClip clip, Vector2 pos)>();

        TryAddClip(entries, fwd,       new Vector2( 0,  1));
        TryAddClip(entries, back,      new Vector2( 0, -1));
        TryAddClip(entries, left,      new Vector2(-1,  0));
        TryAddClip(entries, right,     new Vector2( 1,  0));
        TryAddClip(entries, fwdLeft,   new Vector2(-1,  1).normalized);
        TryAddClip(entries, fwdRight,  new Vector2( 1,  1).normalized);
        TryAddClip(entries, backLeft,  new Vector2(-1, -1).normalized);
        TryAddClip(entries, backRight, new Vector2( 1, -1).normalized);

        // Need at least 4 cardinal directions for a usable mixer
        if (entries.Count < 4)
        {
            Debug.LogWarning($"[CombatLocomotionMixer] Only {entries.Count} clips found, " +
                             $"need at least 4. Mixer will not be created.");
            return null;
        }

        var mixer = new CartesianMixerState();
        foreach (var (clip, pos) in entries)
        {
            mixer.Add(clip, pos);
        }
        return mixer;
    }

    private void TryAddClip(List<(AnimationClip, Vector2)> entries, string key, Vector2 pos)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (animationSet.TryGet(key, out var transition) &&
            transition != null && transition.Clip != null)
        {
            entries.Add((transition.Clip, pos));
        }
        else
        {
            Debug.LogWarning($"[CombatLocomotionMixer] Clip key '{key}' not found — skipping.");
        }
    }

    // ───────────────────── Public API ─────────────────────

    /// <summary>
    /// Returns true if this mixer is ready and should drive the base layer.
    /// </summary>
    public bool WantsControl(int activeWeaponSlot, bool isMoving, bool isDodging,
                             bool isBlocking, bool isBowDrawing, bool isBowAiming,
                             bool isMounted)
    {
        if (!_initialized) return false;
        if (!isMoving) return false;
        if (isDodging || isBlocking || isBowDrawing || isBowAiming) return false;
        if (isMounted) return false;

        // Check if we have a mixer for this weapon slot
        return _mixersBySlot.ContainsKey(activeWeaponSlot);
    }

    /// <summary>
    /// Plays the appropriate mixer (walk or run) on the given layer.
    /// Call every frame when WantsControl() is true.
    /// </summary>
    public void UpdateAndPlay(AnimancerLayer layer, Vector2 moveInput,
                              bool isSprinting, int activeWeaponSlot)
    {
        if (!_mixersBySlot.TryGetValue(activeWeaponSlot, out var pair))
            return;

        // Smooth the blend parameter
        _smoothParam = Vector2.SmoothDamp(_smoothParam, moveInput,
            ref _paramVelocity, parameterSmoothTime);

        // Pick the right mixer (run if sprinting and run mixer exists, else walk)
        var target = isSprinting && pair.run != null ? pair.run : pair.walk;
        if (target == null) return;

        // Update the mixer parameter
        target.Parameter = _smoothParam;

        // Play on layer if not already active
        if (_activeMixer != target)
        {
            layer.Play(target, fadeDuration);
            _activeMixer = target;
        }
    }

    /// <summary>
    /// Called by network sync on remote clients to directly set the blend parameter.
    /// </summary>
    public void SetRemoteParameter(Vector2 param)
    {
        _smoothParam = param;
        _paramVelocity = Vector2.zero;
    }

    /// <summary>
    /// Resets tracking so the next UpdateAndPlay will fade-in fresh.
    /// </summary>
    public void ResetActiveState()
    {
        _activeMixer = null;
        _smoothParam = Vector2.zero;
        _paramVelocity = Vector2.zero;
    }
}
