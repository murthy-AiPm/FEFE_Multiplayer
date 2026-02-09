using UnityEngine;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// Attach to humanoid player. Handles mounting/dismounting horses and state management.
/// Integrates with RuleAnimancerDriver for animation control.
/// 
/// NETWORK SYNC: isMounted and isTransitioning are synced via NetworkVariables
/// </summary>
public class MountController : NetworkBehaviour
{
    [Header("Animation Keys (configure in Inspector)")]
    [Tooltip("Animation key for mounting transition (e.g., 'Mount/Start')")]
    [SerializeField] private string mountAnimationKey = "Mount/Start";

    [Tooltip("Animation key for dismounting transition (e.g., 'Mount/Dismount')")]
    [SerializeField] private string dismountAnimationKey = "Mount/Dismount";

    [Tooltip("How long is the mounting animation in seconds")]
    [SerializeField] private float mountAnimationDuration = 1.5f;

    [Tooltip("How long is the dismounting animation in seconds")]
    [SerializeField] private float dismountAnimationDuration = 1.0f;

    [Header("Interaction")]
    [Tooltip("How long to hold E to mount (seconds)")]
    [SerializeField] private float holdDuration = 1.0f;

    [Tooltip("Key to hold for mounting/dismounting")]
    [SerializeField] private KeyCode mountKey = KeyCode.E;

    [Tooltip("Detection radius for nearby mounts")]
    [SerializeField] private float detectionRadius = 3f;

    [SerializeField] private LayerMask mountLayer = ~0;

    [Header("References")]
    [SerializeField] private ThirdPersonController thirdPersonController;
    [SerializeField] private InputController inputController;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Animator animator;

    // Network state (synced across all clients)
    private NetworkVariable<bool> netIsMounted = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> netIsTransitioning = new NetworkVariable<bool>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Local state (owner only)
    private MountableEntity currentMount;
    private bool isMounted;
    private bool isTransitioning;
    private float holdTimer;
    private MountableEntity nearbyMount;

    // Public read-only properties for AnimationContext
    // Online mode: read from NetworkVariables (synced)
    // Offline mode: read from local state
    public bool IsMounted => IsSpawned ? netIsMounted.Value : isMounted;
    public bool IsTransitioning => IsSpawned ? netIsTransitioning.Value : isTransitioning;
    public MountableEntity CurrentMount => currentMount;

    private void Awake()
    {
        if (thirdPersonController == null)
            thirdPersonController = GetComponent<ThirdPersonController>();
        if (inputController == null)
            inputController = GetComponent<InputController>();
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Detect nearby mounts
        DetectNearbyMount();

        // Handle mount/dismount input
        HandleMountInput();
    }

    private void LateUpdate()
    {
        // Only owner manually positions at saddle point
        // Remotes receive position via NetworkTransform parenting
        if (!IsOwner) return;

        // Keep humanoid positioned at saddle point while mounted
        if (IsMounted && !IsTransitioning && currentMount != null && currentMount.SaddlePoint != null)
        {
            transform.position = currentMount.SaddlePoint.position;
            transform.rotation = currentMount.SaddlePoint.rotation;
        }
    }

    private void DetectNearbyMount()
    {
        if (isMounted || isTransitioning)
        {
            nearbyMount = null;
            return;
        }

        // Find nearest mount within radius
        Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRadius, mountLayer);

        MountableEntity closest = null;
        float closestDist = float.MaxValue;

        foreach (Collider col in colliders)
        {
            MountableEntity mount = col.GetComponent<MountableEntity>();
            if (mount == null || mount.IsMounted) continue;

            float dist = Vector3.Distance(transform.position, col.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = mount;
            }
        }

