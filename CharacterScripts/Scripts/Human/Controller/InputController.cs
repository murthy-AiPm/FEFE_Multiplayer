using UnityEngine;

[System.Serializable]
public struct InputSnapshot
{
    // Movement
    public Vector2 move;              // raw input (-1..1)
    public bool movePressed;          // any movement intent this frame

    // Modifiers / actions
    public bool modifiedHeld;         // sprint / modifier
    public bool actionHeld;
    public bool secondaryHeld;

    // Jump / hover
    public bool jumpDown;             // edge: pressed this frame
    public bool jumpHeld;             // held
    public bool crouchToggleDown;     // edge: toggle crouch this frame
    public bool hoverToggleDown;      // edge: toggle hover this frame

    // Combat
    public bool primaryHeld;
    public bool hoistWeaponsDown;     // edge
    public bool slot1Down;
    public bool slot2Down;
    public bool slot3Down;

    public bool primaryDown; // edge: pressed this frame
    public bool primaryUp;   // edge: released this frame (optional)
}

public class InputController : MonoBehaviour
{
    [SerializeField] PlayerController playerController;

    // Existing public flags (kept for compatibility)
    public bool isMoving;
    public bool isForward;
    public bool isBackward;
    public bool isLeft;
    public bool isRight;
    public bool isModified;
    public bool isFalling;
    public bool isJumpPressed;
    public bool isTurning;
    public bool _wasGrounded;
    public bool onGround;
    public bool isAction;
    public bool isFlying;
    public bool isAirBrake;
    public bool isCondition;
    public bool isCrouch = false;
    public bool isHoverMode = false;
    public string directions;
    public bool isClimbing;

    // Combat — now driven by WeaponManager
    public bool isPrimaryAttack, isWeaponDischarge;
    public bool isSecondaryAttack;
    public bool isCombatMode = false;
    public bool isSheating;

    // DEPRECATED — kept as fields so nothing breaks at compile time,
    // but no longer toggled by InputController. WeaponManager owns weapon state.
    [HideInInspector] public bool fistEquip;
    [HideInInspector] public bool swordEquip;

    // NEW: snapshot output
    public InputSnapshot Snapshot { get; private set; }

    // Cached refs
    private HumanoidColliderManger _humanoidCollider;
    private WeaponManager _weaponManager;
    private CombatController _combatController;

    // Jump continuity (your old logic preserved)
    [SerializeField] bool previousjump;
    [SerializeField] bool currentjump = true;

    void Awake()
    {
        _humanoidCollider = GetComponent<HumanoidColliderManger>(); // may be null (dragon)
        _weaponManager = GetComponentInParent<WeaponManager>();
        _combatController = GetComponentInParent<CombatController>();
    }

    void Update()
    {
        // Lazy lookup if not found at Awake
        if (_weaponManager == null)
            _weaponManager = GetComponentInParent<WeaponManager>();
        if (_combatController == null)
            _combatController = GetComponentInParent<CombatController>();

        // world state (not input)
        onGround = playerController.TPS.isgrounded;
        isFalling = playerController.TPS.isfreeFall;

        if (onGround && !_wasGrounded)
        {
            // clear transient/one-shot flags that can block locomotion cards
            isJumpPressed = false;
            isPrimaryAttack = false;
            isWeaponDischarge = false;
            isSheating = false;
        }
        _wasGrounded = onGround;

        // 1) Read input ONCE
        Snapshot = ReadSnapshot();

        // 2) Apply snapshot into existing flags (compat)
        ApplySnapshotToLegacyFlags(Snapshot);
    }

