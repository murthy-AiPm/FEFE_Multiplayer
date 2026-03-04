using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Attach to Ballista root. Handles:
/// - Horizontal base rotation (camera yaw driven)
/// - Vertical barrel pitch (camera pitch driven)
/// - Firing and reload
/// - Operator slot (one player at a time)
/// - Network sync of rotation and state
/// 
/// Hierarchy expected:
///   Ballista (root, NetworkObject, BallistaController)
///     └── Base (rotates horizontally)
///           └── Barrel (pitches vertically)
///                 └── FirePoint (arrow spawn, forward = fire direction)
/// </summary>
public class BallistaController : NetworkBehaviour
{
    [Header("Transforms")]
    [SerializeField] private Transform ballistaBase;    // rotates horizontally
    [SerializeField] private Transform barrel;          // pitches vertically
    [SerializeField] private Transform firePoint;       // arrow spawn point

    [Header("Rotation Limits")]
    [Tooltip("Total horizontal rotation allowed in degrees. 360 = full rotation. Set lower to restrict arc.")]
    [SerializeField] private float horizontalRotationLimit = 360f;
    [Tooltip("Center angle of the allowed horizontal arc (world space yaw)")]
    [SerializeField] private float horizontalCenter = 0f;
    [Tooltip("Min vertical pitch in degrees (negative = down)")]
    [SerializeField] private float minPitch = -10f;
    [Tooltip("Max vertical pitch in degrees (positive = up)")]
    [SerializeField] private float maxPitch = 40f;

    [Header("Interaction")]
    [SerializeField] private float interactionRadius = 2.5f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Firing")]
    [SerializeField] private GameObject arrowPrefab;
    [SerializeField] private float arrowSpeed = 80f;
    [SerializeField] private float reloadTime = 3f;
    [SerializeField] private KeyCode fireKey = KeyCode.Mouse0;

    [Header("Operator Offset")]
    [Tooltip("Where the player stands relative to ballista root")]
    [SerializeField] private Transform operatorStandPoint;

    // Network state
    private NetworkVariable<float> _netYaw = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> _netPitch = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<ulong> _operatorId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> _isReloading = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Local state
    private float _currentYaw;
    private float _currentPitch;
    private float _reloadTimer;
    private BallistaOperator _currentOperator;

    // Public state
    public bool IsOccupied => _operatorId.Value != ulong.MaxValue;
    public bool IsReloading => _isReloading.Value;
    public Transform OperatorStandPoint => operatorStandPoint;
    public Transform FirePoint => firePoint;
    public float MinPitch => minPitch;
    public float MaxPitch => maxPitch;

    private void Awake()
    {
        // Initialize yaw to current rotation
        _currentYaw = transform.eulerAngles.y;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _netYaw.OnValueChanged += OnYawChanged;
        _netPitch.OnValueChanged += OnPitchChanged;
        _operatorId.OnValueChanged += OnOperatorChanged;

        // Apply initial values
        ApplyRotation(_netYaw.Value, _netPitch.Value);
    }

    public override void OnNetworkDespawn()
    {
        _netYaw.OnValueChanged -= OnYawChanged;
        _netPitch.OnValueChanged -= OnPitchChanged;
        _operatorId.OnValueChanged -= OnOperatorChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        // Reload timer (server only)
        if (IsServer && _isReloading.Value)
        {
            _reloadTimer -= Time.deltaTime;
            if (_reloadTimer <= 0f)
                _isReloading.Value = false;
        }

        // Only owner drives rotation and firing
        if (!IsOwner) return;
        if (!IsOccupied) return;

        HandleRotationInput();
        HandleFireInput();
    }

    private void HandleRotationInput()
    {
        if (Camera.main == null) return;

        // Horizontal — follow camera yaw
        float camYaw = Camera.main.transform.eulerAngles.y;

        // Apply horizontal limit
        if (horizontalRotationLimit < 360f)
        {
            float halfLimit = horizontalRotationLimit * 0.5f;
            float delta = Mathf.DeltaAngle(horizontalCenter, camYaw);
            delta = Mathf.Clamp(delta, -halfLimit, halfLimit);
            camYaw = horizontalCenter + delta;
        }

        _currentYaw = camYaw;

        // Vertical — follow camera pitch
        float camPitch = Camera.main.transform.eulerAngles.x;
        // Convert from 0-360 to -180-180
        if (camPitch > 180f) camPitch -= 360f;
        _currentPitch = Mathf.Clamp(-camPitch, minPitch, maxPitch);

        // Sync to network
        _netYaw.Value = _currentYaw;
        _netPitch.Value = _currentPitch;

        // Apply locally (no need to wait for network callback on owner)
       // Debug.Log($"[Ballista] camPitch raw: {Camera.main.transform.eulerAngles.x}, currentPitch: {_currentPitch}");
        ApplyRotation(_currentYaw, _currentPitch);
    }

