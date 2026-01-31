using System;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

[Serializable]
public struct ClipEntry
{
    public string key;                 // e.g. "Locomotion/Idle", "Attack/Heavy"
    public ClipTransition transition;  // assign clip + settings in inspector
}

public abstract class AnimationSetBase : ScriptableObject
{
    [SerializeField] private ClipEntry[] clips;

    private Dictionary<string, ClipTransition> _map;

    protected virtual void OnEnable() => BuildMap();

    private void BuildMap()
    {
        _map = new Dictionary<string, ClipTransition>(StringComparer.Ordinal);
        if (clips == null) return;

        for (int i = 0; i < clips.Length; i++)
        {
            var e = clips[i];
            if (string.IsNullOrEmpty(e.key)) continue;
            if (e.transition == null || e.transition.Clip == null) continue;
            _map[e.key] = e.transition;
        }
    }

    public bool TryGet(string key, out ClipTransition transition)
    {
        if (_map == null) BuildMap();
        if (string.IsNullOrEmpty(key)) { transition = null; return false; }
        return _map.TryGetValue(key, out transition);
    }

    public ClipTransition GetOrNull(string key)
    {
        TryGet(key, out var t);
        return t;
    }
}
