using UnityEngine;

/// <summary>
/// Dragon-specific ground controller. Inherits all movement, gait, jump, and takeoff
/// logic from AnimalGroundController and adds the flight controller handoff on takeoff.
/// </summary>
public class DragonGroundController : AnimalGroundController
{
    [Header("Dragon Flight")]
    [SerializeField] private DragonFlightController flightController;

    [Header("Dragon Fake Gravity")]
    [SerializeField] private float dragonFakeGravity = 20f;
    [SerializeField] private float dragonMaxFallSpeed = 40f;

    [Header("Ground Detection (Fake Gravity)")]
    [Tooltip("Raycast origin for detecting ground below. Set to a bone or empty transform on the dragon.")]
    [SerializeField] private Transform groundCheckOrigin;
    [Tooltip("How far down to raycast. Must exceed peak jump height but stay short enough to allow cliff free-fall.")]
    [SerializeField] private float groundCheckDistance = 8f;
    [Tooltip("Layers that count as solid ground for the gravity check.")]
    [SerializeField] private LayerMask groundCheckMask = ~0;

    private float _jumpGaitSpeed;
    private float _dragonFallVelocity;
    private bool  _isSwimming;

    protected override void Awake()
    {
        base.Awake();

        if (flightController == null)
            flightController = GetComponentInParent<DragonFlightController>();
    }

    /// <summary>
    /// Called by AnimalGroundController when the takeoff animation completes.
    /// Hands off control to the dragon flight controller.
    /// </summary>
    protected override void OnJumpTriggered()
    {
        _jumpGaitSpeed = GaitSpeed;
    }

    protected override void OnGroundUpdate()
    {
        // Hold GaitSpeed during jump so blend tree shows correct jump clip.
        if (IsPlayingJump)
            GaitSpeed = _jumpGaitSpeed;
    }

    protected override void OnFixedGroundUpdate()
    {
        if (flightController == null || rb == null || groundingSystem == null) return;

        bool inFlight = flightController.IsFlying ||
                        flightController.IsHoverMode ||
                        flightController.IsGliding ||
                        flightController.IsDiving;

        bool groundExistsBelow = GroundExistsBelow();
        if (!inFlight && !groundingSystem.IsGrounded && !_isSwimming && !groundExistsBelow)
        {
            _dragonFallVelocity += dragonFakeGravity * Time.fixedDeltaTime;
            _dragonFallVelocity  = Mathf.Min(_dragonFallVelocity, dragonMaxFallSpeed);
            rb.MovePosition(rb.position + Vector3.down * _dragonFallVelocity * Time.fixedDeltaTime);
        }
        else
        {
            _dragonFallVelocity = 0f;
        }
    }

    protected override void OnLanded()
    {
        // ClearState zeroed GaitSpeed — restore it so MoveTowards can decay gradually.
        GaitSpeed = _jumpGaitSpeed;
    }

    public void SetSwimming(bool swimming) => _isSwimming = swimming;

    protected override bool CanMoveWhileInactive() => _isSwimming;

    /// <summary>
    /// Single raycast straight down from groundCheckOrigin.
    /// Returns true if solid ground is within groundCheckDistance.
    /// </summary>
    private bool GroundExistsBelow()
    {
        if (groundCheckOrigin == null) return false;
        return Physics.Raycast(groundCheckOrigin.position, Vector3.down, groundCheckDistance, groundCheckMask);
    }

    protected override void OnTakeoffRequested()
    {
        if (flightController != null)
            flightController.RequestHover();
        else
            Debug.LogWarning("[DragonGroundController] OnTakeoffRequested: no DragonFlightController found.");
    }
}
