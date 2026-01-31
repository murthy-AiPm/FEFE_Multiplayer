using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CombatController : MonoBehaviour
{
    [SerializeField] internal PlayerController playerController;
    public string weaponType;
    internal int attackLevel, attackPoints;

    protected virtual void Update()
    {
        //GlobalCombatVariables.meleeDamage = attackPoints;
        //GlobalCombatVariables.primaryAttack = playerController.inputController.isPrimaryAttack;
    }

}
