using System;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

/// <summary>
/// Drives 2D Cartesian blend trees for combat locomotion, dodge rolls, and dodge steps.
///
/// Two profile shapes:
///   - WeaponLocomotionProfile (sword/fists/etc.): one 8-direction walk mixer per slot.
///     Sprint is not handled by the mixer — the rule system plays a forward run.
///   - BowLocomotionProfile (slot 2): one 8-direction "no-aim" mixer and one "aim" mixer.
///     Selection switches on bowDrawing/bowAiming. Sprint with bow also falls through
///     to the rule system (same behavior as sword + sprint).
///
/// Network-friendly: sync a single Vector2 for the blend parameter.
/// </summary>
public class CombatLocomotionMixer : MonoBehaviour
{
    [Header("Animation Set (same one the driver uses)")]
    [SerializeField] private AnimationSetBase animationSet;

    [Header("Weapon Locomotion Profiles (non-bow)")]
    [Tooltip("One profile per non-bow weapon slot (1 = sword, 0 = fists, etc.). " +
             "Slot 2 (bow) uses BowLocomotionProfile below.")]
    [SerializeField] private List<WeaponLocomotionProfile> weaponProfiles = new List<WeaponLocomotionProfile>();

    [Header("Bow Locomotion Profile (slot 2)")]
    [SerializeField] private BowLocomotionProfile bowProfile = new BowLocomotionProfile();

    [Header("Dodge Clip Keys (4 cardinal directions)")]
    [SerializeField] private string dodgeFwd   = "Dodge/Front";
    [SerializeField] private string dodgeBack  = "Dodge/Back";
    [SerializeField] private string dodgeLeft  = "Dodge/Left";
    [SerializeField] private string dodgeRight = "Dodge/Right";

    [Header("Dodge Step Clip Keys (4 cardinal directions)")]
    [SerializeField] private string dodgeStepFwd   = "DodgeStep/Front";
    [SerializeField] private string dodgeStepBack  = "DodgeStep/Back";
    [SerializeField] private string dodgeStepLeft  = "DodgeStep/Left";
    [SerializeField] private string dodgeStepRight = "DodgeStep/Right";

    [Header("Blend Settings")]
    [SerializeField] private float fadeDuration = 0.15f;
    [Tooltip("How fast the blend parameter tracks input (lower = smoother).")]
    [SerializeField] private float parameterSmoothTime = 0.1f;
    [SerializeField] private float dodgeFadeDuration = 0.08f;

    // ── Runtime ──
    private Dictionary<int, CartesianMixerState> _mixersBySlot;
    private CartesianMixerState _bowNoAimMixer;
    private CartesianMixerState _bowAimMixer;
    private CartesianMixerState _dodgeMixer;
    private CartesianMixerState _dodgeStepMixer;
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

    /// <summary>True if dodge/dodge step mixers were built successfully.</summary>
    public bool HasDodgeMixer => _dodgeMixer != null;
    public bool HasDodgeStepMixer => _dodgeStepMixer != null;

    // ───────────────────── Data ─────────────────────

    [Serializable]
    public class WeaponLocomotionProfile
    {
        [Tooltip("Which weapon slot this profile applies to (1 = sword, 0 = fists, etc.). " +
                 "Slot 2 (bow) uses BowLocomotionProfile.")]
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
    }

    [Serializable]
    public class BowLocomotionProfile
    {
        [Header("No-Aim Clip Keys (bow held, not drawn) — 8 directions")]
        public string noAimFwd       = "Bow/Walk/Front";
        public string noAimBack      = "Bow/Walk/Back";
        public string noAimLeft      = "Bow/Walk/Left";
        public string noAimRight     = "Bow/Walk/Right";
        public string noAimFwdLeft   = "";
        public string noAimFwdRight  = "";
        public string noAimBackLeft  = "";
        public string noAimBackRight = "";

        [Header("Aim Clip Keys (drawing or aiming) — 8 directions")]
        public string aimFwd       = "Bow/Aim/Walk/Front";
        public string aimBack      = "Bow/Aim/Walk/Back";
        public string aimLeft      = "Bow/Aim/Walk/Left";
        public string aimRight     = "Bow/Aim/Walk/Right";
        public string aimFwdLeft   = "";
        public string aimFwdRight  = "";
        public string aimBackLeft  = "";
        public string aimBackRight = "";
    }

    // ───────────────────── Init ─────────────────────

