using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HumanoidRayCasts : RayCasts
{
    //min vertical height 0.11
    HashSet<string> strings = new HashSet<string>();
    bool isAction,isModified;
    protected override void Update()
    {
        base.Update();
        isAction = colliderManager.playerScript.playerContoller.inputController.isAction;
        isModified = colliderManager.playerScript.playerContoller.inputController.isModified;
    }
    protected override void HumanObstacleDetectionLogic(Collider target, int i)
    {
      
        strings.Add(rayCasts[i].raycastName);
     
        if (rayCasts[i].raycastName == "F1" && isJumpPressed)
        {
            target.isTrigger = true;
            isObstacle = true;
            //Quaternion.LookRotation(Vector3.forward, Vector3.up); //TODO LATER
        }
        if (rayCasts[i].raycastName == "B1")
        {
            target.isTrigger = false; 
            isObstacle = false;
        }
        if (rayCasts[i].raycastName == "B2" || rayCasts[i].raycastName == "B3")
        {            
            isObstacle = false;
        }
        //if ((rayCasts[i].raycastName == "F2" || rayCasts[i].raycastName == "F3") && isAction && isModified)
        //{
        //    isObstacle = true;
        //}
    }
}
