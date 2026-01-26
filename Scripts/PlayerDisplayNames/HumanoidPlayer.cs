using Unity.Netcode;
using UnityEngine;
using Unity.Collections;

public class HumanoidPlayer : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        // If we are the owner of this player, tell the server our name
        if (IsOwner)
        {
            string myName = PlayerPrefs.GetString(NameSelector.PlayerNameKey, "Unknown");
            Debug.Log($"[HumanoidPlayer] I am owner, sending my name: {myName}");
            SetNameServerRpc(myName);
        }
    }

    [ServerRpc]
    private void SetNameServerRpc(string name, ServerRpcParams rpcParams = default)
    {
        Debug.Log($"[HumanoidPlayer] Server received name '{name}' from client {rpcParams.Receive.SenderClientId}");
        PlayerName.Value = name;
    }
}