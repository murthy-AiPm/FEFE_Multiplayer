using Unity.Netcode;
using UnityEngine;

public class PlayerCameraBinder : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;   // Only the local player controls the camera

        var cam = Camera.main;
        var follow = cam.GetComponent<SimpleCameraFollow>();

        follow.SetTarget(transform);   // This client's camera follows THIS player
    }
}
