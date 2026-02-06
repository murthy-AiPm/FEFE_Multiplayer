using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dragon ground alignment - SINGLE AUTHORITY for rotation when grounded.
/// 
/// KEY CHANGES:
/// - Now the ONLY script that calls rb.MoveRotation() when grounded
/// - Added height adjustment (ground snapping) to prevent paw sinking
/// - Smoother slope transitions
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DragonGroundAlignment : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonGroundingSystem groundingSystem;
    [SerializeField] private Transform dragonRoot;

    [Header("Alignment Settings")]
    [SerializeField] private float alignmentSpeed = 8f;
    [SerializeField] private float maxSlopeAngle = 45f;
    [SerializeField] private float minSlopeThreshold = 2f;

    [Header("Height Adjustment (Ground Snapping)")]
    [Tooltip("Target height of the body above average paw ground points")]
    [SerializeField] private float targetBodyHeight = 1.2f;
    [Tooltip("How fast to adjust height")]
    [SerializeField] private float heightAdjustSpeed = 10f;
    [Tooltip("Max height adjustment per frame (prevents teleporting)")]
    [SerializeField] private float maxHeightAdjustPerFrame = 0.15f;
    [Tooltip("Only adjust height when grounded")]
    [SerializeField] private bool onlyAdjustWhenGrounded = true;

    [Header("Smoothing")]
    [Tooltip("Higher = less smoothing. 0 = no smoothing.")]
    [SerializeField] private float normalLerpSpeed = 12f;
    [Tooltip("How fast yaw catches up to target")]
    [SerializeField] private float yawSmoothSpeed = 10f;

    [Header("Debug")]
    [SerializeField] private bool showDebugNormals = false;
    [SerializeField] private bool showDebugHeight = false;

    private Rigidbody rb;
    private float targetYaw;
    private float currentYaw;
    private Vector3 smoothedUp = Vector3.up;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (groundingSystem == null)
            groundingSystem = GetComponent<DragonGroundingSystem>();

        if (dragonRoot == null)
            dragonRoot = transform;

        targetYaw = dragonRoot.eulerAngles.y;
        currentYaw = targetYaw;

        //rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        if (groundingSystem == null || dragonRoot == null) return;

        bool isGrounded = groundingSystem.IsGrounded;

        // ═══════════════════════════════════════════════════════════════
        // HEIGHT ADJUSTMENT (Ground Snapping)
        // ═══════════════════════════════════════════════════════════════
        if (isGrounded || !onlyAdjustWhenGrounded)
        {
            AdjustHeight();
        }

        // ═══════════════════════════════════════════════════════════════
        // ROTATION (Slope Alignment + Yaw)
        // ═══════════════════════════════════════════════════════════════
        Vector3 up = Vector3.up;

        if (isGrounded)
        {
            Vector3 groundNormal = CalculateGroundNormal();
            if (groundNormal != Vector3.zero)
            {
                float slopeAngle = Vector3.Angle(Vector3.up, groundNormal);

                if (slopeAngle >= minSlopeThreshold)
                {
                    if (slopeAngle > maxSlopeAngle)
                        groundNormal = Vector3.Slerp(Vector3.up, groundNormal, maxSlopeAngle / slopeAngle);

                    up = groundNormal;
                }
            }
        }

        // Smooth the up vector
        if (normalLerpSpeed > 0f)
            smoothedUp = Vector3.Slerp(smoothedUp, up, normalLerpSpeed * Time.fixedDeltaTime);
        else
            smoothedUp = up;

        // Smooth yaw towards target
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, yawSmoothSpeed * Time.fixedDeltaTime);

        // Build final rotation
        Quaternion targetRotation = BuildSlopeRotation(smoothedUp, currentYaw);
        Quaternion newRot = Quaternion.Slerp(rb.rotation, targetRotation, alignmentSpeed * Time.fixedDeltaTime);

        rb.MoveRotation(newRot);

        // Debug visualization
        if (showDebugNormals)
        {
            Debug.DrawRay(dragonRoot.position, smoothedUp * 3f, Color.green);
            Debug.DrawRay(dragonRoot.position, (newRot * Vector3.up) * 3f, Color.blue);
        }
    }

    private void AdjustHeight()
    {
        var hits = groundingSystem.GetPawHits();

        // Calculate average ground point from valid paw hits
        Vector3 sumGroundPoints = Vector3.zero;
        int validCount = 0;

        if (hits.leftHandHit.collider != null)
        {
            sumGroundPoints += hits.leftHandHit.point;
            validCount++;
        }
        if (hits.rightHandHit.collider != null)
        {
            sumGroundPoints += hits.rightHandHit.point;
            validCount++;
        }
        if (hits.leftFootHit.collider != null)
        {
            sumGroundPoints += hits.leftFootHit.point;
            validCount++;
        }
        if (hits.rightFootHit.collider != null)
        {
            sumGroundPoints += hits.rightFootHit.point;
            validCount++;
        }

        if (validCount < 2) return; // Need at least 2 points for reliable height

        Vector3 avgGroundPoint = sumGroundPoints / validCount;

        // Target position: body should be targetBodyHeight above average ground
        float targetY = avgGroundPoint.y + targetBodyHeight;
        float currentY = rb.position.y;
        float heightDiff = targetY - currentY;

        // Clamp adjustment to prevent teleporting
        float adjustment = Mathf.Clamp(heightDiff, -maxHeightAdjustPerFrame, maxHeightAdjustPerFrame);

        // Smooth interpolation
        adjustment = Mathf.Lerp(0f, adjustment, heightAdjustSpeed * Time.fixedDeltaTime);

        if (Mathf.Abs(adjustment) > 0.001f)
        {
            Vector3 newPos = rb.position;
            newPos.y += adjustment;
            rb.MovePosition(newPos);
        }

        if (showDebugHeight)
        {
            Debug.DrawLine(avgGroundPoint, avgGroundPoint + Vector3.up * targetBodyHeight, Color.yellow);
            Debug.DrawLine(rb.position, rb.position + Vector3.down * 0.5f, heightDiff > 0 ? Color.red : Color.cyan);
        }
    }

    private Quaternion BuildSlopeRotation(Vector3 up, float yawDegrees)
    {
        // Yaw forward in world space
        Vector3 yawForward = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;

        // Project onto slope plane so forward is tangent to the ground
        Vector3 forwardOnPlane = Vector3.ProjectOnPlane(yawForward, up);
        if (forwardOnPlane.sqrMagnitude < 0.0001f)
        {
            forwardOnPlane = Vector3.ProjectOnPlane(dragonRoot.forward, up);
        }

        forwardOnPlane.Normalize();

        return Quaternion.LookRotation(forwardOnPlane, up);
    }

    private Vector3 CalculateGroundNormal()
    {
        var hits = groundingSystem.GetPawHits();

        int validHits = 0;
        Vector3 sum = Vector3.zero;

        if (hits.leftHandHit.collider != null) { sum += hits.leftHandHit.normal; validHits++; }
        if (hits.rightHandHit.collider != null) { sum += hits.rightHandHit.normal; validHits++; }
        if (hits.leftFootHit.collider != null) { sum += hits.leftFootHit.normal; validHits++; }
        if (hits.rightFootHit.collider != null) { sum += hits.rightFootHit.normal; validHits++; }

        if (validHits < 2) return Vector3.zero;

        Vector3 avg = (sum / validHits).normalized;
        if (avg.y < 0f) avg = -avg;

        return avg;
    }

    /// <summary>
    /// Call this from DragonGroundController when input changes yaw.
    /// This is the ONLY way yaw should be updated during grounded movement.
    /// </summary>
    public void UpdateTargetYaw(float newYaw)
    {
        targetYaw = newYaw;
    }

    /// <summary>
    /// Force-set yaw without smoothing (use when landing or teleporting).
    /// </summary>
    public void SetYawImmediate(float newYaw)
    {
        targetYaw = newYaw;
        currentYaw = newYaw;
    }
}