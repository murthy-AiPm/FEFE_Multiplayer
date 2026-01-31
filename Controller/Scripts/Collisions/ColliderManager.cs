using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ColliderManager : MonoBehaviour
{
    [SerializeField] internal RayCasts rayCasts;
    [SerializeField] internal PlayerScript playerScript;


    public float forceApplied = 50;

    public int health = 100;

    //private void OnTriggerEnter(Collider other)
    //{
    //    if(other.tag == "House")
    //    {
    //    //    controller.Move(Vector3.forward * 0);
    //        Debug.Log("colliding");
    //    }
   
   
    //}

    //private void OnTriggerEnter(Collider other)
    //{
    //    if (other.tag == "Rbody")
    //    {
    //        Debug.Log("hitplayer");
    //    }

    //}
    //void OnControllerColliderHit(ControllerColliderHit col)
    //{

    //    if (col.gameObject.tag == "Rbody")
    //    {
    //        Damage();
    //        Bouncy(col);
    //    }
    //}

    //private void Damage()
    //{
    //    health -= 5;
    //}

    //private void Bouncy(ControllerColliderHit col)
    //{
    //    if (col.gameObject.GetComponent<Rigidbody>()== null)
    //    {
    //        return;
    //    }
    //    else
    //    {
    //        //col.gameObject.GetComponent<Rigidbody>().AddForce(controller.transform.forward * forceApplied);
    //        col.gameObject.GetComponent<Rigidbody>().AddForce(0, 0, 0);
    //    }

    //}
}
