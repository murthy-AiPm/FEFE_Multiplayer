using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles spawning the correct character prefab based on player's selection.
/// Attach this to a GameObject in the Game scene.
/// </summary>
public class CharacterSpawnHandler : NetworkBehaviour
{
    [SerializeField] private CharacterDatabase characterDatabase;
    [SerializeField] private Transform[] spawnPoints;

    private int spawnIndex = 0;

    public static CharacterSpawnHandler Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        // Spawn all existing connected clients who have selected a character
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            TrySpawnPlayerCharacter(clientId);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this)
            Instance = null;

        if (!IsServer) return;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        TrySpawnPlayerCharacter(clientId);
    }

    #region Taken Characters

    /// <summary>
    /// Get list of character indices that are already taken
    /// </summary>
    private List<int> GetTakenCharacters()
    {
        var taken = new List<int>();

        // Check all connected clients for their character selections
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            if (kvp.Value.PlayerObject != null)
            {
                // Player is spawned - get their character index
                int charIndex = CharacterSelectManager.GetPersistedCharacterIndex(kvp.Key);
                if (charIndex >= 0 && !taken.Contains(charIndex))
                {
                    taken.Add(charIndex);
                }
            }
        }

        return taken;
    }

    /// <summary>
    /// Check if a character is already taken, optionally excluding a specific client (e.g. the one requesting a change).
    /// Respects CharacterSelectManager.enforceUniqueCharacters — if false, always returns false.
    /// </summary>
    private bool IsCharacterTaken(int characterIndex, ulong excludeClientId = ulong.MaxValue)
    {
        if (CharacterSelectManager.Instance != null && !CharacterSelectManager.Instance.EnforceUniqueCharacters)
            return false;

        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            if (kvp.Key == excludeClientId) continue;
            if (kvp.Value.PlayerObject != null)
            {
                int charIndex = CharacterSelectManager.GetPersistedCharacterIndex(kvp.Key);
                if (charIndex == characterIndex)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Called by LateJoinCharacterSelectUI to get taken characters
    /// </summary>
    public void RequestTakenCharacters()
    {
        RequestTakenCharactersServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestTakenCharactersServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        var taken = GetTakenCharacters();

        Debug.Log($"[CharacterSpawnHandler] Sending taken characters to client {clientId}: {string.Join(", ", taken)}");

        SendTakenCharactersClientRpc(taken.ToArray(), new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        });
    }

    [ClientRpc]
    private void SendTakenCharactersClientRpc(int[] takenCharacters, ClientRpcParams rpcParams = default)
    {
        var lateJoinUI = FindObjectOfType<LateJoinCharacterSelectUI>();
        if (lateJoinUI != null)
        {
            lateJoinUI.SetTakenCharacters(takenCharacters);
        }
    }

    #endregion

    /// <summary>
    /// Try to spawn a player - only succeeds if they have a valid character selection
    /// </summary>
    public void TrySpawnPlayerCharacter(ulong clientId)
    {
        if (!IsServer) return;

        // Despawn existing player object if present (character change)
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                client.PlayerObject.Despawn(true);
            }
        }

        // Get character index from persisted server data
        int characterIndex = CharacterSelectManager.GetPersistedCharacterIndex(clientId);

        // -1 or invalid means they haven't selected yet
        if (characterIndex < 0)
        {
            // Don't spawn - they'll use LateJoinCharacterSelectUI in the Game scene
            return;
        }

        SpawnPlayerCharacter(clientId, characterIndex);
    }

    private void SpawnPlayerCharacter(ulong clientId, int characterIndex)
    {
        if (characterIndex < 0 || characterIndex >= characterDatabase.CharacterCount)
            characterIndex = 0;

        var characterData = characterDatabase.GetCharacter(characterIndex);
        if (characterData == null || characterData.prefab == null)
        {
            Debug.LogError($"[CharacterSpawnHandler] No prefab for character {characterIndex}");
            return;
        }

        Vector3 spawnPos = GetNextSpawnPosition();

        var playerObject = Instantiate(characterData.prefab, spawnPos, Quaternion.identity);
        var networkObject = playerObject.GetComponent<NetworkObject>();

        if (networkObject != null)
        {
            networkObject.SpawnAsPlayerObject(clientId);

            // Notify the client that spawn was successful
            NotifySpawnSuccessClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            });
        }
        else
        {
            Debug.LogError("[CharacterSpawnHandler] Prefab missing NetworkObject!");
            Destroy(playerObject);
        }
    }

    /// <summary>
    /// Called by LateJoinCharacterSelectUI when a client wants to spawn
    /// </summary>
    public void RequestLateJoinSpawn(int characterIndex)
    {
        RequestLateJoinSpawnServerRpc(characterIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestLateJoinSpawnServerRpc(int characterIndex, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // Validate character index
        if (characterIndex < 0 || characterIndex >= characterDatabase.CharacterCount)
        {
            Debug.LogError($"[CharacterSpawnHandler] Invalid character index {characterIndex}");
            NotifySpawnFailedClientRpc("Invalid character selection", new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
            return;
        }

        // Check if character is taken (exclude the requesting client so they can re-select their own character)
        if (IsCharacterTaken(characterIndex, clientId))
        {
            Debug.Log($"[CharacterSpawnHandler] Character {characterIndex} is already taken!");
            NotifySpawnFailedClientRpc("That character is already taken! Select another.", new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
            return;
        }

        // Despawn existing player object if present (character change)
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null)
                client.PlayerObject.Despawn(true);
        }

        // Store the selection
        CharacterSelectManager.SetServerCharacterSelection(clientId, characterIndex);

        // Spawn the player
        SpawnPlayerCharacter(clientId, characterIndex);
    }

    [ClientRpc]
    private void NotifySpawnSuccessClientRpc(ClientRpcParams rpcParams = default)
    {
        // Find and notify the late join UI
        var lateJoinUI = FindObjectOfType<LateJoinCharacterSelectUI>();
        if (lateJoinUI != null)
        {
            lateJoinUI.OnSpawnSuccess();
        }
    }

    [ClientRpc]
    private void NotifySpawnFailedClientRpc(string reason, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"[CharacterSpawnHandler] Spawn failed: {reason}");

        var lateJoinUI = FindObjectOfType<LateJoinCharacterSelectUI>();
        if (lateJoinUI != null)
        {
            lateJoinUI.OnSpawnFailed(reason);
        }
    }

    private Vector3 GetNextSpawnPosition()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return SpawnPoint.GetRandomSpawnPos();

        Vector3 pos = spawnPoints[spawnIndex % spawnPoints.Length].position;
        spawnIndex++;
        return pos;
    }
}