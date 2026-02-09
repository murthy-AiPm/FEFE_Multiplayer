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
    //[SerializeField] MasterScript MS;

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

    // combat
    public bool isPrimaryAttack, isWeaponDischarge;
    public bool isSecondaryAttack;
    public bool isCombatMode = false;
    public bool isSheating;
    public bool fistEquip, swordEquip;

    // NEW: snapshot output
    public InputSnapshot Snapshot { get; private set; }

    // Cached refs
       private HumanoidColliderManger _humanoidCollider;

    // Jump continuity (your old logic preserved)
    [SerializeField] bool previousjump;
    [SerializeField] bool currentjump = true;

    void Awake()
    {
       
        _humanoidCollider = GetComponent<HumanoidColliderManger>(); // may be null (dragon)

    }

    void Update()
    {
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
            // optionally: isAction = false;  // only if action should cancel on landing
        }
        _wasGrounded = onGround;


        // 1) Read input ONCE
        Snapshot = ReadSnapshot();

        // 2) Apply snapshot into existing flags (compat)
        ApplySnapshotToLegacyFlags(Snapshot);

        // 3) Any “derived” state that depends on other components (compat)



        if (Input.GetButton("Action"))
        {
            Debug.Log("action");
        }

    }

    private InputSnapshot ReadSnapshot()
    {
        // Using your current old Input Manager approach
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

        // Simple “direction string” compatibility (your old logic)
        directions = GetDirectionString(s.move);

        // Branch per character
        ApplyHumanFromSnapshot(s);
        //if (MS.charcterIndicator == "human" || MS.charcterIndicator == null)
        //{
        //    ApplyHumanFromSnapshot(s);
        //}
        //else if (MS.charcterIndicator == "dragon")
        //{
        //    ApplyDragonFromSnapshot(s);
        //}
    }

    private void ApplyHumanFromSnapshot(InputSnapshot s)
    {
        // Jump
        //if (s.jumpDown && onGround)
        //{
        //    isJumpPressed = true;
        //    isCrouch = false;
        //}
        //else if (!onGround)
        //{
        //    isJumpPressed = false;
        //}
        // Jump is an EDGE trigger for animations: true only on the frame you pressed jump while grounded.
        isJumpPressed = (s.jumpDown && onGround);

        if (isJumpPressed)
            isCrouch = false;


        // Crouch toggle
        if (s.crouchToggleDown)
            isCrouch = !isCrouch;

        // Hover off for humans
        isHoverMode = false;

        // Combat input (same behavior as your original HumanCombatInput)
        if (isCombatMode && s.primaryHeld)
            isPrimaryAttack = true;
        else
            isPrimaryAttack = false;

        if (s.slot1Down)
        {
            fistEquip = !fistEquip;
            isCombatMode = !isCombatMode;
            swordEquip = false;
            isCrouch = false;
        }

        if (fistEquip || swordEquip)
            isCombatMode = true;

        if (fistEquip && s.slot2Down)
        {
            fistEquip = false;
            isSheating = true;
            isCrouch = false;
            isCombatMode = true;
            swordEquip = true;
        }
        else if (s.slot2Down)
        {
            isCombatMode = !isCombatMode;
            isSheating = !isSheating;
            isCrouch = false;
            swordEquip = !swordEquip;
        }

        if (isCombatMode)
            isCrouch = false;
    }

    private void ApplyDragonFromSnapshot(InputSnapshot s)
    {
        // “Moving” is already set, keep your special jump continuity logic:
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

        // Hover toggle behavior (preserved)
        ApplyHoverFromSnapshot(s);

        // Combat mode logic (same as your DragonCombatInput)
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

        // You had: if moving+grounded+not flying => stop primary attack
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
        // Keep it simple: pick dominant axis like your W/A/S/D logic
        if (move.sqrMagnitude < 0.001f) return "None";

        if (Mathf.Abs(move.y) >= Mathf.Abs(move.x))
            return move.y > 0 ? "W" : "S";
        else
            return move.x > 0 ? "D" : "A";
    }
}
