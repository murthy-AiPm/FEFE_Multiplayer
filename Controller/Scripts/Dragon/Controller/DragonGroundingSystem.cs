using UnityEngine;

/// <summary>
/// Single source of truth for dragon grounding state.
/// Uses 4 paw raycasts (Left Hand, Right Hand, Left Foot, Right Foot).
/// Grounded = 3/4 paws hit OR both front paws (hands) hit.
/// IsFalling = both front paws off ground while not in flight.
/// </summary>
public class DragonGroundingSystem : MonoBehaviour
{
    [Header("Paw Transforms")]
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform leftFoot;
    [SerializeField] private Transform rightFoot;

    [Header("Raycast Settings")]
    [SerializeField] private float raycastDistance = 1.5f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundedConfirmTime = 0.08f;

    [Header("Cliff Detection")]
    [SerializeField] private float cliffThreshold = 3.0f; // If ground below paws is > this, it's a cliff

    // Grounding state
    private bool leftHandGrounded;
    private bool rightHandGrounded;
    private bool leftFootGrounded;
    private bool rightFootGrounded;

    // Debounce timers
    private float groundedTimer;
    private float fallingTimer;

    // Cached state
    private bool _isGrounded;
    private bool _isFalling;
    private bool _isOnCliff;

    // Public read-only state
    public bool IsGrounded => _isGrounded;
    public bool IsFalling => _isFalling;
    public bool IsOnCliff => _isOnCliff;
    public bool FrontPawsGrounded => leftHandGrounded && rightHandGrounded;

    private void Update()
    {
        RaycastPaw(leftHand, ref leftHandGrounded);
        RaycastPaw(rightHand, ref rightHandGrounded);
        RaycastPaw(leftFoot, ref leftFootGrounded);
        RaycastPaw(rightFoot, ref rightFootGrounded);

        UpdateGroundedState();
        UpdateFallingState();
    }

    private void RaycastPaw(Transform paw, ref bool isHit)
    {
        if (paw == null)
        {
            isHit = false;
            return;
        }

        bool hit = Physics.Raycast(paw.position, Vector3.down, out RaycastHit rh,
            raycastDistance, groundMask, QueryTriggerInteraction.Ignore);

        // Ignore self-hit (dragon's own colliders)
        if (hit && rh.transform != null && rh.transform.root == transform.root)
            hit = false;

        isHit = hit;
    }

    private void UpdateGroundedState()
    {
        int groundedCount = 0;
        if (leftHandGrounded) groundedCount++;
        if (rightHandGrounded) groundedCount++;
        if (leftFootGrounded) groundedCount++;
        if (rightFootGrounded) groundedCount++;

        // Grounded = 3/4 paws OR both front paws
        bool shouldBeGrounded = groundedCount >= 3 || FrontPawsGrounded;

        if (shouldBeGrounded)
        {
            groundedTimer += Time.deltaTime;
            if (groundedTimer >= groundedConfirmTime)
                _isGrounded = true;
        }
        else
        {
            groundedTimer = 0f;
            _isGrounded = false;
        }
    }

    private void UpdateFallingState()
    {
        // Falling = both front paws off ground AND currently grounded (walked off edge)
        // Once falling, stays falling until grounded again
        if (_isGrounded)
        {
            _isFalling = false;
            _isOnCliff = !FrontPawsGrounded;
        }
        else if (_isOnCliff && !_isFalling)
        {
            // Transitioned from cliff edge to actual fall
            _isFalling = true;
        }
    }

    /// <summary>
    /// Call this from FlightController or GroundController when dragon enters flight/glide.
    /// Resets falling state so it doesn't conflict with flight.
    /// </summary>
    public void ResetFallingState()
    {
        _isFalling = false;
        _isOnCliff = false;
    }

    // Debug visualization
    private void OnDrawGizmos()
    {
        DrawPawRay(leftHand, leftHandGrounded);
        DrawPawRay(rightHand, rightHandGrounded);
        DrawPawRay(leftFoot, leftFootGrounded);
        DrawPawRay(rightFoot, rightFootGrounded);
    }

    private void DrawPawRay(Transform paw, bool isHit)
    {
        if (paw == null) return;
        Gizmos.color = isHit ? Color.green : Color.red;
        Gizmos.DrawLine(paw.position, paw.position + Vector3.down * raycastDistance);
        Gizmos.DrawSphere(paw.position, 0.1f);
    }
}
