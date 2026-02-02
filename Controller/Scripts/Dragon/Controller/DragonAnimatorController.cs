using UnityEngine;

[RequireComponent(typeof(Animator))]
public class DragonAnimatorController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private Animator animator;

    [Header("Animator Parameters")]
    [SerializeField] private string isHoveringParam = "IsHovering";
    [SerializeField] private string isFlyingParam = "IsFlying";
    [SerializeField] private string isGlidingParam = "IsGliding";
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string airSpeedParam = "AirSpeed";
    [SerializeField] private string verticalSpeedParam = "VerticalSpeed";
    [SerializeField] private string forwardSpeedParam = "ForwardSpeed";
    [SerializeField] private string isDivingParam = "IsDiving";
  

    private int isHoveringHash;
    private int isFlyingHash;
    private int isGlidingHash;
    private int isGroundedHash;
    private int airSpeedHash;
    private int verticalSpeedHash;
    private int forwardSpeedHash;
    private int isDivingHash;


    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>(); // Same GameObject - should work
        }

        if (flightController == null)
        {
            flightController = GetComponentInParent<DragonFlightController>(); // Search parent!
        }

        // Validation
        if (animator == null)
            Debug.LogError("DragonAnimatorController: Could not find Animator!");
        if (flightController == null)
            Debug.LogError("DragonAnimatorController: Could not find DragonFlightController in parent!");

        isDivingHash = Animator.StringToHash(isDivingParam);

        isHoveringHash = Animator.StringToHash(isHoveringParam);
        isFlyingHash = Animator.StringToHash(isFlyingParam);
        isGlidingHash = Animator.StringToHash(isGlidingParam);
        isGroundedHash = Animator.StringToHash(isGroundedParam);
        airSpeedHash = Animator.StringToHash(airSpeedParam);
        verticalSpeedHash = Animator.StringToHash(verticalSpeedParam);
        forwardSpeedHash = Animator.StringToHash(forwardSpeedParam);
    }

    private void LateUpdate()
    {
        if (animator == null || flightController == null)
        {
            Debug.LogError("Missing references!");
            return;
        }

        Debug.Log($"Setting IsFlying = {flightController.IsFlying}"); // Check if this fires

        animator.SetBool(isFlyingHash, flightController.IsFlying);

        animator.SetBool(isHoveringHash, flightController.IsHoverMode);
        animator.SetBool(isFlyingHash, flightController.IsFlying);
        animator.SetBool(isGlidingHash, flightController.IsGliding);
        animator.SetBool(isGroundedHash, flightController.IsGrounded);
        animator.SetFloat(airSpeedHash, flightController.AirSpeed);
        animator.SetBool(isDivingHash, flightController.IsDiving);


        Vector3 velocity = flightController.Velocity;

        animator.SetFloat(verticalSpeedHash, velocity.y);

        float forwardSpeed = Vector3.Dot(velocity, transform.forward);
        animator.SetFloat(forwardSpeedHash, forwardSpeed);
    }

}
