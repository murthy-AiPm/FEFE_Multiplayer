using UnityEngine;

/// <summary>
/// Temporary debug script - attach to humanoid to visualize mount detection.
/// Shows detection sphere and prints detailed info to console.
/// </summary>
public class MountDetectionDebug : MonoBehaviour
{
    [SerializeField] private MountController mountController;
    [SerializeField] private float checkRadius = 5f;
    [SerializeField] private LayerMask checkLayer = ~0;

    private void Update()
    {
        if (mountController == null) return;

        // Check what's nearby
        Collider[] hits = Physics.OverlapSphere(transform.position, checkRadius, checkLayer);
        
        //Debug.Log($"=== MOUNT DETECTION DEBUG ===");
        //Debug.Log($"Position: {transform.position}");
        //Debug.Log($"Radius: {checkRadius}");
        //Debug.Log($"Layer Mask: {checkLayer.value}");
        //Debug.Log($"Found {hits.Length} colliders");

        foreach (var hit in hits)
        {
            MountableEntity mount = hit.GetComponent<MountableEntity>();
            Debug.Log($"  - {hit.name} on layer {hit.gameObject.layer}, has MountableEntity: {mount != null}, IsMounted: {mount?.IsMounted}");
        }

        var nearbyMount = mountController.GetNearbyMount();
        Debug.Log($"MountController.nearbyMount: {(nearbyMount != null ? nearbyMount.name : "NULL")}");
        Debug.Log($"MountController.IsMounted: {mountController.IsMounted}");
        Debug.Log($"========================\n");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, checkRadius);
    }
}
