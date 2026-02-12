using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HumanoidController : ThirdPersonController
{
    //[SerializeField] internal HumanoidColliderManger humanoidCollider;
    [SerializeField] internal float jumpnum;
    [SerializeField] internal float crouchSpeed;
    [SerializeField] internal float walkSpeed,combatSpeed;
    [SerializeField] internal float stamina, staminaModifier, maxStamina,minStamina;
    internal bool isSecondaryAttack;
    public HumanStats humanStats;
    float oldAngle;
    int layerMask = 1 << 9;
    
    private void OnEnable()
    {
        playerController.inputController.isCombatMode = false;
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
        Stamina();
        isSecondaryAttack = playerController.inputController.isSecondaryAttack;
        
      
 
    }

    protected override void Jump()
    {
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
        if (GetComponent<InputController>().isPrimaryAttack)
        {
            speed = 0;
        }
        else if (GetComponent<InputController>().isCombatMode)
        {
            speed = combatSpeed;
        }
        else if (GetComponent<InputController>().isCrouch)
        {
            speed = crouchSpeed;
        }
        else
        {
            speed = walkSpeed;
        }
    }

    protected override void Walk() // includes combat walk
    {
        CameraCalculations(out float targetAngle, out float angle);
        Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward * speed;
        if (GetComponent<InputController>().isCombatMode)
        {          
            if (isSecondaryAttack)
            {
                transform.rotation = Quaternion.Euler(transform.rotation.x, cam.eulerAngles.y, transform.rotation.z);
                if (isMoving)
                {                    
                    if (GetComponent<InputController>().isPrimaryAttack)
                    {
                        transform.rotation = Quaternion.Euler(transform.rotation.x, cam.eulerAngles.y, transform.rotation.z);
                        controller.Move(moveDir.normalized * 0 * speedModifier * Time.deltaTime);
                    }
                    else
                    {
                        controller.Move(moveDir * speed * speedModifier * Time.deltaTime);
                    }
                }
            }
            else if(isMoving)
            {
                transform.rotation = Quaternion.Euler(transform.rotation.x, angle, transform.rotation.z);
                if (GetComponent<InputController>().isPrimaryAttack)
                {                  
                    controller.Move(moveDir.normalized * 0 * speedModifier * Time.deltaTime);
                }
                else
                {
                    controller.Move(moveDir * speed * speedModifier * Time.deltaTime);
                }
            }
        }
        else if (isMoving)
        {
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
            oldAngle = angle;
            if (!isobstacle)
            {
                controller.Move(moveDir.normalized * speed * speedModifier * Time.deltaTime);
            }
            else if (isobstacle)
            {
                controller.Move(moveDir.normalized * 0 * speedModifier * Time.deltaTime);
            }
        }
        else /*if(!isClimbing)*/
        {
            //isgrounded = true;
            gravityValue = -9.81f;
        }
    }

    private void Stamina()
    {
        if(isMoving && isModified)
        {
            stamina = stamina - staminaModifier;
        }
        else
        {
            stamina = stamina + staminaModifier;
        }

        if(stamina >= maxStamina)
        {
            stamina = maxStamina;
        }
        else if (stamina <=minStamina)
        {
            stamina = minStamina;
        }
    }

}
