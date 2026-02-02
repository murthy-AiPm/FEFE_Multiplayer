using UnityEngine;

public class DragonFlightStateInspector : MonoBehaviour
{
    [SerializeField] private DragonFlightController flight;

    [Header("LIVE STATE (Read Only)")]
    [SerializeField] private bool isHoverMode;
    [SerializeField] private bool isFlying;
    [SerializeField] private bool isGliding;
    [SerializeField] private bool isDiving;
    [SerializeField] private bool isGrounded;

    [SerializeField] private float airSpeed;
    [SerializeField] private float groundDistance;
    [SerializeField] private float rollAngle;
    [SerializeField] private Vector3 velocity;

    private void Awake()
    {
        if (!flight)
            flight = GetComponent<DragonFlightController>();
    }

    private void Update()
    {
        if (!flight) return;

        isHoverMode = flight.IsHoverMode;
        isFlying = flight.IsFlying;
        isGliding = flight.IsGliding;
        isDiving = flight.IsDiving;
        isGrounded = flight.IsGrounded;

        airSpeed = flight.AirSpeed;
        //groundDistance = flight.GroundDistance;
        rollAngle = flight.RollAngle;
        velocity = flight.Velocity;
    }
}
