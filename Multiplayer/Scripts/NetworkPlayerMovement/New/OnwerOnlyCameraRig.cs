using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

[DefaultExecutionOrder(-50)]
public class OwnerOnlyFreeLook : NetworkBehaviour
{
    [SerializeField] private CinemachineFreeLook freeLook;
    [SerializeField] private int ownerPriority = 20;
    [SerializeField] private int nonOwnerPriority = 0;

    private void Awake()
    {
        if (!freeLook) freeLook = GetComponentInChildren<CinemachineFreeLook>(true);

        // Disable before Cinemachine chooses it.
        if (freeLook)
        {
            freeLook.Priority = nonOwnerPriority;
            freeLook.gameObject.SetActive(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        Apply(IsOwner);
    }

    public override void OnGainedOwnership() => Apply(true);
    public override void OnLostOwnership() => Apply(false);

    private void Apply(bool owner)
    {
        if (!freeLook) return;

        if (owner)
        {
            freeLook.gameObject.SetActive(true);
            freeLook.Priority = ownerPriority;
        }
        else
        {
            freeLook.Priority = nonOwnerPriority;
            freeLook.gameObject.SetActive(false);
        }
    }
}
