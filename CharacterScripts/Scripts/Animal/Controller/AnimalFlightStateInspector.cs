using UnityEngine;

public class AnimalStateInspector : MonoBehaviour
{
    [SerializeField] private DragonFlightController flight;

    [Header("LIVE STATE (Read Only)")]
    [SerializeField] private bool isHoverMode;
    [SerializeField] private bool isFlying;
    [SerializeField] private bool isGliding;
    [SerializeField] private bool isDiving;

    [Header("GROUNDING DEBUG")]
    [SerializeField] private AnimalGroundingSystem groundingSystem;
    [SerializeField] private bool isGroundedDebug;
    [SerializeField] private bool isFallingDebug;
    [SerializeField] private bool frontPawsGroundedDebug;

    [Header("GROUND STATE DEBUG")]
    [SerializeField] private AnimalGroundController groundController;
    [SerializeField] private bool isWalkingDebug;
    [SerializeField] private bool isRunningDebug;

    [SerializeField] private float airSpeed;
    [SerializeField] private float groundDistance;
    [SerializeField] private float rollAngle;
    [SerializeField] private Vector3 velocity;

    [Header("DRAGON STATS")]
    [SerializeField] private float stamina;

    private void Awake()
    {
        // Auto-wire all components if not set
        if (flight == null)
            flight = GetComponent<DragonFlightController>();
        if (groundingSystem == null)
            groundingSystem = GetComponent<AnimalGroundingSystem>();
        if (groundController == null)
            groundController = GetComponent<AnimalGroundController>();
    }

    private void Update()
    {
        // Flight state
        if (flight != null)
        {
            isHoverMode = flight.IsHoverMode;
            isFlying = flight.IsFlying;
            isGliding = flight.IsGliding;
            isDiving = flight.IsDiving;
            airSpeed = flight.AirSpeed;
            rollAngle = flight.RollAngle;
            velocity = flight.Velocity;
            stamina = flight.Stamina;
        }

        // Ground controller state
        if (groundController != null)
        {
            isWalkingDebug = groundController.IsWalking;
            isRunningDebug = groundController.IsRunning;
        }

        // Grounding system state
        if (groundingSystem != null)
        {
            isGroundedDebug = groundingSystem.IsGrounded;
            isFallingDebug = groundingSystem.IsFalling;
            frontPawsGroundedDebug = groundingSystem.FrontPawsGrounded;
        }
    }
}