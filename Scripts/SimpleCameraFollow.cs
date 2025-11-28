using UnityEngine;

public class SimpleCameraFollow : MonoBehaviour
{
    private Transform target;
    public Vector3 offset = new Vector3(0, 3, -5);
    public float smooth = 8f;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPos = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPos, smooth * Time.deltaTime);

        Quaternion desiredRot = Quaternion.LookRotation(target.position - transform.position);
        transform.rotation = Quaternion.Lerp(transform.rotation, desiredRot, smooth * Time.deltaTime);
    }
}
