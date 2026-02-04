using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class DragonGroundAlignment : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonGroundingSystem groundingSystem;
    [SerializeField] private Transform dragonRoot; // visual root you rotate (often same as rb transform)

    [Header("Alignment Settings")]
    [SerializeField] private float alignmentSpeed = 8f;
    [SerializeField] private float maxSlopeAngle = 45f;
    [SerializeField] private float minSlopeThreshold = 2f;

    [Header("Smoothing")]
    [Tooltip("Higher = less smoothing. 0 = no smoothing.")]
    [SerializeField] private float normalLerpSpeed = 12f;

    [Header("Debug")]
    [SerializeField] private bool showDebugNormals = false;

    private Rigidbody rb;

    // This should be driven by your input/controller (yaw on the ground).
    private float targetYaw;

    // Smoothed normal to reduce jitter
    private Vector3 smoothedUp = Vector3.up;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (groundingSystem == null)
            groundingSystem = GetComponent<DragonGroundingSystem>();

        if (dragonRoot == null)
            dragonRoot = transform;

        targetYaw = dragonRoot.eulerAngles.y;

        // Strongly recommended for visuals
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        if (groundingSystem == null || dragonRoot == null) return;

        Vector3 up = Vector3.up;

        if (groundingSystem.IsGrounded)
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

        // Smooth the up vector to avoid jitter
        if (normalLerpSpeed > 0f)
            smoothedUp = Vector3.Slerp(smoothedUp, up, normalLerpSpeed * Time.fixedDeltaTime);
        else
            smoothedUp = up;

        // Build a rotation that:
        // - uses targetYaw for the "intended heading"
        // - tilts onto the slope using smoothedUp
        Quaternion targetRotation = BuildSlopeRotation(smoothedUp, targetYaw);

        Quaternion newRot = Quaternion.Slerp(rb.rotation, targetRotation, alignmentSpeed * Time.fixedDeltaTime);

        rb.MoveRotation(newRot);

        if (showDebugNormals)
        {
            Debug.DrawRay(dragonRoot.position, smoothedUp * 3f, Color.green);
            Debug.DrawRay(dragonRoot.position, (newRot * Vector3.up) * 3f, Color.blue);
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
            // Fallback if we're on a near-vertical normal (rare)
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

        if (validHits < 2) return Vector3.zero; // 2+ is usually enough for an averaged normal

        Vector3 avg = (sum / validHits).normalized;
        if (avg.y < 0f) avg = -avg;

        return avg;
    }

    /// <summary>
    /// Call this from your ground controller when yaw changes due to input.
    /// IMPORTANT: Do NOT overwrite targetYaw every frame from transform rotation.
    /// </summary>
    public void UpdateTargetYaw(float newYaw)
    {
        targetYaw = newYaw;
    }
}
