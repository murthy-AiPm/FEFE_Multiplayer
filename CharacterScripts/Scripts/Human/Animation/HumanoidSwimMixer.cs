using System.Collections.Generic;
using Animancer;
using UnityEngine;

/// <summary>
/// Drives surface and underwater humanoid swim blend trees on Animancer's base layer.
/// </summary>
public class HumanoidSwimMixer : MonoBehaviour
{
    [Header("Animation Set")]
    [SerializeField] private AnimationSetBase animationSet;

    [Header("Surface Clip Keys")]
    [SerializeField] private string surfaceIdle = "H_Swim_Idle";
    [SerializeField] private string surfaceForward = "H_Swim_Forward";
    [SerializeField] private string surfaceLeft = "H_Swim_Left";
    [SerializeField] private string surfaceRight = "H_Swim_Right";
    [SerializeField] private string surfaceUp = "H_Swim_Up";
    [SerializeField] private string surfaceDown = "H_Swim_Down";
    [SerializeField] private string surfaceFastForward = "H_Swim_Fast_Forward";
    [SerializeField] private string surfaceFastLeft = "H_Swim_Fast_Left";
    [SerializeField] private string surfaceFastRight = "H_Swim_Fast_Right";

    [Header("Underwater Clip Keys")]
    [SerializeField] private string underwaterIdleFallback = "H_Swim_Idle";
    [SerializeField] private string underwaterForward = "S_SwimUnder_Forward";
    [SerializeField] private string underwaterLeft = "S_SwimUnder_Left";
    [SerializeField] private string underwaterRight = "S_SwimUnder_Right";
    [SerializeField] private string underwaterUp = "S_SwimUnder_Up";
    [SerializeField] private string underwaterDown = "S_SwimUnder_Down";

    [Header("Blend Settings")]
    [SerializeField] private float fadeDuration = 0.15f;
    [SerializeField] private float parameterSmoothTime = 0.08f;

    private CartesianMixerState _surfaceMixer;
    private CartesianMixerState _surfaceFastMixer;
    private CartesianMixerState _underwaterMixer;
    private CartesianMixerState _activeMixer;
    private Vector2 _smoothParameter;
    private Vector2 _parameterVelocity;
    private bool _initialized;

    public Vector2 BlendParameter => _smoothParameter;

    public void Initialize(AnimancerComponent animancer)
    {
        if (_initialized)
            return;

        if (animationSet == null)
        {
            Debug.LogError("[HumanoidSwimMixer] No AnimationSetBase assigned.");
            return;
        }

        _surfaceMixer = BuildSurfaceMixer(false);
        _surfaceFastMixer = BuildSurfaceMixer(true);
        _underwaterMixer = BuildUnderwaterMixer();

        _initialized = _surfaceMixer != null || _surfaceFastMixer != null || _underwaterMixer != null;
    }

    public bool WantsControl(AnimationContext ctx)
    {
        return _initialized && ctx.Swimming;
    }

    public void UpdateAndPlay(AnimancerLayer layer, AnimationContext ctx)
    {
        CartesianMixerState target = SelectMixer(ctx);
        if (target == null)
            return;

        Vector2 targetParameter = BuildParameter(ctx);
        _smoothParameter = Vector2.SmoothDamp(_smoothParameter, targetParameter,
            ref _parameterVelocity, parameterSmoothTime);
        target.Parameter = _smoothParameter;

        if (_activeMixer != target)
        {
            layer.Play(target, fadeDuration);
            _activeMixer = target;
        }
    }

    public void SetRemoteParameter(Vector2 parameter)
    {
        _smoothParameter = parameter;
        _parameterVelocity = Vector2.zero;
    }

    public void ResetActiveState()
    {
        _activeMixer = null;
        _smoothParameter = Vector2.zero;
        _parameterVelocity = Vector2.zero;
    }

    private CartesianMixerState SelectMixer(AnimationContext ctx)
    {
        if (ctx.SwimUnderwater)
            return _underwaterMixer ?? _surfaceMixer ?? _surfaceFastMixer;

        if (ctx.SwimFast)
            return _surfaceFastMixer ?? _surfaceMixer ?? _underwaterMixer;

        return _surfaceMixer ?? _surfaceFastMixer ?? _underwaterMixer;
    }

    private Vector2 BuildParameter(AnimationContext ctx)
    {
        Vector2 move = ctx.SwimMoveInput;
        float vertical = ctx.SwimVertical;

        if (Mathf.Abs(vertical) > 0.1f && move.sqrMagnitude < 0.1f)
            return new Vector2(0f, vertical > 0f ? 2f : -2f);

        if (move.sqrMagnitude < 0.001f)
            return Vector2.zero;

        return new Vector2(move.x, Mathf.Max(0f, move.y));
    }

    private CartesianMixerState BuildSurfaceMixer(bool fast)
    {
        var entries = new List<(AnimationClip clip, Vector2 pos, float speed)>();
        TryAddClip(entries, surfaceIdle, Vector2.zero);
        TryAddClip(entries, fast ? surfaceFastForward : surfaceForward, new Vector2(0f, 1f));
        TryAddClip(entries, fast ? surfaceFastLeft : surfaceLeft, new Vector2(-1f, 0f));
        TryAddClip(entries, fast ? surfaceFastRight : surfaceRight, new Vector2(1f, 0f));
        TryAddClip(entries, surfaceUp, new Vector2(0f, 2f));
        TryAddClip(entries, surfaceDown, new Vector2(0f, -2f));
        return BuildMixer(entries, fast ? "SurfaceFast" : "Surface");
    }

    private CartesianMixerState BuildUnderwaterMixer()
    {
        var entries = new List<(AnimationClip clip, Vector2 pos, float speed)>();
        TryAddClip(entries, underwaterIdleFallback, Vector2.zero);
        TryAddClip(entries, underwaterForward, new Vector2(0f, 1f));
        TryAddClip(entries, underwaterLeft, new Vector2(-1f, 0f));
        TryAddClip(entries, underwaterRight, new Vector2(1f, 0f));
        TryAddClip(entries, underwaterUp, new Vector2(0f, 2f));
        TryAddClip(entries, underwaterDown, new Vector2(0f, -2f));
        return BuildMixer(entries, "Underwater");
    }

    private CartesianMixerState BuildMixer(List<(AnimationClip clip, Vector2 pos, float speed)> entries, string label)
    {
        if (entries.Count < 2)
        {
            Debug.LogWarning($"[HumanoidSwimMixer] {label} mixer only found {entries.Count} clips.");
            return null;
        }

        var mixer = new CartesianMixerState();
        foreach (var entry in entries)
            mixer.Add(entry.clip, entry.pos);

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
        if (string.IsNullOrEmpty(key))
            return;

        if (animationSet.TryGet(key, out var transition) && transition != null && transition.Clip != null)
        {
            entries.Add((transition.Clip, pos, transition.Speed));
        }
        else
        {
            Debug.LogWarning($"[HumanoidSwimMixer] Clip key '{key}' not found.");
        }
    }
}
