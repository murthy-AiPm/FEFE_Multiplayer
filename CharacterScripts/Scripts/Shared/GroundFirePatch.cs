using System;
using Unity.Netcode;
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

    [Header("Debug")]
    [Tooltip("Logs every OnTriggerStay hit. Use to verify trigger geometry / layer matrix while diagnosing 'zombies don't take damage'.")]
    [SerializeField] private bool debugLogging;

    public Action<GroundFirePatch> OnReturnToPool;

    private GroundFirePatchConfig _config;
    private NetworkObjectReference _sourceRef;
    private float _age;
    private float _tickTimer;
    private bool _isServer;
    private bool _active;
    private float _baseFadeFactor = 1f;

    private void Awake()
    {
        if (decal != null) _baseFadeFactor = decal.fadeFactor;

        // OnTrigger* requires at least one of the two parties to have a Rigidbody.
        // Zombies/NPCs in this project are NavMesh-driven with no Rigidbody, so we add a
        // kinematic one here so the trigger always dispatches regardless of who walks in.
        // Kinematic = no forces, no gravity, won't move — it's purely a dispatch enabler.
        if (GetComponent<Rigidbody>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    /// <summary>
    /// Server: pass isServer=true to enable damage. Client visual-only: isServer=false.
    /// Configured by GroundFirePool.SpawnAt -> set externally before activation.
    /// </summary>
    public void Activate(GroundFirePatchConfig config, NetworkObjectReference sourceRef)
    {
        _config = config;
        _sourceRef = sourceRef;
        _age = 0f;
        _tickTimer = 0f;
        _active = true;

        // Server flag: anyone running NetworkManager as host/server applies damage.
        _isServer = Unity.Netcode.NetworkManager.Singleton != null
                    && Unity.Netcode.NetworkManager.Singleton.IsServer;

        if (decal != null) decal.fadeFactor = _baseFadeFactor;
        if (vfxRoot != null) vfxRoot.SetActive(true);

        if (debugLogging)
        {
            Debug.Log($"[GroundFire] Activated. isServer={_isServer} pos={transform.position}", this);

            // Direct physics probe: what does Unity think is inside our trigger volume right now?
            // Bypasses OnTriggerStay so we can tell whether the issue is geometry/layers vs trigger
            // dispatch. Uses ALL layers so layer-matrix mismatches show up here as "found something
            // that the trigger isn't dispatching".
            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                Vector3 worldCenter = transform.TransformPoint(box.center);
                Vector3 halfExtents = Vector3.Scale(box.size, transform.lossyScale) * 0.5f;
                var hits = Physics.OverlapBox(worldCenter, halfExtents, transform.rotation, ~0, QueryTriggerInteraction.Collide);
                Debug.Log($"[GroundFire] Probe found {hits.Length} colliders in trigger volume (center={worldCenter}, halfExt={halfExtents}, allLayers).", this);
                for (int i = 0; i < hits.Length; i++)
                {
                    var h = hits[i];
                    Debug.Log($"[GroundFire]   [{i}] {h.name} layer={LayerMask.LayerToName(h.gameObject.layer)}({h.gameObject.layer}) isTrigger={h.isTrigger} hasRB={h.attachedRigidbody != null}", h);
                }
            }
        }
    }

    /// <summary>
    /// Editor/Inspector helper: right-click the GroundFirePatch component header → "Activate For Testing"
    /// (works in Play mode). Lets you drop a GroundFirePatch.prefab into a scene and turn it on without
    /// involving the dragon, GroundFireSpawner, or the pool. Source NetObj is empty so BurnStatus self-
    /// immunity won't filter anything out. Tweak the [SerializeField] debug values below to taste.
    /// </summary>
    [Header("Debug — Standalone Activation")]
    [SerializeField] private float debugLifetime = 10f;
    [SerializeField] private float debugFadeDuration = 1.2f;
    [SerializeField] private float debugDamagePerTick = 5f;
    [SerializeField] private float debugTickInterval = 0.5f;
    [SerializeField] private float debugBurnTimeOnContact = 1.5f;

    [ContextMenu("Activate For Testing")]
    private void ActivateForTesting()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[GroundFire] Activate For Testing only works in Play mode.", this);
            return;
        }
        Activate(new GroundFirePatchConfig
        {
            lifetime = debugLifetime,
            fadeDuration = debugFadeDuration,
            damagePerTick = debugDamagePerTick,
            tickInterval = debugTickInterval,
            burnTimeOnContact = debugBurnTimeOnContact,
        }, default);
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
        if (debugLogging)
        {
            var burnDbg = other.GetComponentInParent<BurnStatus>();
            Debug.Log($"[GroundFire] [patch={name}#{GetInstanceID()}] OnTriggerStay: {other.name} | active={_active} isServer={_isServer} hasBurnStatus={burnDbg != null}", this);
        }

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
            burn.Ignite(_config.burnTimeOnContact, _sourceRef);
        }
    }
}
