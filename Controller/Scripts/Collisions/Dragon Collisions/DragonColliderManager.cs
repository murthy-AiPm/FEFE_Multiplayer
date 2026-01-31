using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DragonColliderManager :ColliderManager
{
    [SerializeField] internal bool dragonObstacleDetect;

    private void OnCollisionEnter(Collision collision)
    {
        GetComponent<DragonController>().airSpeed = GetComponent<DragonController>().airSpeed / 2;

    }

}
