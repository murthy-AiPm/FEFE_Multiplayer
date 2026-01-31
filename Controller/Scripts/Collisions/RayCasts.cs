using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RayCasts : MonoBehaviour
{
    public List<RayCastObjects> rayCasts = new List<RayCastObjects>();
    [SerializeField] internal ColliderManager colliderManager;

    [SerializeField] GameObject rayCastOrigin;

    internal bool isJumpPressed;
    public bool isObstacle;

    internal Ray ray;
    internal RaycastHit hit;

    void Start()
    {
      
    }
    protected virtual void Update()
    {

        RayCastDetect();
        isJumpPressed = colliderManager.playerScript.playerContoller.inputController.isJumpPressed;
    }

    private void RayCastDetect()
    {
        
        for (int i = 0; i < rayCasts.Count; i++)
        {
            Vector3 start = new Vector3(transform.position.x + rayCasts[i].transverseHeigth,
                                       transform.position.y + rayCasts[i].verticalHeigth,
                                       transform.position.z + rayCasts[i].horizontalHeigth);

            if (rayCasts[i].RayDirection == RayCastObjects.Directions.forward)
            {
                Debug.DrawRay(start, transform.forward * rayCasts[i].length, Color.yellow);
                ray = new Ray(start, transform.forward);
            }
            else if (rayCasts[i].RayDirection == RayCastObjects.Directions.up)
            {
                Debug.DrawRay(start, transform.up * rayCasts[i].length, Color.yellow);
                ray = new Ray(start, transform.up);
            }
            else if (rayCasts[i].RayDirection == RayCastObjects.Directions.backward)
            {
                Debug.DrawRay(start, -transform.forward * rayCasts[i].length, Color.yellow);
                ray = new Ray(start, -transform.forward);
            }
            else if (rayCasts[i].RayDirection == RayCastObjects.Directions.down)
            {
                Debug.DrawRay(start, -transform.up * rayCasts[i].length, Color.yellow);
                ray = new Ray(start, -transform.up);
            }


            if (Physics.Raycast(ray, out hit, rayCasts[i].length))
            {
                for (int j = 0; j < rayCasts[i].hitTag.Count; j++)
                {
                    ObstacleDetectionLogic(i, j);
                }
            }
        }
    }

    protected virtual void ObstacleDetectionLogic(int i, int j)
    {
        if (hit.transform.tag == rayCasts[i].hitTag[j])
        {
            var target = hit.collider;
            HumanObstacleDetectionLogic(target, i);
         
        }
    }

    protected virtual void HumanObstacleDetectionLogic(Collider target, int i)
    {

    }


}
