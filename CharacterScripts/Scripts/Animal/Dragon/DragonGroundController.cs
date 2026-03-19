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

        if (!inFlight && !groundingSystem.IsGrounded && !IsPlayingJump && !_isSwimming)
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

    protected override void OnTakeoffRequested()
    {
        if (flightController != null)
            flightController.RequestHover();
        else
            Debug.LogWarning("[DragonGroundController] OnTakeoffRequested: no DragonFlightController found.");
    }
}
