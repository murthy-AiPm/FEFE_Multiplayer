using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class DragonGroundAlignment : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DragonGroundingSystem groundingSystem;
    [SerializeField] private Rigidbody rb;

    [Header("Settings")]
    [SerializeField] private float rotationSpeed = 8f;
    [SerializeField] private float maxSlopeAngle = 45f;
    [SerializeField] private float normalLerpSpeed = 12f;

    private float targetYaw;
    private Vector3 smoothedUp = Vector3.up;

    private void Awake()
    {
        if (groundingSystem == null) groundingSystem = GetComponent<DragonGroundingSystem>();
        if (rb == null) rb = GetComponent<Rigidbody>();

        targetYaw = transform.eulerAngles.y;

        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public void UpdateTargetYaw(float newYaw) => targetYaw = newYaw;

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        if (groundingSystem == null || rb == null) return;

        Vector3 up = Vector3.up;

        if (groundingSystem.IsGrounded)
        {
            Vector3 n = CalculateGroundNormal();
            if (n != Vector3.zero)
            {
                float slopeAngle = Vector3.Angle(Vector3.up, n);

                if (slopeAngle > maxSlopeAngle)
                    n = Vector3.Slerp(Vector3.up, n, maxSlopeAngle / slopeAngle);

                up = n;
            }
        }

        smoothedUp = Vector3.Slerp(smoothedUp, up, normalLerpSpeed * Time.fixedDeltaTime);

        Quaternion targetRot = BuildSlopeRotation(smoothedUp, targetYaw);
        Quaternion newRot = Quaternion.Slerp(rb.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);

        rb.MoveRotation(newRot);
    }

    private Quaternion BuildSlopeRotation(Vector3 up, float yawDegrees)
    {
        Vector3 yawForward = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;
        Vector3 forwardOnPlane = Vector3.ProjectOnPlane(yawForward, up);

        if (forwardOnPlane.sqrMagnitude < 0.0001f)
            forwardOnPlane = Vector3.ProjectOnPlane(transform.forward, up);

        forwardOnPlane.Normalize();
        return Quaternion.LookRotation(forwardOnPlane, up);
    }

    private Vector3 CalculateGroundNormal()
    {
        var hits = groundingSystem.GetPawHits();
        int count = 0;
        Vector3 sum = Vector3.zero;

        if (hits.leftHandHit.collider != null) { sum += hits.leftHandHit.normal; count++; }
        if (hits.rightHandHit.collider != null) { sum += hits.rightHandHit.normal; count++; }
        if (hits.leftFootHit.collider != null) { sum += hits.leftFootHit.normal; count++; }
        if (hits.rightFootHit.collider != null) { sum += hits.rightFootHit.normal; count++; }

        if (count < 2) return Vector3.zero;

        Vector3 avg = (sum / count).normalized;
        if (avg.y < 0f) avg = -avg;
        return avg;
    }
}
