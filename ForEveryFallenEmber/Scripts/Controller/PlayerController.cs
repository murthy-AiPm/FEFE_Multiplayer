using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [SerializeField] PlayerScript playerScript;

    [SerializeField] internal ThirdPersonController TPS;

    [SerializeField] internal InputController inputController;

    [SerializeField] internal CombatController combatController;

   // [SerializeField] internal PlayerAnimator playerAnimator;
    void Start()
    {
        
    }


    void Update()
    {
        // if(playerScript.Dragon.activeInHierarchy == false)
        // {
        //     playerScript.Dragon.transform.position = playerScript.Humanoid.transform.position;
        // }
        // else if(playerScript.Humanoid.activeInHierarchy == false)
        // {
        //     playerScript.Humanoid.transform.position = playerScript.Dragon.transform.position; 
        // }
        
        
    }
}
