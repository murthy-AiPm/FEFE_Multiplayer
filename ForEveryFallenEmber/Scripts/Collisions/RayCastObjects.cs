using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RaycastCard", menuName = "Collisioncards/Raycast Cards")]
public class RayCastObjects : ScriptableObject
{
    public string Name;
    public float verticalHeigth;
    public float horizontalHeigth;
    public float transverseHeigth;
    public float length;
    public string raycastName;
    public List<string> hitTag = new List<string>();
    public Directions RayDirection;
    public TRIGGERS IsTrigger;
    private bool isHit;

    public bool ISTRIGGER 
    {

        get
        {
            if(IsTrigger == TRIGGERS.Yes)
            {
                isHit = true;
            }
            else if (IsTrigger == TRIGGERS.No)
            {
                isHit = false;
            }
            return isHit;

        }
        set
        {
            isHit = value;
        }
    }
    public enum Directions
    {
        up,  forward,backward,down
    }

    public enum TRIGGERS
    {
        Yes,No
    }

}
