using UnityEngine;
using Unity.Cinemachine;

public class DragonCinemachineModeSwitcher : MonoBehaviour
{
    [SerializeField] private DragonFlightController flight;
    [SerializeField] private CinemachineCamera flightCam;
    [SerializeField] private CinemachineCamera diveCam;

    private void Awake()
    {
        if (!flight) flight = GetComponentInParent<DragonFlightController>();
    }

    private void LateUpdate()
    {
        if (!flight || !flightCam || !diveCam) return;

        bool diving = flight.IsDiving && !flight.IsGrounded;

        // Dive wins only while diving
        flightCam.Priority = diving ? 10 : 20;
        diveCam.Priority = diving ? 20 : 10;

        Debug.Log($"diving={diving} flightPrio={flightCam.Priority} divePrio={diveCam.Priority}");
    }
}
