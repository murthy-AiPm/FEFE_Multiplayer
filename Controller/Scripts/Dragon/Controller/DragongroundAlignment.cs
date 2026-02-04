using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Aligns the dragon's body to ground slopes using the 4-paw raycast system.
/// Calculates ground normal from paw hit points and smoothly rotates the dragon
/// to match terrain angle while preserving player's yaw (turning) input.
/// Only active when grounded. Owner-authoritative (NetworkTransform syncs rotation).
/// </summary>
public class DragonGroundAlignment : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonGroundingSystem groundingSystem;
    [SerializeField] private Transform dragonRoot;

    [Header("Alignment Settings")]
    [Tooltip("How fast dragon aligns to ground slope (higher = faster snap)")]
    [SerializeField] private float alignmentSpeed = 8f;
    
    [Tooltip("Maximum slope angle dragon will align to (degrees)")]
    [SerializeField] private float maxSlopeAngle = 45f;
    
    [Tooltip("Minimum slope to trigger alignment (avoids jitter on flat ground)")]
    [SerializeField] private float minSlopeThreshold = 2f;

    [Header("Debug")]
    [SerializeField] private bool showDebugNormals = false;

    // Cached player yaw (preserved during alignment)
    private float targetYaw;

    private void Awake()
    {
        if (groundingSystem == null)
            groundingSystem = GetComponent<DragonGroundingSystem>();

        if (dragonRoot == null)
            dragonRoot = transform;

        targetYaw = dragonRoot.eulerAngles.y;
    }

    private void LateUpdate()
    {
        // Only owner calculates alignment (NetworkTransform syncs rotation)
        if (!IsOwner) return;

        if (groundingSystem == null || dragonRoot == null) return;

        // Only align when properly grounded
        if (!groundingSystem.IsGrounded)
        {
            // When airborne, gradually return to flat orientation
            AlignToUpVector(Vector3.up);
            return;
        }

        // Calculate ground normal from 4-paw hits
        Vector3 groundNormal = CalculateGroundNormal();

        if (groundNormal == Vector3.zero)
        {
            // No valid hits, align to world up
            AlignToUpVector(Vector3.up);
            return;
        }

        // Check if slope is significant enough to align
        float slopeAngle = Vector3.Angle(Vector3.up, groundNormal);
        if (slopeAngle < minSlopeThreshold)
        {
            // Flat ground, align to world up
            AlignToUpVector(Vector3.up);
            return;
        }

        // Clamp extreme slopes
        if (slopeAngle > maxSlopeAngle)
        {
            groundNormal = Vector3.Slerp(Vector3.up, groundNormal, maxSlopeAngle / slopeAngle);
        }

        // Align to calculated ground normal
        AlignToUpVector(groundNormal);

        if (showDebugNormals)
        {
            Debug.DrawRay(dragonRoot.position, groundNormal * 3f, Color.green);
            Debug.DrawRay(dragonRoot.position, dragonRoot.up * 3f, Color.blue);
        }
    }

    /// <summary>
    /// Calculates ground normal from the 4 paw hit points.
    /// Uses cross product of vectors formed by paw positions.
    /// </summary>
    private Vector3 CalculateGroundNormal()
    {
        // Get raycast hit info from grounding system
        var hits = groundingSystem.GetPawHits(); // Need to expose this

        // Count valid hits
        int validHits = 0;
        Vector3 leftHandPos = Vector3.zero;
        Vector3 rightHandPos = Vector3.zero;
        Vector3 leftFootPos = Vector3.zero;
        Vector3 rightFootPos = Vector3.zero;

        if (hits.leftHandHit.collider != null)
        {
            leftHandPos = hits.leftHandHit.point;
            validHits++;
        }
        if (hits.rightHandHit.collider != null)
        {
            rightHandPos = hits.rightHandHit.point;
            validHits++;
        }
        if (hits.leftFootHit.collider != null)
        {
            leftFootPos = hits.leftFootHit.point;
            validHits++;
        }
        if (hits.rightFootHit.collider != null)
        {
            rightFootPos = hits.rightFootHit.point;
            validHits++;
        }

        // Need at least 3 points to calculate a plane
        if (validHits < 3)
            return Vector3.zero;

        // Method 1: Use front paws and one back paw (most stable)
        if (hits.leftHandHit.collider != null && hits.rightHandHit.collider != null)
        {
            // Front vector: left hand to right hand
            Vector3 frontVector = rightHandPos - leftHandPos;

            // Side vector: use whichever back paw is available
            Vector3 sideVector;
            if (hits.leftFootHit.collider != null)
            {
                sideVector = leftFootPos - leftHandPos;
            }
            else if (hits.rightFootHit.collider != null)
            {
                sideVector = rightFootPos - rightHandPos;
            }
            else
            {
                // Only have front paws, use average normal from both
                return ((hits.leftHandHit.normal + hits.rightHandHit.normal) * 0.5f).normalized;
            }

            // Cross product gives perpendicular (up) vector
            Vector3 normal = Vector3.Cross(frontVector, sideVector).normalized;

            // Ensure normal points upward (not downward)
            if (normal.y < 0)
                normal = -normal;

            return normal;
        }

        // Method 2: Fallback - average all hit normals
        Vector3 averageNormal = Vector3.zero;
        if (hits.leftHandHit.collider != null) averageNormal += hits.leftHandHit.normal;
        if (hits.rightHandHit.collider != null) averageNormal += hits.rightHandHit.normal;
        if (hits.leftFootHit.collider != null) averageNormal += hits.leftFootHit.normal;
        if (hits.rightFootHit.collider != null) averageNormal += hits.rightFootHit.normal;

        return (averageNormal / validHits).normalized;
    }

    /// <summary>
    /// Smoothly aligns dragon's up vector to target up vector while preserving yaw.
    /// </summary>
    private void AlignToUpVector(Vector3 targetUp)
    {
        // Store current yaw before alignment
        targetYaw = dragonRoot.eulerAngles.y;

        // Calculate rotation that aligns current up to target up
        Quaternion targetRotation = Quaternion.FromToRotation(dragonRoot.up, targetUp) * dragonRoot.rotation;

        // Preserve yaw by extracting it from target rotation and replacing with player's yaw
        Vector3 targetEuler = targetRotation.eulerAngles;
        targetEuler.y = targetYaw;
        targetRotation = Quaternion.Euler(targetEuler);

        // Smoothly interpolate to target rotation
        dragonRoot.rotation = Quaternion.Slerp(
            dragonRoot.rotation,
            targetRotation,
            alignmentSpeed * Time.deltaTime
        );
    }

    /// <summary>
    /// Called by DragonGroundController when player turns.
    /// Updates target yaw so alignment doesn't fight player input.
    /// </summary>
    public void UpdateTargetYaw(float newYaw)
    {
        targetYaw = newYaw;
    }
}