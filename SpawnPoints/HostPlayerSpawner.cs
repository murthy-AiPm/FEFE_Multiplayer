using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles moving the host player to the correct spawn point after they spawn.
/// The host player (clientId 0) doesn't go through ApprovalCheck, so we handle it separately.
/// </summary>
public class HostPlayerSpawner : NetworkBehaviour
{
    private static bool hasMovedHostPlayer = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Only run on server/host
        if (!IsServer) return;

        // Only handle the host player (clientId 0) once
        if (OwnerClientId == 0 && !hasMovedHostPlayer)
        {
            Debug.Log("[HostPlayerSpawner] Host player spawned, moving to spawn point");
            MoveToSpawnPoint();
            hasMovedHostPlayer = true;
        }
    }

    private void MoveToSpawnPoint()
    {
        if (PlayerSpawnManager.Instance != null)
        {
            if (PlayerSpawnManager.Instance.TryGetSpawnPoint(OwnerClientId, out Vector3 position, out Quaternion rotation))
            {
                // Move the host player to the spawn point
                transform.position = position;
                transform.rotation = rotation;
                
                Debug.Log($"[HostPlayerSpawner] ✓ Moved host player to spawn point at {position}");
                
                // If there's a CharacterController, we need to disable/enable it to force the position update
                var cc = GetComponent<CharacterController>();
                if (cc != null)
                {
                    cc.enabled = false;
                    transform.position = position;
                    transform.rotation = rotation;
                    cc.enabled = true;
                }
            }
            else
            {
                Debug.LogWarning("[HostPlayerSpawner] No spawn point available for host player");
            }
        }
        else
        {
            Debug.LogError("[HostPlayerSpawner] PlayerSpawnManager not found!");
        }
    }

    private void OnDestroy()
    {
        // Reset the flag when the player is destroyed
        if (IsServer && OwnerClientId == 0)
        {
            hasMovedHostPlayer = false;
        }
    }
}
