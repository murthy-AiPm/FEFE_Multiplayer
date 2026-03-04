using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;

/// <summary>
/// Attach to player prefab root.
/// Handles:
/// - Detecting nearby ballista and prompting interact
/// - Mounting/dismounting ballista
/// - IK spine bend toward aim point via OnAnimatorIK
/// - Disabling normal player input while operating
/// - Playing grip/fire/reload animations via Animancer
/// </summary>
public class BallistaOperator : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private Animator animator;
    [SerializeField] private RuleAnimancerDriver animancerDriver;
    [SerializeField] private ThirdPersonController thirdPersonController;
    [SerializeField] private CombatController combatController;
    [SerializeField] private InputController inputController;

    [Header("Camera")]
    [SerializeField] private CinemachineCamera vcam;
    [SerializeField] private Transform camCube;
    [SerializeField] private Transform ballistaCube;

    [Header("IK Settings")]
    [Tooltip("How strongly the spine bends toward aim (0-1)")]
    [SerializeField] private float spineIKWeight = 0.6f;
    [Tooltip("How strongly the head looks toward aim (0-1)")]
    [SerializeField] private float headIKWeight = 0.8f;
    [Tooltip("How far ahead the IK look target is placed")]
    [SerializeField] private float aimDistance = 20f;
    [Tooltip("Speed at which IK weights blend in/out")]
    [SerializeField] private float ikBlendSpeed = 5f;

    [Header("Hand IK")]
    [SerializeField] private float leftHandIKWeight = 0.8f;
    [SerializeField] private float rightHandIKWeight = 0.8f;
    [Tooltip("Where left hand grips on the ballista (assign after mounting)")]
    [SerializeField] private Transform leftHandGrip;
    [Tooltip("Where right hand grips on the ballista")]
    [SerializeField] private Transform rightHandGrip;

    [Header("Interaction")]
    [SerializeField] private float interactionRadius = 2.5f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private KeyCode dismountKey = KeyCode.F;

    // State
    public bool IsOperating { get; private set; }
    private BallistaController _currentBallista;
    private float _currentIKWeight = 0f;
    private Vector3 _aimTarget;
    public BallistaController CurrentBallista => _currentBallista;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animancerDriver == null) animancerDriver = GetComponentInChildren<RuleAnimancerDriver>();
        if (thirdPersonController == null) thirdPersonController = GetComponent<ThirdPersonController>();
        if (combatController == null) combatController = GetComponent<CombatController>();
        if (inputController == null) inputController = GetComponentInChildren<InputController>();
    }

    private void Update()
    {
        if (!IsOwner || !IsSpawned) return;

        if (!IsOperating)
        {
            CheckForBallista();
        }
        else
        {
            UpdateOperating();
        }
    }

    private void CheckForBallista()
    {
        // Find nearest ballista within range
        var ballistae = FindObjectsByType<BallistaController>(FindObjectsSortMode.None);
        BallistaController nearest = null;
        float nearestDist = interactionRadius;

        foreach (var b in ballistae)
        {
            float dist = Vector3.Distance(transform.position, b.transform.position);
            if (dist < nearestDist && !b.IsOccupied)
            {
                nearestDist = dist;
                nearest = b;
            }
        }

        // Show prompt and handle input
        if (nearest != null && Input.GetKeyDown(interactKey))
        {
            RequestMount(nearest);
        }
    }

    private void RequestMount(BallistaController ballista)
    {
        _currentBallista = ballista;
        ballista.RequestMountServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    private void UpdateOperating()
    {
        if (_currentBallista == null) return;

        // Dismount
        if (Input.GetKeyDown(dismountKey))
        {
            _currentBallista.RequestDismountServerRpc();
            return;
        }

        // Keep player at stand point, rotating with the base
        if (_currentBallista.OperatorStandPoint != null)
        {
            transform.position = _currentBallista.OperatorStandPoint.position;
            float baseYaw = _currentBallista.BallistaBase != null
                ? _currentBallista.BallistaBase.eulerAngles.y
                : _currentBallista.transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, baseYaw, 0f);
        }

        // Update aim target for IK (point camera looks at)
        if (Camera.main != null)
        {
            _aimTarget = Camera.main.transform.position +
                         Camera.main.transform.forward * aimDistance;
        }

        // Blend IK weight in
        _currentIKWeight = Mathf.MoveTowards(_currentIKWeight, 1f,
            ikBlendSpeed * Time.deltaTime);
    }

    // ─── Called by BallistaController ───

    [ClientRpc]
    public void CompleteMountClientRpc(ulong ballistaNetId)
    {
        if (!IsOwner) return;

        // Find the ballista by network ID
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects
            .TryGetValue(ballistaNetId, out var netObj))
        {
            _currentBallista = netObj.GetComponent<BallistaController>();
            _currentBallista?.RegisterOperator(this);
        }

        IsOperating = true;
        if (vcam != null && ballistaCube != null)
            vcam.Target.TrackingTarget = ballistaCube;
        // Disable normal player controls
        if (thirdPersonController != null) thirdPersonController.enabled = false;
        if (combatController != null) combatController.enabled = false;

        // Play grip animation
        // animancerDriver?.PlayBallistaGrip(); // wire up when anim is ready

        Debug.Log("[BallistaOperator] Mounted ballista");
    }

    [ClientRpc]
    public void CompleteDismountClientRpc()
    {
        if (!IsOwner) return;

        _currentBallista?.UnregisterOperator();
        _currentBallista = null;
        IsOperating = false;
        if (vcam != null && camCube != null)
            vcam.Target.TrackingTarget = camCube;
        // Re-enable player controls
        if (thirdPersonController != null) thirdPersonController.enabled = true;
        if (combatController != null) combatController.enabled = true;

        // Blend IK out
        _currentIKWeight = 0f;

        // Return to idle animation
        // animancerDriver?.PlayIdle(); // wire up when needed

        Debug.Log("[BallistaOperator] Dismounted ballista");
    }

    /// <summary>
    /// Called by BallistaController when a shot is fired.
    /// Trigger fire animation here.
    /// </summary>
    public void OnFired()
    {
        // animancerDriver?.PlayBallistaFire(); // wire up when anim is ready
        Debug.Log("[BallistaOperator] Fire animation triggered");
    }

    // ─── IK ───

    private void OnAnimatorIK(int layerIndex)
    {
        if (!IsOperating || animator == null) return;

        float weight = _currentIKWeight;

        // Spine/body look at aim target
        animator.SetLookAtWeight(
            weight * headIKWeight,      // overall weight
            weight * spineIKWeight,     // body weight
            weight * headIKWeight,      // head weight
            0f,                         // eyes weight
            0.5f                        // clamp weight
        );
        animator.SetLookAtPosition(_aimTarget);

        // Left hand grip
        if (leftHandGrip != null)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, weight * leftHandIKWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, weight * leftHandIKWeight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandGrip.position);
            animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandGrip.rotation);
        }

        // Right hand grip
        if (rightHandGrip != null)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight * rightHandIKWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, weight * rightHandIKWeight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, rightHandGrip.position);
            animator.SetIKRotation(AvatarIKGoal.RightHand, rightHandGrip.rotation);
        }
    }

    // ─── Gizmos ───

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}