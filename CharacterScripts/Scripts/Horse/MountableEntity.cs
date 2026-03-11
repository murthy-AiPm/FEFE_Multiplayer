using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Attach to any mount (horse, pegasus, etc.) to make it mountable.
/// Handles mount/dismount requests, rider parenting, and network sync.
/// </summary>
public class MountableEntity : NetworkBehaviour
{
    [Header("Mount Points")]
    [SerializeField] private Transform saddlePoint;
    [Tooltip("Positions around mount where rider can dismount")]
    [SerializeField] private Transform[] dismountPoints = new Transform[4]; // Front, Back, Left, Right

    [Header("Interaction")]
    [SerializeField] private float interactionRadius = 2.5f;
    [SerializeField] private LayerMask riderLayer = ~0;

    [Header("References")]
    [SerializeField] private DragonGroundController groundController;

    [Header("Motion Detection (for remote animation)")]
    [Tooltip("If groundController isn't available or its walk/run flags aren't networked, we estimate motion from transform delta.")]
    [SerializeField] private float movingSpeedThreshold = 0.15f;
    [Tooltip("Smoothing for estimated speed (0 = no smoothing, 1 = very slow).")]
    [Range(0f, 0.95f)]
    [SerializeField] private float speedSmoothing = 0.6f;


    //ridigbody

    private Rigidbody _rb;
    // Network state
    private NetworkVariable<ulong> riderId = new NetworkVariable<ulong>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Local state for offline mode
    private bool localIsMounted = false;
    private ulong localRiderId = 0;

    // Current rider reference (local cache)
    private GameObject currentRider;

    // IMPORTANT: We store riderId as (actualClientId + 1) to avoid collision
    // riderId = 0 means unmounted
    // riderId = 1 means mounted by client 0 (host)
    // riderId = 2 means mounted by client 1, etc.
    public bool IsMounted => IsSpawned ? (riderId.Value != 0) : localIsMounted;
    public Transform SaddlePoint => saddlePoint;
    public ulong RiderId => IsSpawned ? (riderId.Value > 0 ? riderId.Value - 1 : 0) : localRiderId;

    // --- Estimated motion (works for remotes too, using NetworkTransform-updated positions) ---
    private Vector3 _lastPos;
    private float _estimatedSpeed;

    // For animation system to check if mount is moving.
    // Prefer the controller flags when available, but fall back to estimated speed so other clients
    // can still drive rider "ride move" animations.
    public bool IsMoving
    {
        get
        {
            // If we own/drive the mount locally, the controller flags are the source of truth.
            // For non-owners, those flags often won't update (no input), so use estimated motion.
            if (groundController != null)
            {
                bool controllerSaysMoving = (groundController.IsWalking || groundController.IsRunning);

                if (!IsSpawned || IsOwner)
                    return controllerSaysMoving;

                return controllerSaysMoving || (_estimatedSpeed > movingSpeedThreshold);
            }

            return (_estimatedSpeed > movingSpeedThreshold);
        }
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        SetMounted(false); // start frozen
        if (groundController == null)
            groundController = GetComponent<DragonGroundController>();

        _lastPos = transform.position;
    }

