using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────
// SoundDatabase.cs — Central configurable sound data
//
// Create via: Assets > Create > FEFE/Sound/Sound Database
// One per project. Holds all clip references, terrain maps,
// impact matrices, and distance settings.
// ─────────────────────────────────────────────────────────

/// <summary>
/// Sound category determines base hearing distance.
/// </summary>
public enum SoundCategory
{
    Quiet,      // footsteps, cloth, bow draw
    Combat,     // sword swing, arrow impact, hits
    Loud,       // dragon roar, ballista fire, fire breath
    Global,     // UI, respawn — no distance check
    Ambient     // wind, birds — local only, not networked
}

/// <summary>
/// A single sound entry in the database.
/// Supports multiple clips for random variation.
/// </summary>
[Serializable]
public class SoundEntry
{
    [Tooltip("Unique name to reference this sound (e.g. 'Sword_Swing', 'Dragon_Roar')")]
    public string soundName;

    public SoundCategory category = SoundCategory.Combat;

    [Tooltip("Multiple clips for random variation. One is picked at random each play.")]
    public AudioClip[] clips;

    [MinMaxRange(0f, 1f)]
    public Vector2 volume = new Vector2(0.8f, 1f);

    [Tooltip("Pitch randomization range for variety")]
    [MinMaxRange(0.5f, 1.5f)]
    public Vector2 pitch = new Vector2(0.95f, 1.05f);

    [Tooltip("If true, overrides the category's default min/max distance")]
    public bool overrideDistance;

    [Tooltip("Distance at which sound is full volume (only if overrideDistance is true)")]
    public float customMinDistance = 5f;

    [Tooltip("Distance at which sound fades to zero (only if overrideDistance is true)")]
    public float customMaxDistance = 50f;

    /// <summary>
    /// Get a random clip from the array. Returns null if empty.
    /// </summary>
    public AudioClip GetRandomClip()
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[UnityEngine.Random.Range(0, clips.Length)];
    }

    public float GetRandomVolume() => UnityEngine.Random.Range(volume.x, volume.y);
    public float GetRandomPitch() => UnityEngine.Random.Range(pitch.x, pitch.y);
}

/// <summary>
/// Configurable distance settings per sound category.
/// </summary>
[Serializable]
public class CategoryDistanceSettings
{
    public SoundCategory category;

    [Tooltip("Distance at which sound is full volume")]
    public float minDistance = 5f;

    [Tooltip("Distance at which sound fades to zero / is not played")]
    public float maxDistance = 50f;
}

/// <summary>
/// Maps a terrain layer index to a surface type string.
/// </summary>
[Serializable]
public class TerrainLayerMapping
{
    [Tooltip("Index of the terrain layer in the Terrain's splat map (0-based)")]
    public int terrainLayerIndex;

    [Tooltip("Surface type name (e.g. 'Grass', 'Dirt', 'Stone', 'Sand', 'Snow')")]
    public string surfaceType = "Grass";
}

/// <summary>
/// Maps a surface type to a set of footstep clips.
/// </summary>
[Serializable]
public class SurfaceFootstepSet
{
    [Tooltip("Must match a surfaceType from TerrainLayerMapping or SurfaceTag")]
    public string surfaceType = "Grass";

    [Tooltip("Footstep clips for this surface. One is picked randomly each step.")]
    public AudioClip[] footstepClips;

    [MinMaxRange(0f, 1f)]
    public Vector2 volume = new Vector2(0.3f, 0.5f);

    [MinMaxRange(0.8f, 1.2f)]
    public Vector2 pitch = new Vector2(0.9f, 1.1f);

    public AudioClip GetRandomClip()
    {
        if (footstepClips == null || footstepClips.Length == 0) return null;
        return footstepClips[UnityEngine.Random.Range(0, footstepClips.Length)];
    }

    public float GetRandomVolume() => UnityEngine.Random.Range(volume.x, volume.y);
    public float GetRandomPitch() => UnityEngine.Random.Range(pitch.x, pitch.y);
}