    private InputSnapshot ReadSnapshot()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        return new InputSnapshot
        {
            move = new Vector2(horizontal, vertical),
            movePressed = (Mathf.Abs(horizontal) > 0.01f || Mathf.Abs(vertical) > 0.01f),

            modifiedHeld = Input.GetButton("Modified"),
            actionHeld = Input.GetButton("Action"),
            secondaryHeld = Input.GetButton("SecondaryAttack"),

            jumpDown = Input.GetButtonDown("Jump"),
            jumpHeld = Input.GetButton("Jump"),

            crouchToggleDown = Input.GetButtonDown("Crouch"),
            hoverToggleDown = Input.GetButtonDown("HoverMode"),

            primaryHeld = Input.GetButton("PrimaryAttack"),
            hoistWeaponsDown = Input.GetButtonDown("HoistWeapons"),

            slot1Down = Input.GetKeyDown(KeyCode.Alpha1),
            slot2Down = Input.GetKeyDown(KeyCode.Alpha2),
            slot3Down = Input.GetKeyDown(KeyCode.Alpha3),

            primaryDown = Input.GetButtonDown("PrimaryAttack"),
            primaryUp = Input.GetButtonUp("PrimaryAttack"),
        };
    }

    private void ApplySnapshotToLegacyFlags(InputSnapshot s)
    {
        // Shared
        isMoving = s.movePressed;
        isModified = s.modifiedHeld;
        isAction = s.actionHeld;
        isSecondaryAttack = s.secondaryHeld;

        // Direction string compatibility
        directions = GetDirectionString(s.move);

        // Branch per character
        ApplyHumanFromSnapshot(s);
    }

    private void ApplyHumanFromSnapshot(InputSnapshot s)
    {
        // Jump — edge trigger only on the frame pressed while grounded
        isJumpPressed = (s.jumpDown && onGround);

        if (isJumpPressed)
            isCrouch = false;

        // Crouch toggle
        if (s.crouchToggleDown)
            isCrouch = !isCrouch;

        // Hover off for humans
        isHoverMode = false;

        // ─── Combat mode: driven by WeaponManager + CombatController ───
        // Active when any weapon equipped OR in fist combat mode
        if (_weaponManager != null)
        {
            bool weaponEquipped = _weaponManager.ActiveSlot != 0;
            bool fistMode = _combatController != null && _combatController.IsFistCombatMode;
            isCombatMode = weaponEquipped || fistMode;
        }

        // Primary attack flag (for any systems still reading this)
        if (isCombatMode && s.primaryHeld)
            isPrimaryAttack = true;
        else
            isPrimaryAttack = false;

        // Combat mode disables crouch
        if (isCombatMode)
            isCrouch = false;
    }

    // ─── Dragon input (unchanged) ───

    public void ApplyDragonFromSnapshot(InputSnapshot s)
    {
        if (s.jumpDown && (onGround && isModified && isMoving))
        {
            isJumpPressed = true;
            previousjump = true;
        }
        else if (!onGround && previousjump)
        {
            isJumpPressed = true;
            currentjump = false;
        }

        if (currentjump == false && onGround)
        {
            isJumpPressed = false;
            currentjump = true;
            previousjump = false;
        }

        ApplyHoverFromSnapshot(s);

        if (s.slot1Down || s.slot2Down || s.slot3Down)
        {
            isCombatMode = true;
        }
        else if (s.hoistWeaponsDown)
        {
            isCombatMode = false;
        }

        if (isCombatMode && s.primaryHeld)
        {
            isPrimaryAttack = true;
            isWeaponDischarge = true;
        }
        else
        {
            isPrimaryAttack = false;
            isWeaponDischarge = false;
        }

        if (isMoving && onGround && !isFlying)
            isPrimaryAttack = false;
    }

    private void ApplyHoverFromSnapshot(InputSnapshot s)
    {
        if (s.hoverToggleDown && onGround && !isMoving)
            isHoverMode = true;

        if (s.hoverToggleDown && (!onGround) && isHoverMode)
            isHoverMode = false;

        if (isFalling && !isHoverMode && s.hoverToggleDown)
        {
            isHoverMode = true;
            isFalling = false;
        }
    }

    private static string GetDirectionString(Vector2 move)
    {
        if (move.sqrMagnitude < 0.001f) return "None";

        if (Mathf.Abs(move.y) >= Mathf.Abs(move.x))
            return move.y > 0 ? "W" : "S";
        else
            return move.x > 0 ? "D" : "A";
    }
}