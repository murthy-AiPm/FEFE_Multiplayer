using UnityEngine;

// ─────────────────────────────────────────────────────────
// AnimalSoundPlayer.cs — Reusable animal sound component
//
// Attach to any animal prefab alongside its AI script.
// The AI calls the public methods on state transitions.
// Idle ambient sounds run automatically on a random timer.
//
// All sounds routed through ProximitySoundManager so all
// nearby clients hear them (server fires, RPC broadcasts).
//
// Supported states:
//   Idle (ambient vocalizations on random timer)
//   Attack
//   Hit (being attacked)
//   Death
//   Sleep (optional — call PlaySleepSound() when sleep state begins)
//
// Adding a new state (e.g. Aggro):
//   1. Add [SerializeField] private string aggroSound field
//   2. Add public void PlayAggroSound() method below
//   3. Call it from AI SetState(Aggro)
// ─────────────────────────────────────────────────────────

public class AnimalSoundPlayer : MonoBehaviour
{
    [Header("Sound Names (must match SoundDatabase entries)")]
    [Tooltip("Played randomly while alive. Leave empty to disable.")]
    [SerializeField] private string idleSound = "";

    [Tooltip("Played when entering attack state.")]
    [SerializeField] private string attackSound = "";

    [Tooltip("Played when taking damage.")]
    [SerializeField] private string hitSound = "";

    [Tooltip("Played on death.")]
    [SerializeField] private string deathSound = "";

    [Tooltip("Played when entering sleep state (optional).")]
    [SerializeField] private string sleepSound = "";

    [Header("Idle Sound Timing")]
    [Tooltip("Minimum seconds between idle vocalizations.")]
    [SerializeField] private float idleIntervalMin = 4f;

    [Tooltip("Maximum seconds between idle vocalizations.")]
    [SerializeField] private float idleIntervalMax = 12f;

    // ─── State ───
    private float _idleTimer;
    private bool _isDead;

    private void Start()
    {
        ResetIdleTimer();
    }

    private void Update()
    {
        // Idle sounds run server-side only — AI is server-authoritative
        // so this component is only ticked meaningfully on the server.
        // Non-server instances will have ProximitySoundManager handle playback via RPC.
        if (!IsServer()) return;
        if (_isDead) return;
        if (string.IsNullOrEmpty(idleSound)) return;
        if (ProximitySoundManager.Instance == null) return;

        _idleTimer -= Time.deltaTime;
        if (_idleTimer <= 0f)
        {
            ProximitySoundManager.Instance.PlaySound(idleSound, transform.position);
            ResetIdleTimer();
        }
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC API — Call from AI SetState()
    // ═══════════════════════════════════════════════════════

    /// <summary>Call from AI when entering Attack state.</summary>
    public void PlayAttackSound()
    {
        PlayIfSet(attackSound);
    }

    /// <summary>Call from DamageReceiver.OnDamageReceived subscription in the AI.</summary>
    public void PlayHitSound()
    {
        PlayIfSet(hitSound);
    }

    /// <summary>Call from AI when entering Dead state.</summary>
    public void PlayDeathSound()
    {
        _isDead = true;
        PlayIfSet(deathSound);
    }

    /// <summary>Call from AI when entering Sleep state (if implemented).</summary>
    public void PlaySleepSound()
    {
        PlayIfSet(sleepSound);
    }

    // ─── Helpers ───

    private void PlayIfSet(string soundName)
    {
        if (string.IsNullOrEmpty(soundName)) return;
        if (ProximitySoundManager.Instance == null) return;
        ProximitySoundManager.Instance.PlaySound(soundName, transform.position);
    }

    private void ResetIdleTimer()
    {
        _idleTimer = Random.Range(idleIntervalMin, idleIntervalMax);
    }

    // Checks if this instance is running on the server without requiring NetworkBehaviour
    private bool IsServer()
    {
        var netManager = Unity.Netcode.NetworkManager.Singleton;
        return netManager != null && netManager.IsServer;
    }
}
