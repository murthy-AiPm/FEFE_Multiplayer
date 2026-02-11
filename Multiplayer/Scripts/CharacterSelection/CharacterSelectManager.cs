using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Networked character selection manager.
/// - Syncs selections across all clients
/// - Optionally prevents duplicate character selection
/// - Tracks ready state
/// </summary>
public class CharacterSelectManager : NetworkBehaviour
{
    public static CharacterSelectManager Instance { get; private set; }

    [SerializeField] private CharacterDatabase characterDatabase;

    [Header("Rules")]
    [Tooltip("If true, no two players can choose the same character.")]
    [SerializeField] private bool enforceUniqueCharacters = true;

    public bool EnforceUniqueCharacters => enforceUniqueCharacters;

    // Network synced list of all player selections
    private NetworkList<CharacterSelection> selections;

    // Static storage for spawning (persists between scenes on server)
    private static Dictionary<ulong, int> serverSelections = new Dictionary<ulong, int>();

    public event Action OnSelectionsChanged;

    public CharacterDatabase Database => characterDatabase;

    private void Awake()
    {
        selections = new NetworkList<CharacterSelection>();
    }

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        selections.OnListChanged += HandleSelectionsChanged;

        if (IsServer)
        {
            // Clear previous selections when scene loads
            serverSelections.Clear();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Register existing connected clients
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                AddPlayer(clientId);
            }
        }

        OnSelectionsChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        selections.OnListChanged -= HandleSelectionsChanged;

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (Instance == this)
            Instance = null;
    }

    private void HandleSelectionsChanged(NetworkListEvent<CharacterSelection> changeEvent)
    {
        OnSelectionsChanged?.Invoke();
    }

    private void OnClientConnected(ulong clientId)
    {
        AddPlayer(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        RemovePlayer(clientId);
    }

    private void AddPlayer(ulong clientId)
    {
        // Check if already exists
        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].ClientId == clientId) return;
        }

        string playerName = GetPlayerName(clientId);

        selections.Add(new CharacterSelection
        {
            ClientId = clientId,
            CharacterIndex = -1, // Not selected
            IsReady = false,
            PlayerName = playerName
        });

        Debug.Log($"[CharacterSelectManager] Added player: {playerName} (ID: {clientId})");
    }

    private void RemovePlayer(ulong clientId)
    {
        for (int i = selections.Count - 1; i >= 0; i--)
        {
            if (selections[i].ClientId == clientId)
            {
                selections.RemoveAt(i);
                serverSelections.Remove(clientId);
                Debug.Log($"[CharacterSelectManager] Removed player {clientId}");
                break;
            }
        }
    }

    private string GetPlayerName(ulong clientId)
    {
        if (HostSingleton.Instance?.GameManager?.networkServer != null)
        {
            var userData = HostSingleton.Instance.GameManager.networkServer.GetUserDataByClientID(clientId);
            if (userData != null)
                return userData.userName;
        }
        return $"Player {clientId}";
    }

    #region Selection Logic

    /// <summary>
    /// Check if a character is already taken by another player
    /// </summary>
    public bool IsCharacterTaken(int characterIndex, ulong excludeClientId = ulong.MaxValue)
    {
        // If duplicates are allowed, nothing is ever "taken"
        if (!enforceUniqueCharacters) return false;

        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].CharacterIndex == characterIndex && selections[i].ClientId != excludeClientId)
            {
                return true;
            }
        }
        return false;
    }

    public void TrySelectCharacter(int characterIndex)
    {
        if (NetworkManager.Singleton == null) return;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;

        // Check locally first to give instant feedback
        if (enforceUniqueCharacters && IsCharacterTaken(characterIndex, localClientId))
        {
            Debug.Log($"[CharacterSelectManager] Character {characterIndex} is already taken!");
            return;
        }

        // Send to server
        SelectCharacterServerRpc(characterIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SelectCharacterServerRpc(int characterIndex, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // Validate character isn't taken
        if (enforceUniqueCharacters && IsCharacterTaken(characterIndex, clientId))
        {
            Debug.Log($"[CharacterSelectManager] Server rejected: Character {characterIndex} already taken");
            RejectSelectionClientRpc(characterIndex, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            });
            return;
        }

        // Update selection
        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].ClientId == clientId)
            {
                var sel = selections[i];
                sel.CharacterIndex = characterIndex;
                selections[i] = sel;

                // Store for spawning
                serverSelections[clientId] = characterIndex;

                Debug.Log($"[CharacterSelectManager] Server accepted: Client {clientId} selected character {characterIndex}");
                break;
            }
        }
    }

    [ClientRpc]
    private void RejectSelectionClientRpc(int characterIndex, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"[CharacterSelectManager] Selection rejected: Character {characterIndex} is taken");
        OnSelectionsChanged?.Invoke(); // Refresh UI
    }

    #endregion

    #region Ready Logic

    public void SetReady(bool ready)
    {
        SetReadyServerRpc(ready);
    }

    /// <summary>
    /// Called when a client is ready to spawn into the game
    /// </summary>
    public void NotifyReadyToSpawn()
    {
        NotifyReadyToSpawnServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetReadyServerRpc(bool ready, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].ClientId == clientId)
            {
                var sel = selections[i];

                // Can only be ready if character is selected
                if (ready && sel.CharacterIndex < 0)
                {
                    Debug.Log($"[CharacterSelectManager] Client {clientId} cannot ready without selecting character");
                    return;
                }

                sel.IsReady = ready;
                selections[i] = sel;
                Debug.Log($"[CharacterSelectManager] Client {clientId} ready: {ready}");
                break;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyReadyToSpawnServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // Check if they have a valid selection
        if (!serverSelections.TryGetValue(clientId, out int charIndex) || charIndex < 0)
        {
            Debug.Log($"[CharacterSelectManager] Client {clientId} tried to spawn without valid selection");
            return;
        }

        Debug.Log($"[CharacterSelectManager] Client {clientId} ready to spawn with character {charIndex}");

        // Spawn logic happens wherever your project currently does it.
        // You already call CharacterSpawnHandler.Instance.TrySpawnPlayerCharacter(clientId) in your current file.
        if (CharacterSpawnHandler.Instance != null)
        {
            CharacterSpawnHandler.Instance.TrySpawnPlayerCharacter(clientId);
        }
    }

    #endregion

    #region Getters

    public CharacterSelection? GetLocalSelection()
    {
        if (NetworkManager.Singleton == null) return null;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        for (int i = 0; i < selections.Count; i++)
        {
            if (selections[i].ClientId == localId)
                return selections[i];
        }
        return null;
    }

    public List<CharacterSelection> GetAllSelections()
    {
        var list = new List<CharacterSelection>();
        for (int i = 0; i < selections.Count; i++)
        {
            list.Add(selections[i]);
        }
        return list;
    }

    public static int GetPersistedCharacterIndex(ulong clientId)
    {
        if (serverSelections.TryGetValue(clientId, out int index))
        {
            Debug.Log($"[CharacterSelectManager] GetPersistedCharacterIndex: Client {clientId} = {index}");
            return index;
        }
        Debug.Log($"[CharacterSelectManager] GetPersistedCharacterIndex: Client {clientId} not found, returning -1");
        return -1;
    }

    public static void SetServerCharacterSelection(ulong clientId, int characterIndex)
    {
        serverSelections[clientId] = characterIndex;
        Debug.Log($"[CharacterSelectManager] SetServerCharacterSelection: Client {clientId} = {characterIndex}");
    }

    #endregion
}

public struct CharacterSelection : INetworkSerializable, IEquatable<CharacterSelection>
{
    public ulong ClientId;
    public int CharacterIndex;
    public bool IsReady;
    public FixedString32Bytes PlayerName;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref CharacterIndex);
        serializer.SerializeValue(ref IsReady);
        serializer.SerializeValue(ref PlayerName);
    }

    public bool Equals(CharacterSelection other)
    {
        return ClientId == other.ClientId &&
               CharacterIndex == other.CharacterIndex &&
               IsReady == other.IsReady &&
               PlayerName.Equals(other.PlayerName);
    }
}
