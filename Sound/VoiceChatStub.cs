using UnityEngine;
using Unity.Netcode;

// ─────────────────────────────────────────────────────────
// VoiceChatStub.cs — Placeholder for proximity voice chat
//
// Stub implementation with the same API that Vivox/Agora
// will eventually use. All methods log to console.
// Replace method bodies when integrating a real voice SDK.
//
// Attach to player prefab or a persistent manager object.
// ─────────────────────────────────────────────────────────

public class VoiceChatStub : NetworkBehaviour
{
    [Header("Proximity Settings (configurable)")]
    [Tooltip("Distance at which voice is full volume")]
    [SerializeField] private float fullVolumeDistance = 20f;

    [Tooltip("Distance at which voice fades to zero")]
    [SerializeField] private float maxDistance = 60f;

    [Tooltip("Distance beyond which voice is completely silent")]
    [SerializeField] private float cutoffDistance = 80f;

    [Header("Options")]
    [SerializeField] private bool muteByDefault = false;
    [SerializeField] private bool pushToTalk = false;
    [SerializeField] private KeyCode pushToTalkKey = KeyCode.T;

    // ─── State ───
    private bool _isConnected;
    private bool _isMuted;
    private Vector3 _lastPosition;

    public bool IsConnected => _isConnected;
    public bool IsMuted => _isMuted;

    // ═══════════════════════════════════════════════════════
    //  PUBLIC API — Same interface real SDK will use
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Join the voice chat channel. Call when player joins game.
    /// </summary>
    public void JoinChannel(string channelName = "default")
    {
        Debug.Log($"[VoiceChatStub] JoinChannel('{channelName}') — STUB. Integrate Vivox/Agora here.");
        _isConnected = true;
        _isMuted = muteByDefault;

        // TODO: Replace with real SDK call
        // VivoxService.Instance.JoinPositionalChannelAsync(channelName, ...);
    }

    /// <summary>
    /// Leave the voice chat channel. Call when player leaves game.
    /// </summary>
    public void LeaveChannel()
    {
        Debug.Log("[VoiceChatStub] LeaveChannel() — STUB.");
        _isConnected = false;

        // TODO: Replace with real SDK call
    }

    /// <summary>
    /// Update the player's 3D position for proximity voice.
    /// Call every frame or on movement.
    /// </summary>
    public void SetProximityPosition(Vector3 worldPosition)
    {
        if (!_isConnected) return;
        _lastPosition = worldPosition;

        // TODO: Replace with real SDK call
        // VivoxService.Instance.Set3DPosition(position, ...);
    }

    /// <summary>
    /// Mute/unmute local microphone.
    /// </summary>
    public void SetMuted(bool muted)
    {
        _isMuted = muted;
        Debug.Log($"[VoiceChatStub] SetMuted({muted}) — STUB.");

        // TODO: Replace with real SDK call
    }

    /// <summary>
    /// Toggle mute state.
    /// </summary>
    public void ToggleMute()
    {
        SetMuted(!_isMuted);
    }

    /// <summary>
    /// Set master volume for incoming voice (0-1).
    /// </summary>
    public void SetVoiceVolume(float volume)
    {
        Debug.Log($"[VoiceChatStub] SetVoiceVolume({volume:F2}) — STUB.");

        // TODO: Replace with real SDK call
    }

    // ─── Update ───

    private void Update()
    {
        if (!IsOwner) return;
        if (!_isConnected) return;

        // Update proximity position
        SetProximityPosition(transform.position);

        // Push-to-talk
        if (pushToTalk)
        {
            bool talking = Input.GetKey(pushToTalkKey);
            // TODO: Set transmit state based on key
        }
    }
}
