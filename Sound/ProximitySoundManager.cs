using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Audio;

public class ProximitySoundManager : NetworkBehaviour
{
    public static ProximitySoundManager Instance { get; private set; }

    [Header("Database")]
    [SerializeField] private SoundDatabase database;

    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioMixerGroup footstepsMixerGroup;
    [SerializeField] private AudioMixerGroup combatMixerGroup;
    [SerializeField] private AudioMixerGroup loudMixerGroup;
    [SerializeField] private AudioMixerGroup ambientMixerGroup;
    [SerializeField] private AudioMixerGroup uiMixerGroup;

    [Header("Audio Source Pool")]
    [SerializeField] private int poolSize = 32;
    [SerializeField] private AudioRolloffMode defaultRolloffMode = AudioRolloffMode.Logarithmic;
    [Tooltip("Duration in seconds to fade out clip volume before it ends, to prevent waveform pop.")]
    [SerializeField] private float clipFadeOutDuration = 0.02f;

    [Header("Listener")]
    [SerializeField] private Transform listenerTransform;

    private List<AudioSource> _pool;
    private int _poolIndex;
    private Dictionary<string, int> _soundNameToId;
    private Dictionary<int, string> _idToSoundName;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
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
        FindListener();
    }

    private void FindListener()
    {
        if (listenerTransform != null) return;
        var listener = FindObjectOfType<AudioListener>();
        if (listener != null)
        {
            listenerTransform = listener.transform;
        }
        else
        {
            Debug.LogWarning("[ProximitySoundManager] No AudioListener found in scene!");
        }
    }

    private void BuildSoundIdMaps()
    {
        _soundNameToId = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        _idToSoundName = new Dictionary<int, string>();
        if (database == null) return;
    }

    private int GetSoundId(string soundName) => Animator.StringToHash(soundName);

    private void CreatePool()
    {
        _pool = new List<AudioSource>(poolSize);
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject($"PooledAudio_{i}");
            go.transform.SetParent(transform);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = defaultRolloffMode;
            source.dopplerLevel = 0.3f;
            source.spread = 30f;
            source.loop = false;
            // Keep always active — deactivating causes Unity to drop outputAudioMixerGroup
            _pool.Add(source);
        }
    }

    private AudioSource GetPooledSource()
    {
        for (int i = 0; i < poolSize; i++)
        {
            int idx = (_poolIndex + i) % poolSize;
            if (!_pool[idx].isPlaying) { _poolIndex = (idx + 1) % poolSize; return _pool[idx]; }
        }
        _poolIndex = (_poolIndex + 1) % poolSize;
        var stolen = _pool[_poolIndex];
        stolen.Stop();
        return stolen;
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════

    public void PlaySound(string soundName, Vector3 worldPosition)
    {
        if (database == null || string.IsNullOrEmpty(soundName)) return;
        var entry = database.GetSound(soundName);
        if (entry == null) { Debug.LogWarning($"[ProximitySoundManager] Sound not found: '{soundName}'"); return; }
        if (entry.category == SoundCategory.Ambient) { PlaySoundLocal(soundName, worldPosition); return; }
        int soundId = GetSoundId(soundName);
        if (IsServer) PlaySoundClientRpc(soundId, worldPosition, soundName);
        else PlaySoundServerRpc(soundId, worldPosition, soundName);
    }

    public void PlaySoundLocal(string soundName, Vector3 worldPosition)
    {
        if (database == null) return;
        var entry = database.GetSound(soundName);
        if (entry == null) return;
        PlayClipAtPosition(entry, worldPosition);
    }

    public void PlayFootstep(string surfaceType, Vector3 worldPosition, SoundCategory category = SoundCategory.Quiet, string creatureType = "")
    {
        if (database == null) return;
        var footstepSet = database.GetFootstepSet(surfaceType, creatureType);
        if (footstepSet == null) return;
        var clip = footstepSet.GetRandomClip();
        if (clip == null) return;
        int surfaceId = GetSoundId("Footstep_" + creatureType + surfaceType);
        if (IsServer)
        {
            PlayFootstepLocal(surfaceType, worldPosition, creatureType, category);
            var clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = GetRemoteClientIds() }
            };
            if (GetRemoteClientIds().Count > 0)
                PlayFootstepClientRpc(surfaceId, worldPosition, surfaceType, creatureType, (int)category, clientRpcParams);
        }
        else PlayFootstepServerRpc(surfaceId, worldPosition, surfaceType, creatureType, (int)category);
    }

    public void PlayImpact(string attackerMaterial, string targetMaterial, Vector3 worldPosition)
    {
        if (database == null) { Debug.LogWarning("[ProximitySoundManager] PlayImpact: database is null"); return; }
        var impactEntry = database.GetImpactSound(attackerMaterial, targetMaterial);
        if (impactEntry == null) { Debug.LogWarning($"[ProximitySoundManager] PlayImpact: no entry for '{attackerMaterial}|{targetMaterial}'"); return; }
        //Debug.Log($"[ProximitySoundManager] PlayImpact: found '{attackerMaterial}|{targetMaterial}', IsServer={IsServer}, clips={impactEntry.impactClips?.Length}");
        int impactId = GetSoundId($"Impact_{attackerMaterial}_{targetMaterial}");
        if (IsServer) PlayImpactClientRpc(impactId, worldPosition, attackerMaterial, targetMaterial);
        else PlayImpactServerRpc(impactId, worldPosition, attackerMaterial, targetMaterial);
    }

    // ═══════════════════════════════════════════════════════
    //  NETWORK RPCs
    // ═══════════════════════════════════════════════════════

    [ServerRpc(RequireOwnership = false)]
    private void PlaySoundServerRpc(int soundId, Vector3 position, string soundName)
        => PlaySoundClientRpc(soundId, position, soundName);

    [ClientRpc]
    private void PlaySoundClientRpc(int soundId, Vector3 position, string soundName)
    {
        var entry = database.GetSound(soundName);
        if (entry == null) return;
        if (entry.category != SoundCategory.Global && !IsInRange(entry, position)) return;
        PlayClipAtPosition(entry, position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayFootstepServerRpc(int surfaceId, Vector3 position, string surfaceType, string creatureType = "", int category = (int)SoundCategory.Quiet)
        => PlayFootstepClientRpc(surfaceId, position, surfaceType, creatureType, category);

    [ClientRpc]
    private void PlayFootstepClientRpc(int surfaceId, Vector3 position, string surfaceType, string creatureType = "", int category = (int)SoundCategory.Quiet, ClientRpcParams clientRpcParams = default)
    {
        var catDist = database.GetCategoryDistance((SoundCategory)category);
        if (catDist != null && GetDistanceToListener(position) > catDist.maxDistance) return;
        var footstepSet = database.GetFootstepSet(surfaceType, creatureType);
        if (footstepSet == null) return;
        var clip = footstepSet.GetRandomClip();
        if (clip == null) return;
        float volume = footstepSet.GetRandomVolume();
        float pitch = footstepSet.GetRandomPitch();
        float minDist = catDist?.minDistance ?? 2f;
        float maxDist = catDist?.maxDistance ?? 30f;
        AudioRolloffMode rolloff = catDist?.rolloffMode ?? defaultRolloffMode;
        PlayClipRaw(clip, position, volume, pitch, minDist, maxDist, rolloff, null, (SoundCategory)category);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayImpactServerRpc(int impactId, Vector3 position, string attackerMat, string targetMat)
        => PlayImpactClientRpc(impactId, position, attackerMat, targetMat);

    [ClientRpc]
    private void PlayImpactClientRpc(int impactId, Vector3 position, string attackerMat, string targetMat)
    {
        var combatDist = database.GetCategoryDistance(SoundCategory.Combat);
        float dist = GetDistanceToListener(position);
        if (combatDist != null && dist > combatDist.maxDistance)
        {
            Debug.LogWarning($"[ProximitySoundManager] PlayImpactClientRpc: out of range dist={dist:F1} max={combatDist.maxDistance}");
            return;
        }
        var impactEntry = database.GetImpactSound(attackerMat, targetMat);
        if (impactEntry == null) { Debug.LogWarning($"[ProximitySoundManager] PlayImpactClientRpc: no entry for '{attackerMat}|{targetMat}'"); return; }
        var clip = impactEntry.GetRandomClip();
        if (clip == null) { Debug.LogWarning($"[ProximitySoundManager] PlayImpactClientRpc: clip is null for '{attackerMat}|{targetMat}'"); return; }
        float volume = impactEntry.GetRandomVolume();//
        float pitch = impactEntry.GetRandomPitch();
        float minDist = combatDist?.minDistance ?? 5f;
        float maxDist = combatDist?.maxDistance ?? 50f;
        AudioRolloffMode rolloff = combatDist?.rolloffMode ?? defaultRolloffMode;
       // Debug.Log($"[ProximitySoundManager] PlayImpactClientRpc: playing '{clip.name}' vol={volume:F2} pitch={pitch:F2} dist={dist:F1} minDist={minDist} maxDist={maxDist} listenerPos={listenerTransform?.position}");
        PlayClipRaw(clip, position, volume, pitch, minDist, maxDist, rolloff, null, SoundCategory.Combat);
    }

    // ═══════════════════════════════════════════════════════
    //  INTERNAL
    // ═══════════════════════════════════════════════════════

    private bool IsInRange(SoundEntry entry, Vector3 position)
    {
        database.GetDistanceForSound(entry, out _, out float maxDist);
        return GetDistanceToListener(position) <= maxDist;
    }

    private float GetDistanceToListener(Vector3 position)
    {
        if (listenerTransform == null) FindListener();
        if (listenerTransform == null) { Debug.LogWarning("[ProximitySoundManager] GetDistanceToListener: no listener!"); return float.MaxValue; }
        return Vector3.Distance(listenerTransform.position, position);
    }

    private void PlayClipAtPosition(SoundEntry entry, Vector3 position)
    {
        var clip = entry.GetRandomClip();
        if (clip == null) return;
        database.GetDistanceForSound(entry, out float minDist, out float maxDist);
        AudioRolloffMode rolloff = entry.overrideRolloff
            ? entry.customRolloffMode
            : (database.GetCategoryDistance(entry.category)?.rolloffMode ?? defaultRolloffMode);
        AnimationCurve curve = (entry.overrideRolloff && entry.customRolloffMode == AudioRolloffMode.Custom)
            ? entry.customRolloffCurve : null;
        //Debug.Log($"[ProximitySoundManager] PlayClipAtPosition: sound={entry.soundName} overrideRolloff={entry.overrideRolloff} rolloff={rolloff} curveNull={curve == null} curveKeys={curve?.keys.Length}");
        PlayClipRaw(clip, position, entry.GetRandomVolume(), entry.GetRandomPitch(), minDist, maxDist, rolloff, curve, entry.category);
    }

    public AudioMixerGroup GetMixerGroup(SoundCategory category) => category switch
    {
        SoundCategory.Quiet   => footstepsMixerGroup,
        SoundCategory.Combat  => combatMixerGroup,
        SoundCategory.Loud    => loudMixerGroup,
        SoundCategory.Ambient => ambientMixerGroup,
        SoundCategory.Global  => uiMixerGroup,
        _                     => null
    };

    private void PlayClipRaw(AudioClip clip, Vector3 position, float volume, float pitch, float minDist, float maxDist, AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic, AnimationCurve customCurve = null, SoundCategory category = SoundCategory.Combat)
    {
        if (clip == null) return;
        if (volume <= 0f) return;
        var source = GetPooledSource();
        source.transform.position = position;
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.minDistance = minDist;
        source.maxDistance = maxDist;
        source.rolloffMode = rolloffMode;
        if (rolloffMode == AudioRolloffMode.Custom && customCurve != null)
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, customCurve);
        var mixerGroup = GetMixerGroup(category);
        if (mixerGroup != null) source.outputAudioMixerGroup = mixerGroup;
        source.spatialBlend = 1f;
        source.loop = false;
        //Debug.Log($"[ProximitySoundManager] PlayClipRaw: clip={clip.name} vol={volume:F2} pitch={pitch:F2} pos={position} category={category}");
        source.Play();
        float adjustedLength = clip.length / Mathf.Max(pitch, 0.1f);
        StartCoroutine(FadeOutAndReturn(source, volume, adjustedLength));
    }

    private System.Collections.IEnumerator FadeOutAndReturn(AudioSource source, float originalVolume, float clipLength)
    {
        float fadeStart = clipLength - clipFadeOutDuration;
        if (fadeStart > 0f)
            yield return new WaitForSeconds(fadeStart);

        // Fade volume to zero over clipFadeOutDuration
        float elapsed = 0f;
        while (elapsed < clipFadeOutDuration && source != null && source.isPlaying)
        {
            source.volume = Mathf.Lerp(originalVolume, 0f, elapsed / clipFadeOutDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (source != null)
        {
            source.Stop();
            source.volume = originalVolume; // reset for pool reuse
        }
    }

    public void SetListener(Transform listener) => listenerTransform = listener;
    public SoundDatabase Database => database;

    public void PlayFootstepDirect(string surfaceType, Vector3 worldPosition, SoundCategory category = SoundCategory.Quiet, string creatureType = "")
    {
        PlayFootstepLocal(surfaceType, worldPosition, creatureType, category);
    }

    private void PlayFootstepLocal(string surfaceType, Vector3 position, string creatureType, SoundCategory category = SoundCategory.Quiet)
    {
        var footstepSet = database.GetFootstepSet(surfaceType, creatureType);
        if (footstepSet == null) return;
        var clip = footstepSet.GetRandomClip();
        if (clip == null) return;
        var catDist = database.GetCategoryDistance(category);
        if (catDist != null && GetDistanceToListener(position) > catDist.maxDistance) return;
        float minDist = catDist?.minDistance ?? 2f;
        float maxDist = catDist?.maxDistance ?? 30f;
        AudioRolloffMode rolloff = catDist?.rolloffMode ?? defaultRolloffMode;
        PlayClipRaw(clip, position, footstepSet.GetRandomVolume(), footstepSet.GetRandomPitch(), minDist, maxDist, rolloff, null, category);
    }

    private List<ulong> GetRemoteClientIds()
    {
        var ids = new List<ulong>();
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id != NetworkManager.Singleton.LocalClientId)
                ids.Add(id);
        }
        return ids;
    }
}
