using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

public class HumanoidPlayer : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>();

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            string myName = PlayerPrefs.GetString(NameSelector.GetUniquePlayerNameKey(), "Player");
            SetNameServerRpc(myName);
        }
    }

    [ServerRpc]
    private void SetNameServerRpc(string name)
    {
        PlayerName.Value = name;
    }
}