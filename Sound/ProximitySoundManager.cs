using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// ─────────────────────────────────────────────────────────
// ProximitySoundManager.cs — Central networked sound system
//
// Singleton NetworkBehaviour. Place on a persistent GameObject
// in the scene (or spawn via NetworkManager).
//
// Usage:
//   ProximitySoundManager.Instance.PlaySound("Sword_Swing", transform.position);
//   ProximitySoundManager.Instance.PlaySoundLocal("UI_Click", Vector3.zero);
//
// Flow:
//   1. Caller invokes PlaySound(name, position)
//   2. Owner sends ServerRpc with soundId + position + category
//   3. Server broadcasts ClientRpc to ALL clients
//   4. Each client checks distance to local listener
//   5. If in range → spawn pooled 3D AudioSource, play clip
//   6. If out of range → skip
// ─────────────────────────────────────────────────────────

public class ProximitySoundManager : NetworkBehaviour
{
    public static ProximitySoundManager Instance { get; private set; }

    [Header("Database")]
    [SerializeField] private SoundDatabase database;

    [Header("Audio Source Pool")]
    [Tooltip("Max concurrent 3D audio sources. Increase if sounds are getting cut off.")]
    [SerializeField] private int poolSize = 32;

    [Tooltip("Rolloff mode for 3D sounds")]
    [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;

    [Header("Listener")]
    [Tooltip("If null, auto-finds the AudioListener at runtime")]
    [SerializeField] private Transform listenerTransform;

    // ─── Pool ───
    private List<AudioSource> _pool;
    private int _poolIndex;

    // ─── Sound name → int mapping for network efficiency ───
    // Instead of sending strings over the network, we send an int index
    private Dictionary<string, int> _soundNameToId;
    private Dictionary<int, string> _idToSoundName;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildSoundIdMaps();
        CreatePool();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Re-find listener after network spawn in case camera changed
        FindListener();
    }

    private void FindListener()
    {
        if (listenerTransform != null) return;

        var listener = FindObjectOfType<AudioListener>();
        if (listener != null)
            listenerTransform = listener.transform;
    }

    // ─── Sound ID Maps ───

    private void BuildSoundIdMaps()
    {
        _soundNameToId = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        _idToSoundName = new Dictionary<int, string>();

        // We assign IDs based on the order in the database.
        // This is deterministic as long as all clients use the same SoundDatabase asset.
        if (database == null) return;

        // Use reflection or a public accessor to iterate sounds
        // For now, we'll use a name-based hash approach that doesn't require
        // exposing the internal array. Sound names are hashed to ints.
        // Collisions are astronomically unlikely for <100 sounds.
    }

    private int GetSoundId(string soundName)
    {
        // Stable hash — same result on all clients for the same string
        return Animator.StringToHash(soundName);
    }

    // ─── Audio Source Pool ───

