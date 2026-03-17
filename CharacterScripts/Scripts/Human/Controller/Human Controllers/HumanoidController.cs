using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HumanoidController : ThirdPersonController
{
    //[SerializeField] internal HumanoidColliderManger humanoidCollider;
    [SerializeField] private CombatController combatController;
    [SerializeField] private RuleAnimancerDriver animancerDriver;
    [SerializeField] private VitalManager vitalManager;
    [SerializeField] internal float jumpnum;
    [SerializeField] internal float crouchSpeed;
    [SerializeField] internal float walkSpeed,combatSpeed;
    [SerializeField] internal float stamina, staminaModifier, maxStamina,minStamina;
    internal bool isSecondaryAttack;
    public HumanStats humanStats;
    float oldAngle;
    int layerMask = 1 << 9;
    
    private float _defaultJumpHeight;

    private void OnEnable()
    {
        playerController.inputController.isCombatMode = false;
    }

    protected override void Awake()
    {
        base.Awake();
        _defaultJumpHeight = jumpHeight;
    }
    protected override void Update()
    {
        base.Update();
        CrouchAndSlide();
        SpeedLogic();
        Walk();
        //humanStats.CurrentStamina(stamina);
        //humanStats.SetMaxStamina(maxStamina);
        //humanStats.SetMinStamina(minStamina);
        //Jump();
        //Stamina();
        SprintStaminaDrain();  // ← ADD this instead
        isSecondaryAttack = playerController.inputController.isSecondaryAttack;
        
      
 
    }

    protected override void Jump()
    {
        // Don't jump if combat controller is handling the input as a dodge
        if (combatController != null && (combatController.IsDodging || combatController.IsDodgeStep))
            return;

        // In combat mode, Space is used for dodge — block jumping entirely
        if (playerController.inputController.isCombatMode)
            return;

        if (isJumpPressed && isgrounded && isobstacle)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -jumpnum * gravityValue);
        }
        else if (isJumpPressed && isgrounded && !isobstacle)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -1 * gravityValue);
        }
    }

    protected override void GroundCheck()
    {
        Ray landingRay = new Ray(leftFeet.transform.position, Vector3.down);
        Ray landingRay2 = new Ray(rightFeet.transform.position, Vector3.down);
        Debug.DrawRay(leftFeet.transform.position, Vector3.down * DistToGround);
        Debug.DrawRay(rightFeet.transform.position, Vector3.down * DistToGround);
        isgrounded = Physics.Raycast(landingRay, out hit, DistToGround);

        if (Physics.Raycast(landingRay, out hit, DistToGround,~layerMask) || Physics.Raycast(landingRay2, out hit, DistToGround,~layerMask) || controller.isGrounded)
        {
            isgrounded = true;
        }
        else
        {
            isgrounded = false;
        }
    }
    public virtual void CrouchAndSlide()
    {
        if (GetComponent<InputController>().isCrouch)
        {
            controller.center = new Vector3(0, 0.6f , 0);
            controller.height = 1;
            isobstacle = false;

        }
        else if (isMoving && isModified && playerController.inputController.isAction) // 0.9 minimun clearance
        {
            controller.center = new Vector3(0, 0.2f, 0);
            controller.height = 0.2f;
            controller.radius = 0.2f; //0.3 radius is the least tolerance // min height 1
            transform.Translate(Vector3.forward * num * Time.deltaTime);

            //controller.center = new Vector3(0, 0.28f, 0);
            //controller.height = 0.78f;
        }
        else 
        {
            controller.center = new Vector3(0, 0.9f, 0);
            controller.height = 1.7f;
            controller.radius = 0.4f;
        }

        // center x = 0, y = 0.57, z= 0.15
        //height = 1.20
    }

    protected virtual void SpeedLogic()
    {
        // Root motion takes over movement — zero out speed
        if (animancerDriver != null && animancerDriver.RootMotionActive)
        {
            speed = 0;
            return;
        }

        if (combatController != null)
        {
            if (combatController.IsActionLocked())
            {
                speed = 0;
                return;
            }
            if (combatController.IsSlowMovement())
            {
                speed = combatSpeed;
                return;
            }
        }

        if (playerController.inputController.isCombatMode)
        {
            speed = combatSpeed;
            jumpHeight = 0;
        }
        else
        {
            jumpHeight = _defaultJumpHeight;
            if (playerController.inputController.isCrouch)
                speed = crouchSpeed;
            else
                speed = walkSpeed;
        }
    }

    protected override void Walk()
    {
        CameraCalculations(out float targetAngle, out float angle);
        Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward * speed;

        if (playerController.inputController.isCombatMode)
        {
            if (isMoving)
            {
                // Face camera direction, strafe movement
                transform.rotation = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
                controller.Move(moveDir * speed * speedModifier * Time.deltaTime);
            }
            else if (combatController != null && combatController.IsBlocking)
            {
                // Blocking while standing — face camera to aim guard
                transform.rotation = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
            }
            // Standing still, not blocking: camera free rotates
        }
        else if (isMoving)
        {
            // Normal locomotion — face movement direction
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
            if (!isobstacle)
                controller.Move(moveDir.normalized * speed * speedModifier * Time.deltaTime);
        }
    }

    private void SprintStaminaDrain()
    {
        if (vitalManager == null) return;
        if (isMoving && isModified)
        {
            combatController.SetStaminaRegenPausedExternal(true);
            combatController.ConsumeStaminaExternal(staminaModifier * Time.deltaTime);
        }
        else
        {
            combatController.SetStaminaRegenPausedExternal(false);
        }
    }


}
