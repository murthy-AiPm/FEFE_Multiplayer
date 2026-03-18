using UnityEngine;

/// <summary>
/// Single source of truth for dragon/mount grounding state.
/// Uses 4 paw raycasts (Left Hand, Right Hand, Left Foot, Right Foot).
/// Grounded = 3/4 paws hit OR both front paws (hands) hit.
/// IsFalling = both front paws off ground while not in flight.
/// 
/// IMPROVED: Added raycastOriginOffset to start raycasts higher (better for horses)
/// </summary>
public class AnimalGroundingSystem : MonoBehaviour
{
    /// <summary>
    /// Contains all 4 paw raycast hits. Used by DragonGroundAlignment for slope calculation.
    /// </summary>
    public struct PawHitInfo
    {
        public RaycastHit leftHandHit;
        public RaycastHit rightHandHit;
        public RaycastHit leftFootHit;
        public RaycastHit rightFootHit;
    }

    [Header("Paw Transforms")]
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform leftFoot;
    [SerializeField] private Transform rightFoot;

    [Header("Debug (Read Only)")]
    [SerializeField] private bool _debugIsGrounded;
    [SerializeField] private bool _debugIsFalling;
    [SerializeField] private bool _debugIsOnCliff;
    [SerializeField] private bool _debugFrontPawsGrounded;
    [SerializeField] private bool _debugIsLanding;

    [Header("Raycast Settings")]
    [SerializeField] private float raycastDistance = 1.5f;
    [Tooltip("Start raycast this high above paw transform (prevents hitting own collider)")]
    [SerializeField] private float raycastOriginOffset = 0.2f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundedConfirmTime = 0.08f;

    [Header("Cliff Detection")]
    [SerializeField] private float cliffThreshold = 3.0f; // If ground below paws is > this, it's a cliff

    // Grounding state
    private bool leftHandGrounded;
    private bool rightHandGrounded;
    private bool leftFootGrounded;
    private bool rightFootGrounded;

    // Cached raycast hits (for ground alignment)
    private RaycastHit leftHandHit;
    private RaycastHit rightHandHit;
    private RaycastHit leftFootHit;
    private RaycastHit rightFootHit;

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
    public bool IsLanding => leftHandGrounded && rightHandGrounded && !leftFootGrounded && !rightFootGrounded;
    public bool IsOnCliff => leftFootGrounded && rightFootGrounded && !leftHandGrounded && !rightHandGrounded;
    public bool FrontPawsGrounded => leftHandGrounded && rightHandGrounded;

    private void Update()
    {
        RaycastPaw(leftHand, ref leftHandGrounded, ref leftHandHit);
        RaycastPaw(rightHand, ref rightHandGrounded, ref rightHandHit);
        RaycastPaw(leftFoot, ref leftFootGrounded, ref leftFootHit);
        RaycastPaw(rightFoot, ref rightFootGrounded, ref rightFootHit);

        UpdateGroundedState();
        UpdateFallingState();
    }

    private void RaycastPaw(Transform paw, ref bool isHit, ref RaycastHit hit)
    {
        if (paw == null)
        {
            isHit = false;
            hit = default;
            return;
        }

        // Start raycast slightly above paw transform to avoid hitting own collider
        Vector3 rayStart = paw.position + Vector3.up * raycastOriginOffset;
        float totalDistance = raycastDistance + raycastOriginOffset;

        bool didHit = Physics.Raycast(rayStart, Vector3.down, out RaycastHit rh,
            totalDistance, groundMask, QueryTriggerInteraction.Ignore);

        // Ignore self-hit (dragon's/horse's own colliders)
        if (didHit && rh.transform != null && rh.transform.root == transform.root)
            didHit = false;

        isHit = didHit;
        hit = didHit ? rh : default;

        _debugIsGrounded = _isGrounded;
        _debugIsFalling = _isFalling;
        _debugIsOnCliff = IsOnCliff;
        _debugIsLanding = IsLanding;
        _debugFrontPawsGrounded = FrontPawsGrounded;
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
        if (_isGrounded || IsOnCliff)
        {
            _isFalling = false;
        }
        else
        {
            _isFalling = true;
        }

        // IsOnCliff is now a pure property — no state needed here
        // Update debug mirror
        _debugIsOnCliff = IsOnCliff;
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

    /// <summary>
    /// Returns all 4 paw raycast hits for ground alignment calculations.
    /// </summary>
    public PawHitInfo GetPawHits()
    {
        return new PawHitInfo
        {
            leftHandHit = leftHandHit,
            rightHandHit = rightHandHit,
            leftFootHit = leftFootHit,
            rightFootHit = rightFootHit
        };
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

        Vector3 rayStart = paw.position + Vector3.up * raycastOriginOffset;
        float totalDistance = raycastDistance + raycastOriginOffset;

        Gizmos.color = isHit ? Color.green : Color.red;
        Gizmos.DrawLine(rayStart, rayStart + Vector3.down * totalDistance);
        Gizmos.DrawSphere(rayStart, 0.1f);
    }
}