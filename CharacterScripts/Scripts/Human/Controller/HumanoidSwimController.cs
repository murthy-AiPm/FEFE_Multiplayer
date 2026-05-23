using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner-side humanoid swim state, movement, and combat/weapon suppression.
/// Animation consumes this through AnimationContext and HumanoidSwimMixer.
/// </summary>
[RequireComponent(typeof(HumanoidSwimSystem))]
public class HumanoidSwimController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private HumanoidSwimSystem swimSystem;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private CombatController combatController;
    [SerializeField] private Transform root;
    [SerializeField] private Transform cam;

    [Header("Input")]
    [SerializeField] private KeyCode swimUpKey = KeyCode.Space;
    [SerializeField] private KeyCode swimDownKey = KeyCode.LeftControl;

    [Header("Movement")]
    [SerializeField] private float surfaceSwimSpeed = 2.2f;
    [SerializeField] private float surfaceFastSwimSpeed = 3.8f;
    [SerializeField] private float underwaterSwimSpeed = 2.5f;
    [SerializeField] private float underwaterFastSwimSpeed = 4.2f;
    [SerializeField] private float verticalSwimSpeed = 2.2f;
    [SerializeField] private float rotationSmoothTime = 0.08f;
    [SerializeField] private float surfaceFloatSpeed = 0f;
    [SerializeField] private float surfaceHoldDepth = 0.35f;

    private float _turnSmoothVelocity;
    private bool _isSwimming;
    private bool _isUnderwater;
    private Vector2 _swimMoveInput;
    private float _swimVertical;
    private bool _isFastSwimming;

    public bool IsSwimming => _isSwimming;
    public bool IsUnderwater => _isUnderwater;
    public Vector2 SwimMoveInput => _swimMoveInput;
    public float SwimVertical => _swimVertical;
    public bool IsFastSwimming => _isFastSwimming;

    private void Awake()
    {
        if (swimSystem == null) swimSystem = GetComponent<HumanoidSwimSystem>();
        if (characterController == null) characterController = GetComponentInParent<CharacterController>();
        if (characterController == null) characterController = GetComponentInChildren<CharacterController>(true);
        if (playerController == null) playerController = GetComponentInParent<PlayerController>();
        if (playerController == null) playerController = GetComponentInChildren<PlayerController>(true);
        if (weaponManager == null) weaponManager = GetComponentInParent<WeaponManager>();
        if (weaponManager == null) weaponManager = GetComponentInChildren<WeaponManager>(true);
        if (combatController == null) combatController = GetComponentInParent<CombatController>();
        if (combatController == null) combatController = GetComponentInChildren<CombatController>(true);
        if (root == null) root = characterController != null ? characterController.transform : transform;
        if (cam == null) cam = Camera.main != null ? Camera.main.transform : null;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        if (cam == null && Camera.main != null)
            cam = Camera.main.transform;

        bool shouldSwim = swimSystem != null && swimSystem.IsSwimming;
        if (shouldSwim && !_isSwimming)
            EnterWater();
        else if (!shouldSwim && _isSwimming)
            ExitWater();

        if (!_isSwimming)
            return;

        ReadInput();
        ApplySwimMovement();
        ForceNoWeapons();
    }

    private void EnterWater()
    {
        _isSwimming = true;
        _isUnderwater = swimSystem != null && swimSystem.IsUnderwater;
        ForceNoWeapons();
        combatController?.ResetState();
    }

    private void ExitWater()
    {
        _isSwimming = false;
        _isUnderwater = false;
        _swimMoveInput = Vector2.zero;
        _swimVertical = 0f;
        _isFastSwimming = false;
    }

    private void ReadInput()
    {
        InputSnapshot snapshot = playerController != null && playerController.inputController != null
            ? playerController.inputController.Snapshot
            : default;

        _isUnderwater = swimSystem != null && swimSystem.IsUnderwater;
        _swimMoveInput = snapshot.move;
        _isFastSwimming = snapshot.modifiedHeld && _swimMoveInput.y > 0.1f;

        float vertical = 0f;
        if (Input.GetKey(swimUpKey))
            vertical += 1f;
        if (Input.GetKey(swimDownKey))
            vertical -= 1f;
        _swimVertical = Mathf.Clamp(vertical, -1f, 1f);
    }

    private void ApplySwimMovement()
    {
        if (characterController == null)
            return;

        Vector3 horizontal = Vector3.zero;
        if (_swimMoveInput.sqrMagnitude > 0.001f && cam != null)
        {
            Vector3 camForward = cam.forward;
            Vector3 camRight = cam.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            horizontal = (camForward * _swimMoveInput.y + camRight * _swimMoveInput.x).normalized;
            float targetAngle = Mathf.Atan2(horizontal.x, horizontal.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(root.eulerAngles.y, targetAngle, ref _turnSmoothVelocity, rotationSmoothTime);
            root.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        float speed = _isUnderwater
            ? (_isFastSwimming ? underwaterFastSwimSpeed : underwaterSwimSpeed)
            : (_isFastSwimming ? surfaceFastSwimSpeed : surfaceSwimSpeed);

        Vector3 velocity = horizontal * speed;
        velocity.y = GetVerticalVelocity();
        characterController.Move(velocity * Time.deltaTime);
    }

    private float GetVerticalVelocity()
    {
        if (_isUnderwater)
            return _swimVertical * verticalSwimSpeed;

        if (_swimVertical > 0f)
            return _swimVertical * verticalSwimSpeed;
        if (_swimVertical < 0f)
            return _swimVertical * verticalSwimSpeed * 0.5f;

        if (swimSystem != null && swimSystem.SubmersionDepth > surfaceHoldDepth)
            return surfaceFloatSpeed;

        return 0f;
    }

    private void ForceNoWeapons()
    {
        if (weaponManager != null && weaponManager.ActiveSlot != 0)
            weaponManager.InstantEquip(0);
    }

    public void SetRemoteSwimState(bool swimming, bool underwater, Vector2 moveInput, float vertical, bool fast)
    {
        if (IsOwner)
            return;

        _isSwimming = swimming;
        _isUnderwater = underwater;
        _swimMoveInput = moveInput;
        _swimVertical = vertical;
        _isFastSwimming = fast;
    }
}
