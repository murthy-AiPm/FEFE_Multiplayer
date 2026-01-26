using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles spawning the correct character prefab based on player's selection.
/// Attach this to a GameObject in the Game scene (not CharacterSelect).
/// </summary>
public class CharacterSpawnHandler : NetworkBehaviour
{
    [SerializeField] private CharacterDatabase characterDatabase;
    [SerializeField] private Transform[] spawnPoints;

    private int spawnIndex = 0;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // Subscribe to when players need spawning
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        // Spawn all existing connected clients (in case we just loaded the scene)
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SpawnPlayerCharacter(clientId);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;
        
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        SpawnPlayerCharacter(clientId);
    }

    private void SpawnPlayerCharacter(ulong clientId)
    {
        // Check if player already has a character spawned
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null) return; // Already has a player object
        }

        // Get character index from CharacterSelectManager
        int characterIndex = 0;
        if (CharacterSelectManager.Instance != null)
        {
            characterIndex = CharacterSelectManager.Instance.GetPlayerCharacterIndex(clientId);
        }

        // Clamp to valid range
        if (characterIndex < 0 || characterIndex >= characterDatabase.CharacterCount)
            characterIndex = 0;

        var characterData = characterDatabase.GetCharacter(characterIndex);
        if (characterData == null || characterData.prefab == null)
        {
            Debug.LogError($"[CharacterSpawnHandler] No prefab found for character index {characterIndex}");
            return;
        }

        // Get spawn position
        Vector3 spawnPos = GetNextSpawnPosition();

        // Spawn the character
        var playerObject = Instantiate(characterData.prefab, spawnPos, Quaternion.identity);
        var networkObject = playerObject.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            networkObject.SpawnAsPlayerObject(clientId);
            Debug.Log($"[CharacterSpawnHandler] Spawned character {characterIndex} for client {clientId}");
        }
        else
        {
            Debug.LogError($"[CharacterSpawnHandler] Prefab missing NetworkObject component!");
            Destroy(playerObject);
        }
    }

    private Vector3 GetNextSpawnPosition()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            // Fallback to SpawnPoint system if no points assigned
            return SpawnPoint.GetRandomSpawnPos();
        }

        Vector3 pos = spawnPoints[spawnIndex % spawnPoints.Length].position;
        spawnIndex++;
        return pos;
    }
}