        nearbyMount = closest;
    }

    private void HandleMountInput()
    {
        bool holdingKey = Input.GetKey(mountKey);

        if (isMounted)
        {
            // Dismount logic (instant press)
            if (Input.GetKeyDown(mountKey))
            {
                RequestDismount();
            }
        }
        else if (nearbyMount != null && !isTransitioning)
        {
            // Mount logic (hold E)
            if (holdingKey)
            {
                holdTimer += Time.deltaTime;

                if (holdTimer >= holdDuration)
                {
                    holdTimer = 0f;
                    RequestMount(nearbyMount);
                }
            }
            else
            {
                holdTimer = 0f;
            }
        }
        else
        {
            holdTimer = 0f;
        }
    }

    private void RequestMount(MountableEntity mount)
    {
        if (mount == null || mount.IsMounted) return;

        // Update local state
        isTransitioning = true;
        currentMount = mount;

        // IMMEDIATELY sync NetworkVariable so remotes see transitioning state
        SyncNetworkState();

        // Check if we're in online or offline mode
        bool isOnline = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (isOnline)
        {
            // Online mode: Request mount from server
            mount.RequestMountServerRpc(NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            // Offline mode: Mount directly
            Debug.Log("[MountController] Offline mode - mounting locally");
            mount.MountLocal(gameObject, 0);
            StartCoroutine(PlayMountTransition());
        }
    }

    private void RequestDismount()
    {
        if (currentMount == null) return;

        // Update local state
        isTransitioning = true;

        // IMMEDIATELY sync NetworkVariable
        SyncNetworkState();

        // Check if we're in online or offline mode
        bool isOnline = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (isOnline)
        {
            // Online mode: Request dismount from server
            currentMount.RequestDismountServerRpc();
        }
        else
        {
            // Offline mode: Dismount directly
            Debug.Log("[MountController] Offline mode - dismounting locally");
            Vector3 dismountPos = currentMount.transform.position + currentMount.transform.right * -1.5f;
            currentMount.DismountLocal();
            StartCoroutine(PlayDismountTransition(dismountPos));
        }
    }

    /// <summary>
    /// Syncs local state to NetworkVariables (owner only).
    /// Call this EVERY time isMounted or isTransitioning changes.
    /// </summary>
    private void SyncNetworkState()
    {
        if (!IsOwner || !IsSpawned) return;

        netIsMounted.Value = isMounted;
        netIsTransitioning.Value = isTransitioning;
    }

    /// <summary>
    /// Called by server via ClientRpc when mount is approved.
    /// </summary>
    [ClientRpc]
    public void CompleteMountClientRpc(ulong mountNetworkObjectId)
    {
        // Find the mount NetworkObject
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(mountNetworkObjectId, out NetworkObject mountNetObj))
        {
            Debug.LogError($"[MountController] Could not find mount NetworkObject {mountNetworkObjectId}");
            isTransitioning = false;
            SyncNetworkState();
            return;
        }

        currentMount = mountNetObj.GetComponent<MountableEntity>();
        if (currentMount == null)
        {
            Debug.LogError("[MountController] Mount NetworkObject has no MountableEntity");
            isTransitioning = false;
            SyncNetworkState();
            return;
        }

        StartCoroutine(PlayMountTransition());
    }

    private IEnumerator PlayMountTransition()
    {
        // Play mounting animation (handled by RuleAnimancerDriver via IsTransitioning)
        yield return new WaitForSeconds(mountAnimationDuration);

        // Complete mount
        FinishMount();
    }

    private void FinishMount()
    {
        // Update local state
        isMounted = true;
        isTransitioning = false;

        // Sync to network (owner only)
        SyncNetworkState();

        // Only owner performs reparenting and position changes
        // Remotes will receive position updates via NetworkTransform
        if (IsOwner)
        {
            // IMPORTANT: NetworkObjects can only be parented under other NetworkObjects
            Transform horseRoot = currentMount.transform;
            transform.SetParent(horseRoot);

            // Position at saddle point
            if (currentMount.SaddlePoint != null)
            {
                transform.position = currentMount.SaddlePoint.position;
                transform.rotation = currentMount.SaddlePoint.rotation;
            }
            else
            {
                transform.localPosition = Vector3.up * 1.5f;
                transform.localRotation = Quaternion.identity;
            }

            // Disable humanoid movement
            if (thirdPersonController != null)
                thirdPersonController.enabled = false;

            if (characterController != null)
                characterController.enabled = false;
        }

        Debug.Log($"[MountController] Mount complete (IsOwner: {IsOwner})");
    }

    /// <summary>
    /// Called by server via ClientRpc when dismount is approved.
    /// </summary>
    [ClientRpc]
    public void CompleteDismountClientRpc(Vector3 dismountPosition)
    {
        StartCoroutine(PlayDismountTransition(dismountPosition));
    }

    private IEnumerator PlayDismountTransition(Vector3 targetPosition)
    {
        // Play dismounting animation
        yield return new WaitForSeconds(dismountAnimationDuration);

        // Complete dismount
        FinishDismount(targetPosition);
    }

    private void FinishDismount(Vector3 position)
    {
        // Update local state
        isMounted = false;
        isTransitioning = false;

        // Sync to network (owner only)
        SyncNetworkState();

        // Only owner performs unparenting and position changes
        if (IsOwner)
        {
            // Unparent from horse
            transform.SetParent(null);

            // Position at dismount location
            transform.position = position;

            // Re-enable humanoid movement
            if (thirdPersonController != null)
                thirdPersonController.enabled = true;

            if (characterController != null)
                characterController.enabled = true;

            currentMount = null;
        }

        Debug.Log($"[MountController] Dismount complete (IsOwner: {IsOwner})");
    }

    /// <summary>
    /// For UI: returns nearby mount if player is close enough to mount.
    /// </summary>
    public MountableEntity GetNearbyMount()
    {
        return nearbyMount;
    }

    /// <summary>
    /// For UI: returns mount hold progress (0-1).
    /// </summary>
    public float GetMountHoldProgress()
    {
        return holdTimer / holdDuration;
    }

    // Debug visualization
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        if (nearbyMount != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, nearbyMount.transform.position);
        }
    }
}