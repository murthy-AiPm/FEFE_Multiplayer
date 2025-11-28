using UnityEngine;

public class LocalCameraFollow : MonoBehaviour
{
    [SerializeField] private Cinemachine.CinemachineVirtualCamera vcam;

    private void Start()
    {
        // Find local player
        foreach (var player in FindObjectsOfType<ClientPlayerMove>())
        {
            if (player.IsOwner)
            {
                vcam.Follow = player.transform;   // or the camera target in ThirdPersonController
                vcam.LookAt = player.transform;   // or separate target
                break;
            }
        }
    }
}
