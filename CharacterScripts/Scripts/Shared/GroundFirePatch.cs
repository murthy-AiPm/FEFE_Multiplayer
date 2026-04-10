using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// A single ground fire patch. Lives in GroundFirePool. On the host, applies burn damage to
/// anything entering the trigger. On all clients (including host), fades a URP DecalProjector
/// from full → 0 over fadeDuration. Damage logic is gated by a runtime IsServer flag set by
/// the spawner via Activate().
/// </summary>
[RequireComponent(typeof(Collider))]
public class GroundFirePatch : MonoBehaviour
{
    [Tooltip("URP Decal Projector for the scorch/fire decal. Required for visual fade.")]
    [SerializeField] private DecalProjector decal;
    [Tooltip("Optional VFX root (particles) toggled with the patch.")]
    [SerializeField] private GameObject vfxRoot;

    public Action<GroundFirePatch> OnReturnToPool;

    private GroundFirePatchConfig _config;
    private ulong _sourceOwnerId;
    private float _age;
    private float _tickTimer;
    private bool _isServer;
    private bool _active;
    private float _baseFadeFactor = 1f;

    private void Awake()
    {
        if (decal != null) _baseFadeFactor = decal.fadeFactor;
    }

    /// <summary>
    /// Server: pass isServer=true to enable damage. Client visual-only: isServer=false.
    /// Configured by GroundFirePool.SpawnAt -> set externally before activation.
    /// </summary>
    public void Activate(GroundFirePatchConfig config, ulong sourceOwnerId)
    {
        _config = config;
        _sourceOwnerId = sourceOwnerId;
        _age = 0f;
        _tickTimer = 0f;
        _active = true;

        // Server flag: anyone running NetworkManager as host/server applies damage.
        _isServer = Unity.Netcode.NetworkManager.Singleton != null
                    && Unity.Netcode.NetworkManager.Singleton.IsServer;

        if (decal != null) decal.fadeFactor = _baseFadeFactor;
        if (vfxRoot != null) vfxRoot.SetActive(true);
    }

    public void ForceExpire()
    {
        _active = false;
        if (vfxRoot != null) vfxRoot.SetActive(false);
        OnReturnToPool?.Invoke(this);
    }

    private void Update()
    {
        if (!_active) return;

        _age += Time.deltaTime;

        // Visual fade across full lifetime: hold full opacity until fade window starts
        if (decal != null)
        {
            float fadeStart = Mathf.Max(0f, _config.lifetime - _config.fadeDuration);
            if (_age >= fadeStart && _config.fadeDuration > 0f)
            {
                float t = Mathf.Clamp01((_age - fadeStart) / _config.fadeDuration);
                decal.fadeFactor = Mathf.Lerp(_baseFadeFactor, 0f, t);
            }
        }

        if (_age >= _config.lifetime)
        {
            ForceExpire();
            return;
        }

        // Server-only: damage tick (handled via OnTriggerStay below; nothing to do here per-frame)
    }

    private void OnTriggerStay(Collider other)
    {
        if (!_active || !_isServer) return;

        _tickTimer -= Time.deltaTime;
        // Note: OnTriggerStay is called per-other-collider per physics step. We gate by a
        // shared timer so multiple overlapping colliders don't multi-tick. The timer ticks
        // even on no-stay frames via Update would be cleaner — but BurnStatus.Ignite is the
        // primary damage path, so per-contact ticking here is just a safety net.
        if (_tickTimer > 0f) return;
        _tickTimer = _config.tickInterval;

        var burn = other.GetComponentInParent<BurnStatus>();
        if (burn != null)
        {
            burn.Ignite(_config.burnTimeOnContact, _sourceOwnerId);
        }
    }
}
