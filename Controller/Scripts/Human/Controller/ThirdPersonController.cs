using System;
using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

public class ThirdPersonController : MonoBehaviour
{
    [SerializeField] internal PlayerController playerController;
      [SerializeField] internal CharacterController controller;
    [SerializeField] internal Transform cam;
    [SerializeField] internal float jumpHeight;
    [SerializeField] internal float DistToGround;
    [SerializeField] internal float gravityValue;
    [SerializeField] internal float speed;
    [SerializeField] internal float turnSmootheTime;
    [SerializeField] internal float speedModifier, maxSpeedModifier, minSpeedModifer;
    [SerializeField] internal GameObject leftFeet, rightFeet, obstacleRayCast;

    //public CinemachineFreeLook vcam;
    public Vector3 playerVelocity;
    public bool isgrounded;
    public bool isfreeFall;
   [SerializeField] internal bool isobstacle;
    public float floatfreefall;
    public float num;  // raycast vertical movement
    public int key;

    internal float turnSmoothVelocity;
    internal float flySmoothVelocity;

    internal float targetAngle, angle;

    internal RaycastHit hit,hit1,hit2,hit3,hit4;


    internal bool isMoving;
    internal bool isModified;
    internal bool isJumpPressed;
    internal bool isHoverMode;

    // Jump grace so animation/locomotion doesn't lag behind grounding.
    [SerializeField] private float jumpGraceTime = 0.12f;
    private float _jumpGraceUntil;


    //private bool onGround;

    protected InputSnapshot input;
    public enum LocomotionState
    {
        Grounded,
        Jumping,
        Falling,
        Hovering,
        Flying,
        Climbing
    }

    public LocomotionState Locomotion { get; protected set; }



    protected virtual void Awake()
    {
        cam = Camera.main.transform;
        // Cache CharacterController once
        if (controller == null )
            controller = GetComponentInChildren<CharacterController>();

        if (controller == null)
            controller = GetComponentInChildren<CharacterController>();
    }
    protected virtual void Start()
    {
       
    }

    protected virtual void Update()
    {
        // cache snapshot once per frame
        input = playerController.inputController.Snapshot;

        // OPTIONAL: keep these legacy flags if other code depends on them
        isMoving = input.movePressed;
        isModified = input.modifiedHeld;
        isJumpPressed = input.jumpDown;   // edge
        isHoverMode = playerController.inputController.isHoverMode; // derived/toggled state stays in InputController for now

        if (!isfreeFall)
        {
            Walk();
            Jump();
        }

        SpeedModifier();
        GroundCheck();
        FreeFall();
        UpdateLocomotionState();
        Gravity();
    }

    protected virtual void UpdateLocomotionState()
    {
        // 🔑 JUMP GRACE — MUST BE FIRST
        if (Time.time < _jumpGraceUntil)
        {
            Locomotion = LocomotionState.Jumping;
            return;
        }
        // Default ordering: explicit overrides first
        if (playerController != null && playerController.inputController != null)
        {
            // if you still keep climbing in InputController
            if (playerController.inputController.isClimbing)
            {
                Locomotion = LocomotionState.Climbing;
                return;
            }

            if (playerController.inputController.isHoverMode)
            {
                Locomotion = LocomotionState.Hovering;
                return;
            }
        }

        if (isfreeFall && !isgrounded)
        {
            Locomotion = LocomotionState.Falling;
            return;
        }

        if (!isgrounded && playerVelocity.y > 0.1f)
        {
            Locomotion = LocomotionState.Jumping;
            return;
        }

        Locomotion = LocomotionState.Grounded;
    }

    protected virtual void Gravity()
    {
        playerVelocity.y += gravityValue * Time.deltaTime;
        controller.Move(playerVelocity * Time.deltaTime);
    }

    protected virtual void FreeFall()
    {
        if (playerVelocity.y < -floatfreefall && !isgrounded)
        {
            playerVelocity.y -= -1 * Time.deltaTime;
            isfreeFall = true;
        }
        if (playerVelocity.y < 0 && isgrounded)
        {
            playerVelocity.y = -floatfreefall;
            isfreeFall = false; /// responsible for landing animation
        }
        else if (playerVelocity.y>=0 && isfreeFall)
        {
            isfreeFall = false;
        }
    }

    protected virtual void Jump()
    {

        if (isJumpPressed && isgrounded && isobstacle)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight *- 1 * gravityValue);

        }
        else if (isJumpPressed && isgrounded && !isobstacle)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -1 * gravityValue);
            _jumpGraceUntil = Time.time + jumpGraceTime;
            isgrounded = false;

        }
    }

    protected virtual void Walk()
    {
        CameraCalculations(out float targetAngle, out float angle);
        if (isMoving )
        {
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward * speed;
            //transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
           // vcam.transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            if (!isobstacle)
            {
                controller.Move(moveDir.normalized * speed * speedModifier * Time.deltaTime);
            }
            else if (isobstacle)
            {
                controller.Move(moveDir.normalized * 0 * speedModifier * Time.deltaTime);
            }
        }
    }
    protected virtual void SpeedModifier()
    {
        if (isModified)
        {
            speedModifier = maxSpeedModifier;
        }
        else
        {
            speedModifier = minSpeedModifer;
        }
    }

    protected virtual void GroundCheck()
    {

        Ray landingRay = new Ray(leftFeet.transform.position, Vector3.down);
        Ray landingRay2 = new Ray(rightFeet.transform.position, Vector3.down);
        Debug.DrawRay(leftFeet.transform.position, Vector3.down * DistToGround);
        Debug.DrawRay(rightFeet.transform.position, Vector3.down * DistToGround);
        isgrounded = Physics.Raycast(landingRay, out hit, DistToGround);

        if (Physics.Raycast(landingRay, out hit, DistToGround) || Physics.Raycast(landingRay2, out hit, DistToGround) || controller.isGrounded)
        {
            isgrounded = true;
        }
        else
        {
            isgrounded = false;
        }
    }

    public virtual void CameraCalculations(out float targetAngle, out float angle)
    {
        // Use snapshot instead of Input.GetAxisRaw
        Vector3 direction = new Vector3(input.move.x, 0f, input.move.y).normalized;

        targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
        angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmootheTime);
    }

    /////////////////

    //
}
