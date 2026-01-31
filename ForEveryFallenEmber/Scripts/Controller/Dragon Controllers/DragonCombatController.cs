using System.Collections;
using System.Collections.Generic;
using UnityEngine;


//TODO PRIMARY ATTACK BOOL BUG NOT SUITABLE FOR LOOPING ANIMATIONS DRAGON BREATHE FIRE
public class DragonCombatController : CombatController
{
    [SerializeField] internal InputController input;
    [SerializeField] internal ParticleSystem fireBreathe;
    [SerializeField] internal GameObject fireBall,parent;
    [SerializeField] internal float fireBallRange, fireBallLife;
    [SerializeField] internal Transform cam;
    Vector3 moveDir;
    void Start()
    {
        weaponType = "BreatheFire";
        fireBreathe.Stop();
    }

    private void OnEnable()
    {
        fireBreathe.Stop();
    }
    protected override void Update()
    {
        base.Update();
        SelectWeapon();
        FireWeapon();
        WeaponDamage();
    }

    private void SelectWeapon()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            weaponType = "BreatheFire";
           
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            weaponType = "FireBall";
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            weaponType = "DragonHandMelee";
        }
    }

    public  void FireWeapon()
    {
        if (weaponType == "BreatheFire" && input.isCombatMode && input.isWeaponDischarge)
        {
            fireBreathe.Play();
        }
        if (input.isWeaponDischarge == false)
        {
            fireBreathe.Stop();         
        }


        if (weaponType == "FireBall" && input.isCombatMode && input.isPrimaryAttack/*Input.GetButtonDown("PrimaryAttack")*/)
        {
            GetComponent<ThirdPersonController>().CameraCalculations(out float targetAngle, out float angle);
            if (input.isSecondaryAttack)
            {
                moveDir = cam.transform.forward.normalized; // depends on camera settings
            }
            else
            {
                moveDir = transform.forward.normalized;            
            }
        }
    }

    private int WeaponDamage()
    {
        if (attackLevel > 1)
        {
            return attackPoints = 2;
        }
        else
        {
   
            return attackPoints = 1;
            
        }
    }
    private void FireBall()
    {
        if (!input.isHoverMode)
        {
            GameObject FireBall = Instantiate(fireBall, parent.transform.position, Quaternion.identity);
            FireBall.GetComponent<FireBallScript>().Setup(moveDir);
            Destroy(FireBall, FireBall.GetComponent<FireBallScript>().DestroySeconds(fireBallLife));
        }

    }
}