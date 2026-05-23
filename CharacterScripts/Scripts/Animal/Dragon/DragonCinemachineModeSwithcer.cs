using UnityEngine;
using Unity.Cinemachine;
using Unity.Netcode;

public class DragonCinemachineModeSwitcher : NetworkBehaviour
{
    [SerializeField] private DragonFlightController flight;
    [SerializeField] private AnimalGroundingSystem grounding;
    [SerializeField] private CinemachineCamera groundCam;
    [SerializeField] private CinemachineCamera flightCam;
    [SerializeField] private CinemachineCamera diveCam;

    [Header("Lens")]
    [Tooltip("Far clipping plane applied to all dragon Cinemachine cameras. Cinemachine copies this value to MainCamera while a dragon camera is live.")]
    [SerializeField] private float farClipPlane = 10000f;

    [Header("Priority")]
    [SerializeField] private int ownerActivePriority = 15;
    [SerializeField] private int ownerInactivePriority = 5;
    [SerializeField] private int nonOwnerPriority = 0;

    private void Awake()
    {
        if (!flight) flight = GetComponentInParent<DragonFlightController>();
        if (!grounding) grounding = GetComponentInParent<AnimalGroundingSystem>();

        // Disable all cameras until ownership is resolved
        SetCamerasEnabled(false);
    }

    public override void OnNetworkSpawn()
    {
        ApplyOwnership(IsOwner);
    }

    public override void OnGainedOwnership() => ApplyOwnership(true);
    public override void OnLostOwnership() => ApplyOwnership(false);

    private void ApplyOwnership(bool owner)
    {
        if (!owner)
        {
            // Non-owner: disable all cameras so they don't steal the client's view
            SetCamerasEnabled(false);
            return;
        }

        // Owner: enable cameras
        ApplyLensSettings();
        SetCamerasEnabled(true);
    }

    private void SetCamerasEnabled(bool enabled)
    {
        if (groundCam) { ApplyLensSettings(groundCam); groundCam.gameObject.SetActive(enabled); groundCam.Priority = enabled ? ownerActivePriority : nonOwnerPriority; }
        if (flightCam) { ApplyLensSettings(flightCam); flightCam.gameObject.SetActive(enabled); flightCam.Priority = enabled ? ownerInactivePriority : nonOwnerPriority; }
        if (diveCam) { ApplyLensSettings(diveCam); diveCam.gameObject.SetActive(enabled); diveCam.Priority = enabled ? ownerInactivePriority : nonOwnerPriority; }
    }

    private void ApplyLensSettings()
    {
        ApplyLensSettings(groundCam);
        ApplyLensSettings(flightCam);
        ApplyLensSettings(diveCam);
    }

    private void ApplyLensSettings(CinemachineCamera cam)
    {
        if (!cam)
            return;

        cam.Lens.FarClipPlane = Mathf.Max(cam.Lens.NearClipPlane + 0.01f, farClipPlane);
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;
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