/// <summary>
/// Maps attacker material + target material to impact clips.
/// E.g. "Metal" + "Flesh" → sword-into-body sounds
///      "Metal" + "Metal" → block/parry clang
///      "Wood"  + "Stone" → arrow hitting rock
/// </summary>
[Serializable]
public class ImpactSoundEntry
{
    [Tooltip("Display name for this entry (e.g. 'Metal-Wood', 'Sword-Flesh')")]
    public string soundName = "";

    [Tooltip("Material of the weapon/projectile (e.g. 'Metal', 'Wood')")]
    public string attackerMaterial = "Metal";

    [Tooltip("Material of the target (e.g. 'Flesh', 'Metal', 'Stone', 'Wood')")]
    public string targetMaterial = "Flesh";

    public AudioClip[] impactClips;

    [MinMaxRange(0f, 1f)]
    public Vector2 volume = new Vector2(0.7f, 1f);

    [MinMaxRange(0.8f, 1.2f)]
    public Vector2 pitch = new Vector2(0.9f, 1.1f);

    public AudioClip GetRandomClip()
    {
        if (impactClips == null || impactClips.Length == 0) return null;
        return impactClips[UnityEngine.Random.Range(0, impactClips.Length)];
    }

    public float GetRandomVolume() => UnityEngine.Random.Range(volume.x, volume.y);
    public float GetRandomPitch() => UnityEngine.Random.Range(pitch.x, pitch.y);
}

/// <summary>
/// Central sound database. Create one asset via Assets > Create > FEFE/Sound/Sound Database.
/// All sound configuration is done here in the Inspector — no code changes to add new sounds.
/// </summary>
[CreateAssetMenu(fileName = "SoundDatabase", menuName = "FEFE/Sound/Sound Database")]
public class SoundDatabase : ScriptableObject
{
    // ─── Category Distances ───
    [Header("Category Distance Settings (configurable)")]
    [Tooltip("Default hearing distances per category. Adjust to taste.")]
    [SerializeField] private CategoryDistanceSettings[] categoryDistances = new CategoryDistanceSettings[]
    {
        new CategoryDistanceSettings { category = SoundCategory.Quiet,  minDistance = 2f,  maxDistance = 30f },
        new CategoryDistanceSettings { category = SoundCategory.Combat, minDistance = 5f,  maxDistance = 50f },
        new CategoryDistanceSettings { category = SoundCategory.Loud,   minDistance = 20f, maxDistance = 500f },
        new CategoryDistanceSettings { category = SoundCategory.Global, minDistance = 0f,  maxDistance = float.MaxValue },
    };

    // ─── Sound Entries ───
    [Header("Sound Entries")]
    [Tooltip("All game sounds. Each has a unique name, category, and clip set.")]
    [SerializeField] private SoundEntry[] sounds;

    // ─── Terrain / Surface ───
    [Header("Terrain Surface Mapping")]
    [Tooltip("Maps terrain layer indices to surface type names. Add entries as you add terrain layers.")]
    [SerializeField] private TerrainLayerMapping[] terrainLayerMappings;

    [Tooltip("Default surface type when no terrain or SurfaceTag is found")]
    [SerializeField] private string defaultSurfaceType = "Grass";

    // ─── Footstep Sets ───
    [Header("Footstep Clip Sets")]
    [Tooltip("One entry per surface type. Maps surface name to footstep clips.")]
    [SerializeField] private SurfaceFootstepSet[] footstepSets;

    // ─── Impact Matrix ───
    [Header("Impact Sound Matrix")]
    [Tooltip("Maps attacker material + target material to impact clips.")]
    [SerializeField] private ImpactSoundEntry[] impactSounds;

    // ─── Runtime Lookups (built on enable) ───
    private Dictionary<string, SoundEntry> _soundMap;
    private Dictionary<SoundCategory, CategoryDistanceSettings> _distanceMap;
    private Dictionary<int, string> _terrainLayerMap;
    private Dictionary<string, SurfaceFootstepSet> _footstepMap;
    private Dictionary<string, ImpactSoundEntry> _impactMap; // key = "attackerMat|targetMat"

    private void OnEnable() => BuildLookups();

