using UnityEngine;
using Unity.Netcode;

// ─────────────────────────────────────────────────────────
// BallistaSoundPlayer.cs — Ballista-specific sounds
//
// Attach to ballista prefab. Handles:
// - Fire (loud, long range)
// - Reload mechanical sounds
// - Impact (from projectile, handled separately)
// - Optional rotation creak
// ─────────────────────────────────────────────────────────

public class BallistaSoundPlayer : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private BallistaController ballistaController;

    [Header("Sound Names (must match SoundDatabase)")]
    [SerializeField] private string fireSound = "Ballista_Fire";
    [SerializeField] private string reloadSound = "Ballista_Reload";
    [SerializeField] private string rotationCreakSound = "Ballista_Creak";

    [Header("Rotation Creak")]
    [Tooltip("Minimum rotation speed (deg/sec) to trigger creak sound")]
    [SerializeField] private float creakThreshold = 10f;
    [SerializeField] private float creakInterval = 1.5f;

    // ─── State ───
    private bool _wasReloading;
    private float _creakTimer;
    private float _lastYaw;

    private void Awake()
    {
        if (ballistaController == null)
            ballistaController = GetComponent<BallistaController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (ballistaController != null && ballistaController.BallistaBase != null)
            _lastYaw = ballistaController.BallistaBase.eulerAngles.y;
    }

    private void Update()
    {
        if (ProximitySoundManager.Instance == null) return;
        if (ballistaController == null) return;

        UpdateReloadSound();
        UpdateRotationCreak();
    }

    private void UpdateReloadSound()
    {
        bool isReloading = ballistaController.IsReloading;

        // Reload just started
        if (isReloading && !_wasReloading)
        {
            ProximitySoundManager.Instance.PlaySound(reloadSound, transform.position);
        }

        _wasReloading = isReloading;
    }

    private void UpdateRotationCreak()
    {
        if (!ballistaController.IsOccupied) return;
        if (ballistaController.BallistaBase == null) return;

        float currentYaw = ballistaController.BallistaBase.eulerAngles.y;
        float rotationSpeed = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, currentYaw)) / Mathf.Max(Time.deltaTime, 0.001f);
        _lastYaw = currentYaw;

        if (rotationSpeed < creakThreshold)
        {
            _creakTimer = 0f;
            return;
        }

        _creakTimer -= Time.deltaTime;
        if (_creakTimer <= 0f)
        {
            ProximitySoundManager.Instance.PlaySound(rotationCreakSound, transform.position);
            _creakTimer = creakInterval;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC API — Call from BallistaController
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Call when ballista fires. Should be called from NotifyFireClientRpc
    /// so all clients hear it.
    /// </summary>
    public void OnFire()
    {
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(fireSound, transform.position);
    }
}
