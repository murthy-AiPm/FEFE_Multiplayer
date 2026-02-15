using System;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

[Serializable]
public struct ClipEntry
{
    public string key;                 // e.g. "Locomotion/Idle", "Attack/Heavy"
    public ClipTransition transition;  // assign clip + settings in inspector

    [Tooltip("Enable root motion when this clip is played. " +
             "Movement delta will be applied to the CharacterController.")]
    public bool useRootMotion;
}

public abstract class AnimationSetBase : ScriptableObject
{
    [SerializeField] private ClipEntry[] clips;

    private Dictionary<string, ClipTransition> _transitionMap;
    private HashSet<string> _rootMotionKeys;

    protected virtual void OnEnable() => BuildMap();

    private void BuildMap()
    {
        _transitionMap = new Dictionary<string, ClipTransition>(StringComparer.Ordinal);
        _rootMotionKeys = new HashSet<string>(StringComparer.Ordinal);
        if (clips == null) return;

        for (int i = 0; i < clips.Length; i++)
        {
            var e = clips[i];
            if (string.IsNullOrEmpty(e.key)) continue;
            if (e.transition == null || e.transition.Clip == null) continue;
            _transitionMap[e.key] = e.transition;
            if (e.useRootMotion)
                _rootMotionKeys.Add(e.key);
        }
    }

    public bool TryGet(string key, out ClipTransition transition)
    {
        if (_transitionMap == null) BuildMap();
        if (string.IsNullOrEmpty(key)) { transition = null; return false; }
        return _transitionMap.TryGetValue(key, out transition);
    }

    /// <summary>
    /// Returns true if the clip entry for this key has useRootMotion enabled.
    /// </summary>
    public bool IsRootMotion(string key)
    {
        if (_rootMotionKeys == null) BuildMap();
        return !string.IsNullOrEmpty(key) && _rootMotionKeys.Contains(key);
    }

    public ClipTransition GetOrNull(string key)
    {
        TryGet(key, out var t);
        return t;
    }
}