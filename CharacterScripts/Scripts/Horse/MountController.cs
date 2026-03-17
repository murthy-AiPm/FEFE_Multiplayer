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
    [Header("Hit Detection")]
    [SerializeField] private CapsuleCollider hitCollider;

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
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private CombatController combatController;

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

        DetectNearbyMount();
        HandleMountInput();
    }

    private void DetectNearbyMount()
    {
        if (isMounted || isTransitioning)
        {
            nearbyMount = null;
            return;
        }

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
            if (Input.GetKeyDown(mountKey))
                RequestDismount();
        }
        else if (nearbyMount != null && !isTransitioning)
        {
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

        if (hitCollider != null) hitCollider.enabled = false;

        isTransitioning = true;
        currentMount = mount;
        SyncNetworkState();

        bool isOnline = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (isOnline)
            mount.RequestMountServerRpc(NetworkManager.Singleton.LocalClientId);
        else
        {
            mount.MountLocal(gameObject, 0);
            StartCoroutine(PlayMountTransition());
        }
    }

    private void RequestDismount()
    {
        if (currentMount == null) return;

        isTransitioning = true;
        SyncNetworkState();

        bool isOnline = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (isOnline)
            currentMount.RequestDismountServerRpc();
        else
        {
            Vector3 dismountPos = currentMount.transform.position + currentMount.transform.right * -1.5f;
            currentMount.DismountLocal();
            StartCoroutine(PlayDismountTransition(dismountPos));
        }
    }

    /// <summary>
    /// Force-dismount immediately with no animation — used on death/respawn.
    /// </summary>
    public void ForceDismount()
    {
        if (!isMounted && !isTransitioning) return;

        // Stop any in-progress mount/dismount coroutines
        StopAllCoroutines();

        if (currentMount != null)
        {
            bool isOnline = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (isOnline)
                currentMount.RequestDismountServerRpc();
            else
                currentMount.DismountLocal();
        }

        // Immediately reset state without animation
        Vector3 dismountPos = currentMount != null
            ? currentMount.transform.position + currentMount.transform.right * -1.5f
            : transform.position;

        FinishDismount(dismountPos);
    }

    private void SyncNetworkState()
    {
        if (!IsOwner || !IsSpawned) return;

        netIsMounted.Value = isMounted;
        netIsTransitioning.Value = isTransitioning;
    }

    [ClientRpc]
    public void CompleteMountClientRpc(ulong mountNetworkObjectId)
    {
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
        yield return new WaitForSeconds(mountAnimationDuration);
        FinishMount();
    }

    private void FinishMount()
    {
        isMounted = true;
        isTransitioning = false;
        SyncNetworkState();

        if (IsOwner)
        {
            if (!IsSpawned)
                transform.SetParent(currentMount.transform);

            if (currentMount != null && currentMount.SaddlePoint != null)
                transform.SetPositionAndRotation(currentMount.SaddlePoint.position, currentMount.SaddlePoint.rotation);

            if (thirdPersonController != null)
                thirdPersonController.enabled = false;

            if (characterController != null)
                characterController.enabled = false;

            if (weaponManager != null && weaponManager.ActiveSlot != 0)
                weaponManager.InstantEquip(0);

            var footsteps = GetComponentInChildren<FootstepSoundPlayer>();
            if (footsteps != null)
                footsteps.enabled = false;
        }

        Debug.Log($"[MountController] Mount complete (IsOwner: {IsOwner})");
    }

    [ClientRpc]
    public void CompleteDismountClientRpc(Vector3 dismountPosition)
    {
        StartCoroutine(PlayDismountTransition(dismountPosition));
    }

    private IEnumerator PlayDismountTransition(Vector3 targetPosition)
    {
        yield return new WaitForSeconds(dismountAnimationDuration);
        FinishDismount(targetPosition);
    }

    private void FinishDismount(Vector3 position)
    {
        isMounted = false;
        isTransitioning = false;
        SyncNetworkState();

        if (IsOwner)
        {
            if (!IsSpawned)
                transform.SetParent(null);

            transform.position = position;

            if (currentMount != null)
            {
                var horseController = currentMount.GetComponentInChildren<DragonGroundController>();
                Debug.Log($"[MountController] horseController found: {horseController != null} on {currentMount.name}");
                if (horseController != null)
                    horseController.StopGradually();
            }

            if (weaponManager != null && combatController != null && !combatController.allowMountedCombat)
            {
                if (weaponManager.ActiveSlot != 0)
                    weaponManager.InstantEquip(0);
            }

            if (thirdPersonController != null)
                thirdPersonController.enabled = true;

            var footsteps = GetComponentInChildren<FootstepSoundPlayer>();
            if (footsteps != null)
                footsteps.enabled = true;

            if (hitCollider != null) hitCollider.isTrigger = false;

            if (characterController != null)
                characterController.enabled = true;

            currentMount = null;
        }

        Debug.Log($"[MountController] Dismount complete (IsOwner: {IsOwner})");
    }

    public MountableEntity GetNearbyMount() => nearbyMount;
    public float GetMountHoldProgress() => holdTimer / holdDuration;

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