    private void HandleFireInput()
    {
        if (!Input.GetKeyDown(fireKey)) return;
        if (_isReloading.Value) return;

        // Pass fire direction from client so server uses correct pitch
        RequestFireServerRpc(firePoint.position, firePoint.rotation);
        Debug.Log($"[Ballista] Client firePoint.forward: {firePoint.forward}");

    }

    private void ApplyRotation(float yaw, float pitch)
    {
        Debug.Log($"[Ballista] Barrel localRotation: {barrel.localRotation.eulerAngles}, FirePoint worldRotation: {firePoint.rotation.eulerAngles}");
        if (ballistaBase != null)
            ballistaBase.rotation = Quaternion.Euler(0f, yaw, 0f);

        if (barrel != null)
            barrel.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
    }

    // ─── Network Callbacks ───

    private void OnYawChanged(float oldVal, float newVal)
    {
        if (IsOwner) return; // owner already applied locally
        ApplyRotation(newVal, _netPitch.Value);
    }

    private void OnPitchChanged(float oldVal, float newVal)
    {
        if (IsOwner) return;
        ApplyRotation(_netYaw.Value, newVal);
    }

    private void OnOperatorChanged(ulong oldVal, ulong newVal)
    {
       //
       //Debug.Log($"[BallistaController] Operator changed: {oldVal} -> {newVal}");
    }

    // ─── ServerRpcs ───

    [ServerRpc(RequireOwnership = false)]
    public void RequestMountServerRpc(ulong clientId, ServerRpcParams rpcParams = default)
    {
        if (IsOccupied)
        {
            Debug.LogWarning("[BallistaController] Already occupied");
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return;

        if (client.PlayerObject == null) return;

        var operator_ = client.PlayerObject.GetComponent<BallistaOperator>();
        if (operator_ == null)
        {
            Debug.LogError("[BallistaController] Player has no BallistaOperator component");
            return;
        }

        _operatorId.Value = clientId;

        // Transfer ownership to operator so they can write rotation
        NetworkObject.ChangeOwnership(clientId);

        // Notify operator
        operator_.CompleteMountClientRpc(NetworkObjectId);

        Debug.Log($"[BallistaController] Client {clientId} mounted ballista");
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestDismountServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (_operatorId.Value != clientId)
        {
            Debug.LogWarning("[BallistaController] Dismount denied - not the operator");
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return;

        var operator_ = client.PlayerObject?.GetComponent<BallistaOperator>();

        _operatorId.Value = ulong.MaxValue;
        NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        operator_?.CompleteDismountClientRpc();

        //Debug.Log($"[BallistaController] Client {clientId} dismounted ballista");
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestFireServerRpc(Vector3 spawnPosition, Quaternion spawnRotation, ServerRpcParams rpcParams = default)
    {
        if (_isReloading.Value) return;
        if (arrowPrefab == null) return;

        // Use client-provided position/rotation so pitch is correct
        var arrow = Instantiate(arrowPrefab, spawnPosition, spawnRotation);
        var rb = arrow.GetComponent<Rigidbody>();
        Debug.Log($"[Ballista] spawnRotation forward: {spawnRotation * Vector3.forward}");
        if (rb != null)
            rb.linearVelocity = spawnRotation * Vector3.forward * arrowSpeed;

        var netObj = arrow.GetComponent<NetworkObject>();
        if (netObj != null)
            netObj.Spawn();

        // Start reload
        _isReloading.Value = true;
        _reloadTimer = reloadTime;

        // Notify all clients for fire effects/animation
        NotifyFireClientRpc();
    }

    [ClientRpc]
    private void NotifyFireClientRpc()
    {
        // Notify the local operator to play fire animation
        if (!IsOccupied) return;
        ulong myId = NetworkManager.Singleton.LocalClientId;
        if (_operatorId.Value != myId) return;

        _currentOperator?.OnFired();
    }

    // ─── Public API ───

    public void RegisterOperator(BallistaOperator op) => _currentOperator = op;
    public void UnregisterOperator() => _currentOperator = null;

    // ─── Gizmos ───

    private void OnDrawGizmosSelected()
    {
        // Interaction radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);

        // Fire direction
        if (firePoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(firePoint.position, firePoint.forward * 3f);
        }

        // Operator stand point
        if (operatorStandPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(operatorStandPoint.position, 0.2f);
        }

        // Horizontal arc visualization
        if (horizontalRotationLimit < 360f)
        {
            Gizmos.color = Color.green;
            float halfLimit = horizontalRotationLimit * 0.5f;
            Vector3 leftDir = Quaternion.Euler(0, horizontalCenter - halfLimit, 0) * Vector3.forward;
            Vector3 rightDir = Quaternion.Euler(0, horizontalCenter + halfLimit, 0) * Vector3.forward;
            Gizmos.DrawRay(transform.position, leftDir * 3f);
            Gizmos.DrawRay(transform.position, rightDir * 3f);
        }
    }
}