    private void BuildLookups()
    {
        // Sound entries
        _soundMap = new Dictionary<string, SoundEntry>(StringComparer.OrdinalIgnoreCase);
        if (sounds != null)
        {
            foreach (var s in sounds)
            {
                if (string.IsNullOrEmpty(s.soundName)) continue;
                _soundMap[s.soundName] = s;
            }
        }

        // Category distances
        _distanceMap = new Dictionary<SoundCategory, CategoryDistanceSettings>();
        if (categoryDistances != null)
        {
            foreach (var cd in categoryDistances)
                _distanceMap[cd.category] = cd;
        }

        // Terrain layers
        _terrainLayerMap = new Dictionary<int, string>();
        if (terrainLayerMappings != null)
        {
            foreach (var m in terrainLayerMappings)
                _terrainLayerMap[m.terrainLayerIndex] = m.surfaceType;
        }

        // Footstep sets
        _footstepMap = new Dictionary<string, SurfaceFootstepSet>(StringComparer.OrdinalIgnoreCase);
        if (footstepSets != null)
        {
            foreach (var fs in footstepSets)
            {
                if (string.IsNullOrEmpty(fs.surfaceType)) continue;
                _footstepMap[fs.surfaceType] = fs;
            }
        }

        // Impact matrix
        _impactMap = new Dictionary<string, ImpactSoundEntry>(StringComparer.OrdinalIgnoreCase);
        if (impactSounds != null)
        {
            foreach (var imp in impactSounds)
            {
                string key = $"{imp.attackerMaterial}|{imp.targetMaterial}";
                _impactMap[key] = imp;
            }
        }
    }

    // ─── Public API ───

    public SoundEntry GetSound(string soundName)
    {
        if (_soundMap == null) BuildLookups();
        _soundMap.TryGetValue(soundName, out var entry);
        return entry;
    }

    public CategoryDistanceSettings GetCategoryDistance(SoundCategory category)
    {
        if (_distanceMap == null) BuildLookups();
        _distanceMap.TryGetValue(category, out var settings);
        return settings;
    }

    /// <summary>
    /// Get min/max distance for a specific sound, respecting per-sound overrides.
    /// </summary>
    public void GetDistanceForSound(SoundEntry entry, out float minDist, out float maxDist)
    {
        if (entry.overrideDistance)
        {
            minDist = entry.customMinDistance;
            maxDist = entry.customMaxDistance;
        }
        else
        {
            var catDist = GetCategoryDistance(entry.category);
            if (catDist != null)
            {
                minDist = catDist.minDistance;
                maxDist = catDist.maxDistance;
            }
            else
            {
                minDist = 5f;
                maxDist = 50f;
            }
        }
    }

    /// <summary>
    /// Get surface type for a terrain layer index. Returns defaultSurfaceType if not mapped.
    /// </summary>
    public string GetSurfaceTypeForTerrainLayer(int layerIndex)
    {
        if (_terrainLayerMap == null) BuildLookups();
        return _terrainLayerMap.TryGetValue(layerIndex, out var type) ? type : defaultSurfaceType;
    }

    public string DefaultSurfaceType => defaultSurfaceType;

    /// <summary>
    /// Get footstep clip set for a surface type. Returns null if not configured.
    /// </summary>
    public SurfaceFootstepSet GetFootstepSet(string surfaceType)
    {
        if (_footstepMap == null) BuildLookups();
        if (string.IsNullOrEmpty(surfaceType)) surfaceType = defaultSurfaceType;
        _footstepMap.TryGetValue(surfaceType, out var set);
        return set;
    }

    /// <summary>
    /// Get impact clip set for an attacker/target material combo.
    /// Falls back to "Default|Default" if no match found.
    /// </summary>
    public ImpactSoundEntry GetImpactSound(string attackerMaterial, string targetMaterial)
    {
        if (_impactMap == null) BuildLookups();

        // Try exact match
        string key = $"{attackerMaterial}|{targetMaterial}";
        if (_impactMap.TryGetValue(key, out var entry)) return entry;

        // Try attacker + Default
        key = $"{attackerMaterial}|Default";
        if (_impactMap.TryGetValue(key, out entry)) return entry;

        // Try Default + target
        key = $"Default|{targetMaterial}";
        if (_impactMap.TryGetValue(key, out entry)) return entry;

        // Try Default + Default
        key = "Default|Default";
        _impactMap.TryGetValue(key, out entry);
        return entry;
    }
}
