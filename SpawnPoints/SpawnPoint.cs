using UnityEngine;

/// <summary>
/// Marks a location as a valid player spawn point.
/// Place this on empty GameObjects in your scene to define spawn locations.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [SerializeField] private bool isOccupied = false;

    // Optional: Visual debugging in editor
    [SerializeField] private Color gizmoColor = Color.green;
    [SerializeField] private float gizmoRadius = 0.5f;

    public bool IsOccupied => isOccupied;
    public Vector3 Position => transform.position;
    public Quaternion Rotation => transform.rotation;

    /// <summary>
    /// Mark this spawn point as occupied by a player
    /// </summary>
    public void SetOccupied(bool occupied)
    {
        isOccupied = occupied;
    }

    private void OnDrawGizmos()
    {
        // Draw a sphere at spawn point for easy visualization
        Gizmos.color = isOccupied ? Color.red : gizmoColor;
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);

        // Draw forward direction arrow
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, transform.forward * 1f);
    }

    private void OnDrawGizmosSelected()
    {
        // Draw a larger indicator when selected
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, gizmoRadius * 1.5f);
    }
}