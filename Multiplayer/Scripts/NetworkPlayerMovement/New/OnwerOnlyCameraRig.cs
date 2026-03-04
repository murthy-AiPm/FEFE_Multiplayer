using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

[DefaultExecutionOrder(-50)]
public class OwnerOnlyFreeLook : NetworkBehaviour
{
    [SerializeField] private CinemachineCamera vcam;
    [SerializeField] private int ownerPriority = 20;
    [SerializeField] private int nonOwnerPriority = 0;

    [Tooltip("Optional: GameObjects to only enable for the owner (e.g. input controller)")]
    [SerializeField] private GameObject[] ownerOnlyObjects;

    private void Awake()
    {
        if (!vcam) vcam = GetComponentInChildren<CinemachineCamera>(true);
        if (vcam)
        {
            vcam.Priority = nonOwnerPriority;
            vcam.gameObject.SetActive(false);
        }

        foreach (var obj in ownerOnlyObjects)
            if (obj) obj.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        Apply(IsOwner);
    }

    public override void OnGainedOwnership() => Apply(true);
    public override void OnLostOwnership() => Apply(false);

    private void Apply(bool owner)
    {
        if (vcam)
        {
            vcam.gameObject.SetActive(owner);
            vcam.Priority = owner ? ownerPriority : nonOwnerPriority;
        }

        foreach (var obj in ownerOnlyObjects)
            if (obj) obj.SetActive(owner);
    }
}