    private void CreatePool()
    {
        _pool = new List<AudioSource>(poolSize);

        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject($"PooledAudio_{i}");
            go.transform.SetParent(transform);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;          // full 3D
            source.rolloffMode = rolloffMode;
            source.dopplerLevel = 0.3f;
            source.spread = 30f;
            source.loop = false;

            go.SetActive(false);
            _pool.Add(source);
        }
    }

    private AudioSource GetPooledSource()
    {
        // Round-robin through pool
        for (int i = 0; i < poolSize; i++)
        {
            int idx = (_poolIndex + i) % poolSize;
            if (!_pool[idx].isPlaying)
            {
                _poolIndex = (idx + 1) % poolSize;
                return _pool[idx];
            }
        }

        // All busy — steal oldest
        _poolIndex = (_poolIndex + 1) % poolSize;
        var stolen = _pool[_poolIndex];
        stolen.Stop();
        return stolen;
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Play a sound at a world position. Networked — all clients in range will hear it.
    /// Call from any script: ProximitySoundManager.Instance.PlaySound("Sword_Swing", transform.position);
    /// </summary>
    public void PlaySound(string soundName, Vector3 worldPosition)
    {
        if (database == null || string.IsNullOrEmpty(soundName)) return;

        var entry = database.GetSound(soundName);
        if (entry == null)
        {
            Debug.LogWarning($"[ProximitySoundManager] Sound not found in database: '{soundName}'");
            return;
        }

        // Ambient sounds are local-only, never networked
        if (entry.category == SoundCategory.Ambient)
        {
            PlaySoundLocal(soundName, worldPosition);
            return;
        }

        // Global sounds play locally for everyone without distance check
        // Still need to network them so other clients hear them
        int soundId = GetSoundId(soundName);

        if (IsServer)
        {
            // Server can broadcast directly
            PlaySoundClientRpc(soundId, worldPosition, soundName);
        }
        else
        {
            // Client sends to server, server broadcasts
            PlaySoundServerRpc(soundId, worldPosition, soundName);
        }
    }

    /// <summary>
    /// Play a sound locally only. No networking. Good for UI, ambient, personal feedback.
    /// </summary>
    public void PlaySoundLocal(string soundName, Vector3 worldPosition)
    {
        if (database == null) return;

        var entry = database.GetSound(soundName);
        if (entry == null) return;

        PlayClipAtPosition(entry, worldPosition);
    }

    /// <summary>
    /// Play a footstep sound for a given surface type at a position. Networked.
    /// </summary>
    public void PlayFootstep(string surfaceType, Vector3 worldPosition, SoundCategory category = SoundCategory.Quiet)
    {
        if (database == null) return;

        var footstepSet = database.GetFootstepSet(surfaceType);
        if (footstepSet == null) return;

        var clip = footstepSet.GetRandomClip();
        if (clip == null) return;

        // Network the footstep using a special encoding:
        // We send surface type + position, each client resolves the clip locally
        int surfaceId = GetSoundId("Footstep_" + surfaceType);

        if (IsServer)
            PlayFootstepClientRpc(surfaceId, worldPosition, surfaceType);
        else
            PlayFootstepServerRpc(surfaceId, worldPosition, surfaceType);
    }

    /// <summary>
    /// Play an impact sound for attacker material hitting target material. Networked.
    /// </summary>
    public void PlayImpact(string attackerMaterial, string targetMaterial, Vector3 worldPosition)
    {
        if (database == null) return;

        var impactEntry = database.GetImpactSound(attackerMaterial, targetMaterial);
        if (impactEntry == null) return;

        // Network as a generic sound with the impact lookup done on each client
        int impactId = GetSoundId($"Impact_{attackerMaterial}_{targetMaterial}");

        if (IsServer)
            PlayImpactClientRpc(impactId, worldPosition, attackerMaterial, targetMaterial);
        else
            PlayImpactServerRpc(impactId, worldPosition, attackerMaterial, targetMaterial);
    }

    // ═══════════════════════════════════════════════════════
    //  NETWORK RPCs
    // ═══════════════════════════════════════════════════════

    // --- Generic Sound ---

    [ServerRpc(RequireOwnership = false)]
    private void PlaySoundServerRpc(int soundId, Vector3 position, string soundName)
    {
        PlaySoundClientRpc(soundId, position, soundName);
    }

    [ClientRpc]
    private void PlaySoundClientRpc(int soundId, Vector3 position, string soundName)
    {
        var entry = database.GetSound(soundName);
        if (entry == null) return;

        // Distance check (skip for Global)
        if (entry.category != SoundCategory.Global)
        {
            if (!IsInRange(entry, position)) return;
        }

        PlayClipAtPosition(entry, position);
    }

    // --- Footstep ---

    [ServerRpc(RequireOwnership = false)]
    private void PlayFootstepServerRpc(int surfaceId, Vector3 position, string surfaceType)
    {
        PlayFootstepClientRpc(surfaceId, position, surfaceType);
    }

    [ClientRpc]
    private void PlayFootstepClientRpc(int surfaceId, Vector3 position, string surfaceType)
    {
        // Distance check using Quiet category
        var quietDist = database.GetCategoryDistance(SoundCategory.Quiet);
        if (quietDist != null)
        {
            float dist = GetDistanceToListener(position);
            if (dist > quietDist.maxDistance) return;
        }

        var footstepSet = database.GetFootstepSet(surfaceType);
        if (footstepSet == null) return;

        var clip = footstepSet.GetRandomClip();
        if (clip == null) return;

        float volume = Random.Range(footstepSet.volumeMin, footstepSet.volumeMax);
        float pitch = Random.Range(footstepSet.pitchMin, footstepSet.pitchMax);

        float minDist = quietDist?.minDistance ?? 2f;
        float maxDist = quietDist?.maxDistance ?? 30f;

        PlayClipRaw(clip, position, volume, pitch, minDist, maxDist);
    }

    // --- Impact ---

    [ServerRpc(RequireOwnership = false)]
    private void PlayImpactServerRpc(int impactId, Vector3 position, string attackerMat, string targetMat)
    {
        PlayImpactClientRpc(impactId, position, attackerMat, targetMat);
    }

    [ClientRpc]
    private void PlayImpactClientRpc(int impactId, Vector3 position, string attackerMat, string targetMat)
    {
        // Distance check using Combat category
        var combatDist = database.GetCategoryDistance(SoundCategory.Combat);
        if (combatDist != null)
        {
            float dist = GetDistanceToListener(position);
            if (dist > combatDist.maxDistance) return;
        }

        var impactEntry = database.GetImpactSound(attackerMat, targetMat);
        if (impactEntry == null) return;

        var clip = impactEntry.GetRandomClip();
        if (clip == null) return;

        float volume = Random.Range(impactEntry.volumeMin, impactEntry.volumeMax);
        float pitch = Random.Range(impactEntry.pitchMin, impactEntry.pitchMax);

        float minDist = combatDist?.minDistance ?? 5f;
        float maxDist = combatDist?.maxDistance ?? 50f;

        PlayClipRaw(clip, position, volume, pitch, minDist, maxDist);
    }

    // ═══════════════════════════════════════════════════════
    //  INTERNAL
    // ═══════════════════════════════════════════════════════

    private bool IsInRange(SoundEntry entry, Vector3 position)
    {
        database.GetDistanceForSound(entry, out _, out float maxDist);
        float dist = GetDistanceToListener(position);
        return dist <= maxDist;
    }

    private float GetDistanceToListener(Vector3 position)
    {
        if (listenerTransform == null) FindListener();
        if (listenerTransform == null) return float.MaxValue;
        return Vector3.Distance(listenerTransform.position, position);
    }

    /// <summary>
    /// Play a SoundEntry clip at a position using the pooled audio system.
    /// </summary>
    private void PlayClipAtPosition(SoundEntry entry, Vector3 position)
    {
        var clip = entry.GetRandomClip();
        if (clip == null) return;

        database.GetDistanceForSound(entry, out float minDist, out float maxDist);
        float volume = entry.GetRandomVolume();
        float pitch = entry.GetRandomPitch();

        PlayClipRaw(clip, position, volume, pitch, minDist, maxDist);
    }

    /// <summary>
    /// Low-level: play a clip at a position using a pooled AudioSource.
    /// </summary>
    private void PlayClipRaw(AudioClip clip, Vector3 position, float volume, float pitch, float minDist, float maxDist)
    {
        if (clip == null) return;

        var source = GetPooledSource();
        source.gameObject.SetActive(true);
        source.transform.position = position;

        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.minDistance = minDist;
        source.maxDistance = maxDist;
        source.rolloffMode = rolloffMode;
        source.spatialBlend = 1f; // full 3D
        source.loop = false;

        source.Play();

        // Auto-disable after clip finishes
        StartCoroutine(ReturnToPoolAfterPlay(source, clip.length / Mathf.Max(pitch, 0.1f)));
    }

    private System.Collections.IEnumerator ReturnToPoolAfterPlay(AudioSource source, float duration)
    {
        yield return new WaitForSeconds(duration + 0.1f);
        if (source != null && !source.isPlaying)
        {
            source.gameObject.SetActive(false);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  LISTENER TRACKING
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Call this when the local player spawns or the camera changes.
    /// Sets the reference point for distance checks.
    /// </summary>
    public void SetListener(Transform listener)
    {
        listenerTransform = listener;
    }

    /// <summary>
    /// Public getter for the database, useful for other scripts
    /// that need to query surface types etc.
    /// </summary>
    public SoundDatabase Database => database;
}