    /// <summary>
    /// Must be called after AnimancerComponent is available (e.g. from the driver's Awake).
    /// Builds mixers for each weapon profile, the bow profile, and dodge/dodge step mixers.
    /// </summary>
    public void Initialize(AnimancerComponent animancer)
    {
        if (_initialized) return;
        if (animationSet == null)
        {
            Debug.LogError("[CombatLocomotionMixer] No AnimationSetBase assigned.");
            return;
        }

        // Build non-bow weapon mixers (one walk mixer per slot)
        _mixersBySlot = new Dictionary<int, CartesianMixerState>();

        foreach (var profile in weaponProfiles)
        {
            if (profile.weaponSlot == 2)
            {
                Debug.LogWarning("[CombatLocomotionMixer] Slot 2 should use BowLocomotionProfile, " +
                                 "not WeaponLocomotionProfile — entry skipped.");
                continue;
            }

            var walk = BuildMixer(profile.walkFwd, profile.walkBack,
                profile.walkLeft, profile.walkRight,
                profile.walkFwdLeft, profile.walkFwdRight,
                profile.walkBackLeft, profile.walkBackRight);

            if (walk != null)
            {
                _mixersBySlot[profile.weaponSlot] = walk;
                Debug.Log($"[CombatLocomotionMixer] Built walk mixer for slot {profile.weaponSlot}");
            }
        }

        // Build bow mixers (no-aim and aim variants)
        if (bowProfile != null)
        {
            _bowNoAimMixer = BuildMixer(bowProfile.noAimFwd, bowProfile.noAimBack,
                bowProfile.noAimLeft, bowProfile.noAimRight,
                bowProfile.noAimFwdLeft, bowProfile.noAimFwdRight,
                bowProfile.noAimBackLeft, bowProfile.noAimBackRight);

            _bowAimMixer = BuildMixer(bowProfile.aimFwd, bowProfile.aimBack,
                bowProfile.aimLeft, bowProfile.aimRight,
                bowProfile.aimFwdLeft, bowProfile.aimFwdRight,
                bowProfile.aimBackLeft, bowProfile.aimBackRight);

            if (_bowNoAimMixer != null || _bowAimMixer != null)
                Debug.Log($"[CombatLocomotionMixer] Built bow mixers " +
                          $"(noAim: {(_bowNoAimMixer != null ? "OK" : "NONE")}, " +
                          $"aim: {(_bowAimMixer != null ? "OK" : "NONE")})");
        }

        // Build dodge mixers (4 cardinal directions each)
        _dodgeMixer = BuildCardinalMixer(dodgeFwd, dodgeBack, dodgeLeft, dodgeRight, "Dodge");
        _dodgeStepMixer = BuildCardinalMixer(dodgeStepFwd, dodgeStepBack, dodgeStepLeft, dodgeStepRight, "DodgeStep");

        _initialized = _mixersBySlot.Count > 0
            || _bowNoAimMixer != null || _bowAimMixer != null
            || _dodgeMixer != null || _dodgeStepMixer != null;
    }

    private CartesianMixerState BuildCardinalMixer(string fwd, string back, string left, string right, string label)
    {
        return BuildMixer(fwd, back, left, right, "", "", "", "");
    }

    private CartesianMixerState BuildMixer(
        string fwd, string back, string left, string right,
        string fwdLeft, string fwdRight, string backLeft, string backRight)
    {
        var entries = new List<(AnimationClip clip, Vector2 pos, float speed)>();

        TryAddClip(entries, fwd,       new Vector2( 0,  1));
        TryAddClip(entries, back,      new Vector2( 0, -1));
        TryAddClip(entries, left,      new Vector2(-1,  0));
        TryAddClip(entries, right,     new Vector2( 1,  0));
        TryAddClip(entries, fwdLeft,   new Vector2(-1,  1).normalized);
        TryAddClip(entries, fwdRight,  new Vector2( 1,  1).normalized);
        TryAddClip(entries, backLeft,  new Vector2(-1, -1).normalized);
        TryAddClip(entries, backRight, new Vector2( 1, -1).normalized);

        if (entries.Count < 4)
        {
            Debug.LogWarning($"[CombatLocomotionMixer] Only {entries.Count} clips found, " +
                             $"need at least 4. Mixer will not be created.");
            return null;
        }

        var mixer = new CartesianMixerState();
        foreach (var (clip, pos, speed) in entries)
        {
            mixer.Add(clip, pos);
        }

        // Apply per-clip speeds from the ClipTransition data
        for (int i = 0; i < entries.Count; i++)
        {
            float speed = entries[i].speed;
            if (!float.IsNaN(speed) && speed > 0f)
                mixer.GetChild(i).Speed = speed;
        }

        return mixer;
    }

