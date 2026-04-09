using UnityEngine;
using Unity.Netcode;

/// <summary>//
/// Attach to Horse alongside DragonGroundController.
/// Enables/disables the ground controller based on whether someone is mounted.
/// When unmounted: Horse is idle (or AI-controlled if you add AI later)
/// When mounted: Rider's input controls the horse
/// </summary>
public class MountInputController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private MountableEntity mountableEntity;
    [SerializeField] private AnimalGroundController groundController;
    [SerializeField] private AnimalAnimatorController animatorController;

    [Header("Settings")]
    [Tooltip("Should horse have AI movement when not mounted? (Not implemented yet)")]
    [SerializeField] private bool useAIWhenUnmounted = false;

    private void Awake()
    {
        if (mountableEntity == null)
            mountableEntity = GetComponent<MountableEntity>();
        if (groundController == null)
            groundController = GetComponent<AnimalGroundController>();
        if (animatorController == null)
            animatorController = GetComponentInChildren<AnimalAnimatorController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Initially disable ground controller (horse is idle until mounted)
        if (groundController != null)
            groundController.enabled = false;

        // Subscribe to mount state changes
        if (mountableEntity != null)
        {
            // Access the NetworkVariable and subscribe to changes
            // This ensures immediate response when riderId syncs
            UpdateControllerState();
        }
    }

    private void Update()
    {
        // Continuously check mount state and update controller
        // This catches any changes that might have been missed
        UpdateControllerState();
    }

    private void UpdateControllerState()
    {
        if (mountableEntity == null || groundController == null) return;

        bool isMounted = mountableEntity.IsMounted;

        // In offline mode (no NetworkManager), always allow control when mounted
        // In online mode, only allow control if we own the horse
        bool isOnline = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        bool shouldBeActive = isMounted && (isOnline ? IsOwner : true);

        //Debug.Log($"[MountInputController] IsMounted: {isMounted}, IsOwner: {IsOwner}, IsOnline: {isOnline}, RiderId: {mountableEntity.RiderId}, shouldBeActive: {shouldBeActive}");

        // Enable controller only if:
        // 1. Someone is mounted, AND
        // 2. We own this horse (in online mode) OR we're in offline mode
        if (groundController.enabled != shouldBeActive)
        {
            groundController.enabled = shouldBeActive;

            if (shouldBeActive)
                Debug.Log($"[MountInputController] Ground controller ENABLED - Rider {mountableEntity.RiderId} now controls horse");
            else
                Debug.Log("[MountInputController] Ground controller DISABLED - Horse is idle");
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
    }
}