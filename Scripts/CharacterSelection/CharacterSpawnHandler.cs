using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles spawning the correct character prefab based on player's selection.
/// Attach this to a GameObject in the Game scene.
/// </summary>
public class CharacterSpawnHandler : NetworkBehaviour
{
    [SerializeField] private CharacterDatabase characterDatabase;
    [SerializeField] private Transform[] spawnPoints;

    private int spawnIndex = 0;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        // Spawn all existing connected clients
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
        // Check if already spawned
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null) return;
        }

        // Get character index from persisted server data
        int characterIndex = CharacterSelectManager.GetPersistedCharacterIndex(clientId);

        Debug.Log($"[CharacterSpawnHandler] Spawning character {characterIndex} for client {clientId}");

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
            Debug.Log($"[CharacterSpawnHandler] Spawned {characterData.characterName} for client {clientId}");
        }
        else
        {
            Debug.LogError("[CharacterSpawnHandler] Prefab missing NetworkObject!");
            Destroy(playerObject);
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