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

    /// <summary>
    /// Try to spawn a player - only succeeds if they have a valid character selection
    /// </summary>
    public void TrySpawnPlayerCharacter(ulong clientId)
    {
        if (!IsServer) return;

        // Check if already spawned
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null) return;
        }

        // Get character index from persisted server data
        int characterIndex = CharacterSelectManager.GetPersistedCharacterIndex(clientId);

        // -1 or invalid means they haven't selected yet
        if (characterIndex < 0)
        {
            Debug.Log($"[CharacterSpawnHandler] Client {clientId} hasn't selected a character yet - waiting for selection");
            // Don't spawn - they'll use LateJoinCharacterSelectUI in the Game scene
            return;
        }

        SpawnPlayerCharacter(clientId, characterIndex);
    }

    private void SpawnPlayerCharacter(ulong clientId, int characterIndex)
    {
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

        Debug.Log($"[CharacterSpawnHandler] Late join request: Client {clientId} wants character {characterIndex}");

        // Validate character index
        if (characterIndex < 0 || characterIndex >= characterDatabase.CharacterCount)
        {
            Debug.LogError($"[CharacterSpawnHandler] Invalid character index {characterIndex}");
            return;
        }

        // Check if already spawned
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                Debug.Log($"[CharacterSpawnHandler] Client {clientId} already has a player object");
                return;
            }
        }

        // Store the selection
        CharacterSelectManager.SetServerCharacterSelection(clientId, characterIndex);

        // Spawn the player
        SpawnPlayerCharacter(clientId, characterIndex);
    }

    [ClientRpc]
    private void NotifySpawnSuccessClientRpc(ClientRpcParams rpcParams = default)
    {
        Debug.Log("[CharacterSpawnHandler] Spawn successful!");

        // Find and notify the late join UI
        var lateJoinUI = FindObjectOfType<LateJoinCharacterSelectUI>();
        if (lateJoinUI != null)
        {
            lateJoinUI.OnSpawnSuccess();
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