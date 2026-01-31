using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Syncs character selections from clients to the server.
/// Place this on a NetworkObject in the CharacterSelect scene.
/// </summary>
public class CharacterSelectionSync : NetworkBehaviour
{
    public static CharacterSelectionSync Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Call this when a client selects a character
    /// </summary>
    public void SyncSelection(int characterIndex)
    {
        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.IsServer)
        {
            // Host - store directly
            ulong clientId = NetworkManager.Singleton.LocalClientId;
            CharacterSelectManager.SetServerCharacterSelection(clientId, characterIndex);
        }
        else if (IsSpawned)
        {
            // Client - send to server via RPC
            SyncSelectionServerRpc(characterIndex);
        }
        else
        {
            Debug.LogWarning("[CharacterSelectionSync] Not spawned yet, cannot sync selection");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SyncSelectionServerRpc(int characterIndex, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        CharacterSelectManager.SetServerCharacterSelection(clientId, characterIndex);
        Debug.Log($"[CharacterSelectionSync] Server received selection: Client {clientId} = Character {characterIndex}");
    }
}
