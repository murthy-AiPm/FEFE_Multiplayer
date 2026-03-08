using UnityEngine;

// ─────────────────────────────────────────────────────────
// AmbientSoundZone.cs — Local-only ambient sound loops
//
// Attach to a trigger collider in the world. When the local
// player enters, the ambient sound fades in. When they leave,
// it fades out. NOT networked — every client runs their own.
//
// Use for: wind, birds, forest ambiance, river, city sounds.
// Multiple zones can overlap for layered ambiance.
// ─────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider))]
public class AmbientSoundZone : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip ambientClip;

    [Range(0f, 1f)]
    [SerializeField] private float maxVolume = 0.3f;

    [Tooltip("Fade in/out duration in seconds")]
    [SerializeField] private float fadeDuration = 2f;

    [SerializeField] private bool loop = true;

    [Header("3D Settings")]
    [Tooltip("0 = 2D (same everywhere in zone), 1 = 3D (positioned at zone center)")]
    [Range(0f, 1f)]
    [SerializeField] private float spatialBlend = 0f;

    [Header("Random Pitch")]
    [SerializeField] private bool randomizePitch = false;
    [Range(0.8f, 1.2f)]
    [SerializeField] private float pitchMin = 0.95f;
    [Range(0.8f, 1.2f)]
    [SerializeField] private float pitchMax = 1.05f;

    // ─── State ───
    private AudioSource _source;
    private float _targetVolume;
    private float _currentVolume;
    private bool _isInside;

    private void Awake()
    {
        // Ensure collider is trigger
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        // Create audio source
        _source = gameObject.AddComponent<AudioSource>();
        _source.clip = ambientClip;
        _source.loop = loop;
        _source.playOnAwake = false;
        _source.volume = 0f;
        _source.spatialBlend = spatialBlend;

        if (randomizePitch)
            _source.pitch = Random.Range(pitchMin, pitchMax);
    }

    private void Update()
    {
        if (_source == null || ambientClip == null) return;

        // Smooth fade
        _currentVolume = Mathf.MoveTowards(_currentVolume, _targetVolume, (maxVolume / fadeDuration) * Time.deltaTime);
        _source.volume = _currentVolume;

        // Start playing when fading in
        if (_currentVolume > 0f && !_source.isPlaying)
            _source.Play();

        // Stop when fully faded out
        if (_currentVolume <= 0f && _source.isPlaying)
            _source.Stop();
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only respond to the local player's listener
        if (!IsLocalPlayer(other)) return;

        _isInside = true;
        _targetVolume = maxVolume;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsLocalPlayer(other)) return;

        _isInside = false;
        _targetVolume = 0f;
    }

    private bool IsLocalPlayer(Collider col)
    {
        // Check for AudioListener (usually on the camera or player)
        if (col.GetComponentInChildren<AudioListener>() != null) return true;
        if (col.GetComponentInParent<AudioListener>() != null) return true;

        // Check for NetworkBehaviour with IsOwner (covers player directly)
        var netBehaviour = col.GetComponentInParent<Unity.Netcode.NetworkBehaviour>();
        if (netBehaviour != null && netBehaviour.IsOwner) return true;

        // Check if this is a horse that the local player is riding
        var mountable = col.GetComponentInParent<MountableEntity>();
        if (mountable != null && mountable.IsMounted)
        {
            // The horse is mounted — check if the local player is the rider
            // by finding any owner-controlled NetworkBehaviour in its children
            var childNetBehaviours = col.GetComponentsInParent<Unity.Netcode.NetworkBehaviour>();
            foreach (var nb in childNetBehaviours)
            {
                if (nb.IsOwner) return true;
            }

            // Also check children (rider is parented to horse)
            var childNets = col.GetComponentsInChildren<Unity.Netcode.NetworkBehaviour>();
            foreach (var nb in childNets)
            {
                if (nb.IsOwner) return true;
            }
        }

        return false;
    }

    // ─── Public API ───

    /// <summary>
    /// Manually fade in the ambient sound (e.g. for global ambient at game start).
    /// </summary>
    public void FadeIn()
    {
        _targetVolume = maxVolume;
    }

    /// <summary>
    /// Manually fade out the ambient sound.
    /// </summary>
    public void FadeOut()
    {
        _targetVolume = 0f;
    }

    /// <summary>
    /// Set volume directly (no fade).
    /// </summary>
    public void SetVolume(float volume)
    {
        _currentVolume = volume;
        _targetVolume = volume;
        if (_source != null) _source.volume = volume;
    }
}
