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
        bool isHover = flight.IsHoverMode;
        
        // Priority: Dive > Flight > Ground
        if (isDiving)
        {
            diveCam.Priority = 15;
            flightCam.Priority = 10;
            groundCam.Priority = 5;
        }
        else if (isHover)
        {
            diveCam.Priority = 5;
            flightCam.Priority = 15;
            groundCam.Priority = 10;
        }
        else if (isFlying)
        {
            diveCam.Priority = 5;
            flightCam.Priority = 15;
            groundCam.Priority = 10;
        }
        else if (isGrounded)
        {
            diveCam.Priority = 5;
            flightCam.Priority = 10;
            groundCam.Priority = 15;
        }
    }
}