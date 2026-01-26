using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CharacterSelectManager : NetworkBehaviour
{
    [Serializable]
    public struct SelectionEntry : INetworkSerializable, IEquatable<SelectionEntry>
    {
        public ulong clientId;
        public int characterId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref characterId);
        }

        public bool Equals(SelectionEntry other) => clientId == other.clientId && characterId == other.characterId;
    }

    public NetworkList<SelectionEntry> Selections;

    private void Awake()
    {
        Selections = new NetworkList<SelectionEntry>();
    }

    public override void OnNetworkSpawn()
    {
        DontDestroyOnLoad(gameObject);
        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // free their character
        for (int i = Selections.Count - 1; i >= 0; i--)
        {
            if (Selections[i].clientId == clientId)
                Selections.RemoveAt(i);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestSelectServerRpc(int characterId, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // already taken by someone else?
        for (int i = 0; i < Selections.Count; i++)
        {
            if (Selections[i].characterId == characterId && Selections[i].clientId != clientId)
                return; // deny silently (UI will reflect taken list)
        }

        // replace existing selection for this client
        for (int i = Selections.Count - 1; i >= 0; i--)
        {
            if (Selections[i].clientId == clientId)
                Selections.RemoveAt(i);
        }

        Selections.Add(new SelectionEntry { clientId = clientId, characterId = characterId });
    }

    public bool TryGetSelection(ulong clientId, out int characterId)
    {
        for (int i = 0; i < Selections.Count; i++)
        {
            if (Selections[i].clientId == clientId)
            {
                characterId = Selections[i].characterId;
                return true;
            }
        }

        characterId = -1;
        return false;
    }

    public bool IsTaken(int characterId)
    {
        for (int i = 0; i < Selections.Count; i++)
            if (Selections[i].characterId == characterId) return true;
        return false;
    }


}
