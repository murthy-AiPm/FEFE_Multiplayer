using UnityEngine;
using Unity.Cinemachine;

public class DragonCinemachineModeSwitcher : MonoBehaviour
{
    [SerializeField] private DragonFlightController flight;
    [SerializeField] private DragonGroundingSystem grounding;
    [SerializeField] private CinemachineCamera groundCam;
    [SerializeField] private CinemachineCamera flightCam;
    [SerializeField] private CinemachineCamera diveCam;

    private void Awake()
    {
        if (!flight) flight = GetComponentInParent<DragonFlightController>();
        if (!grounding) grounding = GetComponentInParent<DragonGroundingSystem>();
    }

    private void LateUpdate()
    {
        if (!flight || !grounding || !groundCam || !flightCam || !diveCam) return;

        bool isGrounded = grounding.IsGrounded;
        bool isDiving = flight.IsDiving && !isGrounded;
        bool isFlying = flight.IsFlying;
        Debug.Log($"Ground:{groundCam.Priority} Flight:{flightCam.Priority} Dive:{diveCam.Priority} | Grounded:{isGrounded} Diving:{isDiving}");
        // Priority: Dive > Flight > Ground
        if (isDiving)
        {
            diveCam.Priority = 30;
            flightCam.Priority = 20;
            groundCam.Priority = 10;
        }
        else if (!isGrounded && !isFlying)
        {
            diveCam.Priority = 10;
            flightCam.Priority = 30;
            groundCam.Priority = 20;
        }
        else
        {
            diveCam.Priority = 10;
            flightCam.Priority = 20;
            groundCam.Priority = 30;
        }
    }
}