using UnityEngine;

/// <summary>
/// Dragon-specific ground controller. Inherits all movement, gait, jump, and takeoff
/// logic from AnimalGroundController and adds the flight controller handoff on takeoff.
/// </summary>
public class DragonGroundController : AnimalGroundController
{
    [Header("Dragon Flight")]
    [SerializeField] private DragonFlightController flightController;

    private float _jumpGaitSpeed;

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

    protected override void OnLanded()
    {
        // ClearState zeroed GaitSpeed — restore it so MoveTowards can decay gradually.
        GaitSpeed = _jumpGaitSpeed;
    }

    protected override void OnTakeoffRequested()
    {
        if (flightController != null)
            flightController.RequestHover();
        else
            Debug.LogWarning("[DragonGroundController] OnTakeoffRequested: no DragonFlightController found.");
    }
}
