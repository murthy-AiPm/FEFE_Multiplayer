using Unity.Netcode;
using UnityEngine;
using Cinemachine;

public class LocalCinemachineBinder : NetworkBehaviour
{
    [SerializeField] private Transform followTarget;  // assign CameraFollowTarget in Inspector

    public override void OnNetworkSpawn()
    {
        // Only run for *this client's* own player
        if (!IsOwner) return;

        // Get the local (per-client) VCam in the scene
        CinemachineVirtualCamera vcam = FindObjectOfType<CinemachineVirtualCamera>();
        if (vcam == null)
        {
            Debug.LogError("No CinemachineVirtualCamera found in the scene!");
            return;
        }

        vcam.Follow = followTarget;
        vcam.LookAt = followTarget;

        // Optional but nice:
        vcam.Priority = 20;

        Debug.Log($"[LocalCinemachineBinder] Bound VCam '{vcam.name}' to player '{gameObject.name}' on client.");
    }
}
