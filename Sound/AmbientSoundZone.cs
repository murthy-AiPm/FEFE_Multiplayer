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
    private int _insideCount;  // counts colliders currently inside, not just bool

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
        if (!IsLocalPlayerOrMount(other)) return;

        _insideCount++;
        _targetVolume = maxVolume;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsLocalPlayerOrMount(other)) return;

        _insideCount = Mathf.Max(0, _insideCount - 1);
        if (_insideCount == 0)
            _targetVolume = 0f;
    }

    private bool IsLocalPlayerOrMount(Collider col)
    {
        // ── Direct player check ──
        // Check for ClientPlayerMove — the NetworkBehaviour on the player root.
        // Arrows and other projectiles will never have this component.
        var playerMove = col.GetComponentInParent<ClientPlayerMove>();
        if (playerMove != null && playerMove.IsOwner) return true;

        // ── Mounted horse check ──
        var mountable = col.GetComponentInParent<MountableEntity>();
        if (mountable != null && mountable.IsMounted)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && mountable.RiderId == nm.LocalClientId)
                return true;
        }

        return false;
    }

    // ─── Public API ───

    public void FadeIn()
    {
        _targetVolume = maxVolume;
    }

    public void FadeOut()
    {
        _targetVolume = 0f;
    }

    public void SetVolume(float volume)
    {
        _currentVolume = volume;
        _targetVolume = volume;
        if (_source != null) _source.volume = volume;
    }
}
