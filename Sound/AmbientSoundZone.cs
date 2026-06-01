using System.Collections.Generic;
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

    [Header("Debug")]
    [SerializeField] private bool debugLogs;

    [Header("Safety Checks")]
    [Tooltip("How often to verify tracked local occupants are still inside the zone. Catches respawn/teleport cases where OnTriggerExit does not fire.")]
    [SerializeField] private float occupantCheckInterval = 0.25f;

    [Tooltip("Extra tolerance around the trigger when checking whether a tracked occupant has left by teleport/respawn.")]
    [SerializeField] private float containmentPadding = 1f;

    // ─── State ───
    private AudioSource _source;
    private Collider _zoneCollider;
    private float _targetVolume;
    private float _currentVolume;
    private float _occupantCheckTimer;
    private readonly Dictionary<Transform, int> _insideRoots = new Dictionary<Transform, int>();
    private static readonly List<Transform> s_rootsToRemove = new List<Transform>();

    private void Awake()
    {
        // Ensure collider is trigger
        _zoneCollider = GetComponent<Collider>();
        _zoneCollider.isTrigger = true;

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

        UpdateTrackedOccupants();

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
        bool accepted = IsLocalPlayerOrMount(other);
        if (debugLogs)
            Debug.Log($"[AmbientSoundZone] ENTER zone={name} other={other.name} root={other.transform.root.name} layer={LayerMask.LayerToName(other.gameObject.layer)} accepted={accepted}");

        if (!accepted) return;

        RegisterOccupant(other.transform.root);

        if (debugLogs)
            Debug.Log($"[AmbientSoundZone] FADE IN zone={name} occupants={_insideRoots.Count} targetVolume={_targetVolume:F2}");
    }

    private void OnTriggerExit(Collider other)
    {
        bool accepted = IsLocalPlayerOrMount(other);
        if (debugLogs)
            Debug.Log($"[AmbientSoundZone] EXIT zone={name} other={other.name} root={other.transform.root.name} layer={LayerMask.LayerToName(other.gameObject.layer)} accepted={accepted}");

        UnregisterOccupant(other.transform.root);
    }

    private bool IsLocalPlayerOrMount(Collider col)
    {
        // ── Direct player check ──
        // Check for ClientPlayerMove — the NetworkBehaviour on the player root.
        // Arrows and other projectiles will never have this component.
        var playerMove = col.GetComponentInParent<ClientPlayerMove>();
        if (playerMove != null && playerMove.IsOwner) return true;

        var newPlayerDriver = col.GetComponentInParent<ClientAuthoritativePlayerDriver>();
        if (newPlayerDriver != null && newPlayerDriver.IsOwner) return true;

        var dragonFlight = col.GetComponentInParent<DragonFlightController>();
        if (dragonFlight != null && dragonFlight.IsOwner) return true;

        var dragonGround = col.GetComponentInParent<DragonGroundController>();
        if (dragonGround != null && dragonGround.IsOwner) return true;

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

    private void RegisterOccupant(Transform root)
    {
        if (root == null) return;

        _insideRoots.TryGetValue(root, out int count);
        _insideRoots[root] = count + 1;
        _targetVolume = maxVolume;
    }

    private void UnregisterOccupant(Transform root)
    {
        if (root == null) return;
        if (!_insideRoots.TryGetValue(root, out int count)) return;

        if (count <= 1)
            _insideRoots.Remove(root);
        else
            _insideRoots[root] = count - 1;

        RefreshTargetVolume();
    }

    private void UpdateTrackedOccupants()
    {
        _occupantCheckTimer -= Time.deltaTime;
        if (_occupantCheckTimer > 0f) return;

        _occupantCheckTimer = Mathf.Max(0.05f, occupantCheckInterval);
        if (_insideRoots.Count == 0) return;

        s_rootsToRemove.Clear();
        foreach (var pair in _insideRoots)
        {
            Transform root = pair.Key;
            if (root == null || !root.gameObject.activeInHierarchy || !IsInsideZone(root.position) || !IsTrackedRootStillLocalOccupant(root))
                s_rootsToRemove.Add(root);
        }

        for (int i = 0; i < s_rootsToRemove.Count; i++)
        {
            Transform root = s_rootsToRemove[i];
            _insideRoots.Remove(root);

            if (debugLogs)
                Debug.Log($"[AmbientSoundZone] PRUNE zone={name} root={(root != null ? root.name : "null")} occupants={_insideRoots.Count}");
        }

        RefreshTargetVolume();
    }

    private bool IsTrackedRootStillLocalOccupant(Transform root)
    {
        if (root == null) return false;

        var playerMove = root.GetComponentInChildren<ClientPlayerMove>();
        if (playerMove != null && playerMove.IsOwner) return true;

        var newPlayerDriver = root.GetComponentInChildren<ClientAuthoritativePlayerDriver>();
        if (newPlayerDriver != null && newPlayerDriver.IsOwner) return true;

        var dragonFlight = root.GetComponentInChildren<DragonFlightController>();
        if (dragonFlight != null && dragonFlight.IsOwner) return true;

        var dragonGround = root.GetComponentInChildren<DragonGroundController>();
        if (dragonGround != null && dragonGround.IsOwner) return true;

        var mountable = root.GetComponentInChildren<MountableEntity>();
        if (mountable != null && mountable.IsMounted)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && mountable.RiderId == nm.LocalClientId)
                return true;
        }

        return false;
    }

    private bool IsInsideZone(Vector3 position)
    {
        if (_zoneCollider == null) return false;
        if (_zoneCollider.bounds.Contains(position)) return true;

        Vector3 closest = _zoneCollider.ClosestPoint(position);
        return (closest - position).sqrMagnitude <= containmentPadding * containmentPadding;
    }

    private void RefreshTargetVolume()
    {
        _targetVolume = _insideRoots.Count > 0 ? maxVolume : 0f;

        if (debugLogs && _insideRoots.Count == 0)
            Debug.Log($"[AmbientSoundZone] FADE OUT zone={name}");
    }

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
