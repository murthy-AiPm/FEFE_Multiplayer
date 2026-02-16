using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Simple training dummy. Takes damage, resets health after a delay.
/// Add a Collider so HitboxController can detect it.
/// </summary>
public class TrainingDummy : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private VitalManager vitalManager;

    [Header("Settings")]
    [SerializeField] private float respawnDelay = 5f;
    [SerializeField] private bool autoReset = true;

    private float _resetTimer;
    private bool _isDead;
    [Header("Debug (read only)")]
    [SerializeField] private float currentHealth;

    private void LateUpdate()
    {
        if (vitalManager != null)
        {
            var health = vitalManager.GetVital("health");
            if (health != null)
                currentHealth = health.Current;
        }
    }
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (vitalManager != null)
        {
            vitalManager.OnVitalChanged += OnVitalChanged;
            vitalManager.OnDeath += OnDeath;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (vitalManager != null)
        {
            vitalManager.OnVitalChanged -= OnVitalChanged;
            vitalManager.OnDeath -= OnDeath;
        }
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer) return;

        if (_isDead && autoReset)
        {
            _resetTimer -= Time.deltaTime;
            if (_resetTimer <= 0f)
            {
                vitalManager.ResetAllVitals();
                _isDead = false;
                Debug.Log("[TrainingDummy] Reset!");
            }
        }
    }

    private void OnVitalChanged(string vitalID, float newVal, float oldVal)
    {
        float damage = oldVal - newVal;
        if (damage > 0)
            Debug.Log($"[TrainingDummy] Hit! {vitalID}: {oldVal:F0} → {newVal:F0} (-{damage:F0})");
    }

    private void OnDeath()
    {
        Debug.Log("[TrainingDummy] Destroyed!");
        _isDead = true;
        _resetTimer = respawnDelay;
    }
}