    private void TryAddClip(List<(AnimationClip, Vector2, float)> entries, string key, Vector2 pos)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (animationSet.TryGet(key, out var transition) &&
            transition != null && transition.Clip != null)
        {
            entries.Add((transition.Clip, pos, transition.Speed));
        }
        else
        {
            Debug.LogWarning($"[CombatLocomotionMixer] Clip key '{key}' not found — skipping.");
        }
    }

    // ───────────────────── Locomotion API ─────────────────────

    /// <summary>
    /// Returns true if this mixer should drive base layer locomotion.
    /// </summary>
    public bool WantsControl(int activeWeaponSlot, bool isMoving, bool isDodging,
                             bool isBlocking, bool isBowDrawing, bool isBowAiming,
                             bool isMounted, bool isDodgeStep, bool isSprinting)
    {
        if (!_initialized) return false;
        if (!isMoving) return false;
        if (isDodging) return false;
        if (isDodgeStep) return false;
        if (isMounted) return false;

        // Combat sprint always falls through to rules for a forward-only run.
        // Applies to bow too — bow + sprint plays the rule-based forward run,
        // matching sword + sprint behavior.
        if (isSprinting) return false;

        if (activeWeaponSlot == 2)
            return _bowNoAimMixer != null || _bowAimMixer != null;

        return _mixersBySlot.ContainsKey(activeWeaponSlot);
    }

    /// <summary>
    /// Plays the appropriate locomotion mixer on the given layer.
    /// Call every frame when WantsControl() is true.
    /// </summary>
    public void UpdateAndPlay(AnimancerLayer layer, Vector2 moveInput,
                              int activeWeaponSlot, bool bowAimDraw)
    {
        CartesianMixerState target = SelectMixer(activeWeaponSlot, bowAimDraw);
        if (target == null) return;

        _smoothParam = Vector2.SmoothDamp(_smoothParam, moveInput,
            ref _paramVelocity, parameterSmoothTime);

        target.Parameter = _smoothParam;

        if (_activeMixer != target)
        {
            layer.Play(target, fadeDuration);
            _activeMixer = target;
        }
    }

    private CartesianMixerState SelectMixer(int activeWeaponSlot, bool bowAimDraw)
    {
        if (activeWeaponSlot == 2)
        {
            // Prefer the requested aim/no-aim variant; fall back to whichever exists
            // if the matching one is missing.
            if (bowAimDraw)
                return _bowAimMixer ?? _bowNoAimMixer;
            return _bowNoAimMixer ?? _bowAimMixer;
        }

        return _mixersBySlot.TryGetValue(activeWeaponSlot, out var m) ? m : null;
    }

    // ───────────────────── Dodge / Dodge Step API ─────────────────────

    /// <summary>
    /// Plays the dodge mixer as a one-shot on the given layer.
    /// Sets the blend parameter once from moveInput (direction at dodge start).
    /// Returns the mixer state so the caller can LockLayerUntilEnd.
    /// Returns null if the dodge mixer doesn't exist.
    /// </summary>
    /// <param name="layer">The base Animancer layer.</param>
    /// <param name="moveInput">Raw move input at time of dodge. Zero = backward.</param>
    public CartesianMixerState PlayDodge(AnimancerLayer layer, Vector2 moveInput)
    {
        if (_dodgeMixer == null) return null;

        // Default to backward if no input
        Vector2 dir = moveInput.sqrMagnitude > 0.01f ? moveInput.normalized : new Vector2(0, -1);
        _dodgeMixer.Parameter = dir;

        layer.Play(_dodgeMixer, dodgeFadeDuration);
        _activeMixer = _dodgeMixer;

        return _dodgeMixer;
    }

    /// <summary>
    /// Plays the dodge step mixer as a one-shot on the given layer.
    /// Returns the mixer state so the caller can LockLayerUntilEnd.
    /// Returns null if the dodge step mixer doesn't exist.
    /// </summary>
    public CartesianMixerState PlayDodgeStep(AnimancerLayer layer, Vector2 moveInput)
    {
        if (_dodgeStepMixer == null) return null;

        Vector2 dir = moveInput.sqrMagnitude > 0.01f ? moveInput.normalized : new Vector2(0, -1);
        _dodgeStepMixer.Parameter = dir;

        layer.Play(_dodgeStepMixer, dodgeFadeDuration);
        _activeMixer = _dodgeStepMixer;

        return _dodgeStepMixer;
    }

    /// <summary>
    /// Returns the duration of the clip with the highest blend weight in the mixer,
    /// i.e. the clip most relevant to the current direction.
    /// </summary>
    public static float GetDominantClipDuration(CartesianMixerState mixer)
    {
        if (mixer == null) return 0f;
        float maxWeight = -1f;
        float duration = 0f;
        for (int i = 0; i < mixer.ChildCount; i++)
        {
            var child = mixer.GetChild(i);
            if (child != null && child.Weight > maxWeight)
            {
                maxWeight = child.Weight;
                duration = child.Length;
            }
        }
        return duration;
    }

    /// <summary>
    /// Returns the longest clip duration in a mixer (used for lockUntilEnd timing).
    /// </summary>
    public static float GetMixerDuration(CartesianMixerState mixer)
    {
        if (mixer == null) return 0f;
        float max = 0f;
        for (int i = 0; i < mixer.ChildCount; i++)
        {
            var child = mixer.GetChild(i);
            if (child != null && child.Length > max)
                max = child.Length;
        }
        return max;
    }

    // ───────────────────── Network / Reset ─────────────────────

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