    private void LateUpdate()
    {
        // Keep a cheap motion estimate so remotes can infer mounted locomotion even if the mount
        // controller state isn't networked.
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 pos = transform.position;
        float instantSpeed = (pos - _lastPos).magnitude / dt;
        _lastPos = pos;

        // Exponential smoothing
        _estimatedSpeed = Mathf.Lerp(instantSpeed, _estimatedSpeed, speedSmoothing);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        riderId.OnValueChanged += OnRiderChanged;

        // Check initial state in case we spawned with someone already mounted
        if (riderId.Value != 0)
        {
            OnRiderChanged(0, riderId.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        riderId.OnValueChanged -= OnRiderChanged;
        base.OnNetworkDespawn();
    }

    private void OnRiderChanged(ulong oldValue, ulong newValue)
    {
        Debug.Log($"[MountableEntity] RiderId changed from {oldValue} to {newValue}. IsMounted: {IsMounted}");

        // If someone mounted/dismounted, update local cache
        if (newValue == 0)
        {
            currentRider = null;
        }
        else
        {
            // Remember: riderId is stored as (actualClientId + 1)
            ulong actualClientId = newValue - 1;

            // Find the rider when they mount
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(actualClientId, out var client))
            {
                currentRider = client.PlayerObject?.gameObject;
                Debug.Log($"[MountableEntity] Found rider for client {actualClientId}: {currentRider?.name}");
            }
        }
    }

    /// <summary>
    /// Called by MountController when player wants to mount.
    /// Server validates and executes the mount.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestMountServerRpc(ulong requestingClientId, ServerRpcParams rpcParams = default)
    {
        Debug.Log($"[MountableEntity] RequestMountServerRpc called - requestingClientId: {requestingClientId}, IsMounted: {IsMounted}, riderId.Value: {riderId.Value}");

        // Validate: is mount available?
        if (IsMounted)
        {
            //Debug.LogWarning($"[MountableEntity] Mount request denied - already mounted by {riderId.Value}");
            return;
        }

        // Find the rider's player object
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(requestingClientId, out var client))
        {
            //Debug.Log($"[MountableEntity] Available clients: {string.Join(", ", NetworkManager.Singleton.ConnectedClients.Keys)}");
            return;
        }

        if (client.PlayerObject == null)
        {
            //Debug.LogError($"[MountableEntity] Client {requestingClientId} has no PlayerObject");
            return;
        }

        GameObject riderObject = client.PlayerObject.gameObject;
        NetworkObject riderNetObj = client.PlayerObject;
        MountController mountController = riderObject.GetComponent<MountController>();

        if (mountController == null)
        {
            Debug.LogError($"[MountableEntity] Rider {requestingClientId} has no MountController");
            return;
        }

        // Execute mount
        // IMPORTANT: Store (clientId + 1) to avoid riderId=0 collision
        // (0 means unmounted, so host clientId 0 would not trigger change)
        Debug.Log($"[MountableEntity] Setting riderId.Value from {riderId.Value} to {requestingClientId + 1}");
        riderId.Value = requestingClientId + 1;
        SetMounted(true);
        // Debug.Log($"[MountableEntity] riderId.Value is now: {riderId.Value}, IsMounted: {IsMounted}");

        currentRider = riderObject;
        SetMounted(true);

        // Transfer ownership of mount to rider
        // Debug.Log($"[MountableEntity] Transferring ownership to client {requestingClientId}");
        NetworkObject.ChangeOwnership(requestingClientId);
        // Sync current yaw to new owner so they don't snap to stale rotation
        float currentYaw = transform.eulerAngles.y;
        SyncYawClientRpc(currentYaw);

        // Debug.Log($"[MountableEntity] Ownership transferred. NetworkObject.OwnerClientId: {NetworkObject.OwnerClientId}");

        // IMPORTANT: Parenting must be network-driven (server sets it) so EVERY client sees the rider attached.
        // Do NOT rely on local Transform.SetParent on the owner only.
        if (riderNetObj != null)
        {
            // Parent the rider under this mount's NetworkObject (replicates to all clients).
            // worldPositionStays=true so we can snap to saddle in world space next.
            bool parented = riderNetObj.TrySetParent(NetworkObject, true);
            Debug.Log($"[MountableEntity] Rider TrySetParent result: {parented}");

            // Snap rider to saddle for everyone.
            if (saddlePoint != null)
            {
                riderNetObj.transform.SetPositionAndRotation(saddlePoint.position, saddlePoint.rotation);
            }
        }

        // Notify rider to complete mount on their end
        mountController.CompleteMountClientRpc(NetworkObjectId);

        //     Debug.Log($"[MountableEntity] Client {requestingClientId} mounted successfully");
    }

    /// <summary>
    /// Offline mode: Mount without networking (for single-player testing)
    /// </summary>
    public void MountLocal(GameObject riderObject, ulong fakeClientId = 0)
    {
        if (localIsMounted)
        {
            //  Debug.LogWarning("[MountableEntity] Already mounted locally");
            return;
        }

        localIsMounted = true;
        localRiderId = fakeClientId;
        currentRider = riderObject;

        //  Debug.Log($"[MountableEntity] Local mount complete");
    }

    /// <summary>
    /// Called by MountController when player wants to dismount.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestDismountServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong requestingClientId = rpcParams.Receive.SenderClientId;

