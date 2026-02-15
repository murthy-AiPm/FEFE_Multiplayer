using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FireBallScript : MonoBehaviour
{
    private Vector3 shootDir;
    private float destorySeconds,fireballSpeed;
    public void Setup(Vector3 shootDir)
    {
        this.shootDir = shootDir;
    }

    public float DestroySeconds(float destorySeconds)
    {
        this.destorySeconds = destorySeconds;
        return this.destorySeconds;
    }

    //public float FireBallSpeed(float fireballSpeed)
    //{
    //    this.destorySeconds = destorySeconds;
    //    return this.destorySeconds;
    //}

    private void Update()
    {
        transform.position += shootDir * Time.deltaTime * 5;
    }
}
