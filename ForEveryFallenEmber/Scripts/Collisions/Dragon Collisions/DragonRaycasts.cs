using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DragonRaycasts : RayCasts
{
    DragonColliderManager dragonCollider;
    private void Awake()
    {
        dragonCollider = GetComponent<DragonColliderManager>();
    }
    protected override void Update()
    {
        base.Update();  
        

    }


    protected override void ObstacleDetectionLogic(int i, int j)
    {
        if (hit.transform.gameObject.layer == LayerMask.NameToLayer("Terrain"))
        {
            dragonCollider.dragonObstacleDetect = true;
        }
        else
        {
            dragonCollider.dragonObstacleDetect = false;
        }
    }
}