        // Validate: is this the current rider?
        // Remember riderId is stored as (actualClientId + 1)
        if (riderId.Value != requestingClientId + 1)
        {
            Debug.LogWarning($"[MountableEntity] Dismount denied - {requestingClientId} is not the rider (riderId={riderId.Value})");
            return;
        }

        if (currentRider == null)
        {
            Debug.LogError("[MountableEntity] currentRider is null but riderId is set");
            riderId.Value = 0;
            SetMounted(false);
            return;
        }

        MountController mountController = currentRider.GetComponent<MountController>();
        if (mountController == null)
        {
            Debug.LogError("[MountableEntity] Rider has no MountController");
            riderId.Value = 0;
            return;
        }

        // Find safe dismount position
        Vector3 dismountPosition = FindSafeDismountPosition();

        // Clear rider
        riderId.Value = 0;

        // Unparent on server so everyone sees rider detached.
        if (mountController != null && mountController.NetworkObject != null)
        {
            // Parent to null (world) on server.
            mountController.NetworkObject.TryRemoveParent(true);
        }

        currentRider = null;
        SetMounted(false);
        // Sync horse yaw before returning ownership
        var alignment = GetComponent<DragonGroundAlignment>();
        if (alignment != null)
        {
            float currentYaw = transform.eulerAngles.y;
            alignment.SetYawImmediate(currentYaw);
        }
        // Return ownership to server (or keep as-is for AI control)
        // You can change this logic if you want mounts to be persistent
        NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        // Notify rider to complete dismount
        mountController.CompleteDismountClientRpc(dismountPosition);

        Debug.Log($"[MountableEntity] Client {requestingClientId} dismounted");
    }

    /// <summary>
    /// Offline mode: Dismount without networking
    /// </summary>
    public void DismountLocal()
    {
        if (!localIsMounted)
        {
            Debug.LogWarning("[MountableEntity] Not mounted locally");
            return;
        }

        localIsMounted = false;
        localRiderId = 0;
        currentRider = null;

        Debug.Log("[MountableEntity] Local dismount complete");
    }

    /// <summary>
    /// Finds nearest safe ground position around the mount for dismounting.
    /// </summary>
    private Vector3 FindSafeDismountPosition()
    {
        // Try dismount points in order: Left, Right, Front, Back
        Vector3[] offsets = new Vector3[]
        {
            transform.right * -1.5f,  // Left
            transform.right * 1.5f,   // Right
            transform.forward * 1.5f, // Front
            -transform.forward * 1.5f // Back
        };

        foreach (Vector3 offset in offsets)
        {
            Vector3 testPos = transform.position + offset;
            testPos.y += 2f; // Start raycast from above

            // Raycast down to find ground
            if (Physics.Raycast(testPos, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore))
            {
                Vector3 groundPos = hit.point;

                // Check if humanoid capsule fits here (prevents spawning in walls)
                if (!Physics.CheckCapsule(
                    groundPos + Vector3.up * 0.5f,
                    groundPos + Vector3.up * 1.8f,
                    0.3f, ~0, QueryTriggerInteraction.Ignore))
                {
                    return groundPos + Vector3.up * 0.1f; // Slightly above ground
                }
            }
        }

        // Fallback: just offset to the left
        Vector3 fallback = transform.position + transform.right * -1.5f;
        return fallback;
    }

    /// <summary>
    /// Returns the rider GameObject if mounted, null otherwise.
    /// </summary>
    public GameObject GetRider()
    {
        return currentRider;
    }

    // Debug visualization
    private void OnDrawGizmosSelected()
    {
        if (saddlePoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(saddlePoint.position, 0.2f);
            Gizmos.DrawLine(saddlePoint.position, saddlePoint.position + Vector3.up * 0.5f);
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }

    [ClientRpc]
    private void SyncYawClientRpc(float yaw)
    {
        var alignment = GetComponent<DragonGroundAlignment>();
        if (alignment != null)
            alignment.SetYawImmediate(yaw);
    }

    private void SetMounted(bool mounted)
    {
        if (_rb == null) return;
        if (mounted)
        {
            // Release all constraints — horse can move freely
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
        else
        {
            // Freeze everything — horse won't slide but stays non-kinematic
            _rb.constraints = RigidbodyConstraints.FreezeAll;
        }
    }
}