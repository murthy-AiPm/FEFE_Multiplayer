using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local pool of GroundFirePatch instances. One instance lives on the host (server-authoritative,
/// patches have active damage triggers) and one on each client (visual-only, decal + fade).
/// Same prefab; GroundFirePatch internally gates damage logic by IsServer.
///
/// Designed as a singleton-per-scene for simplicity. GroundFireSpawner finds it via Instance.
/// </summary>
public class GroundFirePool : MonoBehaviour
{
    public static GroundFirePool Instance { get; private set; }

    [Header("Pool")]
    [Tooltip("Patch prefab. Must contain a GroundFirePatch component, a URP DecalProjector, and a trigger collider.")]
    [SerializeField] private GroundFirePatch patchPrefab;
    [Tooltip("Number of patches prewarmed. Overflow recycles oldest.")]
    [SerializeField] private int poolSize = 64;

    private readonly Queue<GroundFirePatch> _idle = new Queue<GroundFirePatch>();
    private readonly LinkedList<GroundFirePatch> _active = new LinkedList<GroundFirePatch>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (patchPrefab == null)
        {
            Debug.LogError("[GroundFirePool] patchPrefab not assigned.", this);
            return;
        }

        for (int i = 0; i < poolSize; i++)
        {
            var p = Instantiate(patchPrefab, transform);
            p.gameObject.SetActive(false);
            p.OnReturnToPool = ReturnToPool;
            _idle.Enqueue(p);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Spawn a patch at the given world position/normal. Returns the patch (or null if pool exhausted).
    /// </summary>
    public GroundFirePatch SpawnAt(Vector3 position, Vector3 normal, GroundFirePatchConfig config, ulong sourceOwnerId)
    {
        GroundFirePatch patch;
        if (_idle.Count > 0)
        {
            patch = _idle.Dequeue();
        }
        else if (_active.Count > 0)
        {
            // Recycle oldest
            patch = _active.First.Value;
            _active.RemoveFirst();
            patch.ForceExpire();
        }
        else
        {
            return null;
        }

        patch.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.forward, normal));
        patch.gameObject.SetActive(true);
        patch.Activate(config, sourceOwnerId);
        _active.AddLast(patch);
        return patch;
    }

    private void ReturnToPool(GroundFirePatch patch)
    {
        _active.Remove(patch);
        patch.gameObject.SetActive(false);
        _idle.Enqueue(patch);
    }
}

/// <summary>
/// Tuning bundle passed from spawner to pool. Keeps patch behavior config-driven so the
/// spawner owns all tuning values (Inspector-tweakable on the dragon).
/// </summary>
public struct GroundFirePatchConfig
{
    public float lifetime;
    public float fadeDuration;
    public float damagePerTick;
    public float tickInterval;
    public float burnTimeOnContact;
}
