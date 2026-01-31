using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//using FIMSpace.FSpine;

// TODO Fix jumping problem with land and dive animations
// TODO Align controller when Land
// TODO reduce airspeed to zero when script disabled// may create public airspeed variable
// Enable collisions while flying
// Eliminate pitch glitching while low flying;

public class DragonController : ThirdPersonController
{
    [Header("Dragon - Hover / Flight")]
    [SerializeField] int hoverheight;

    [SerializeField] internal GameObject rightArm, leftArm, baseObject;
    [SerializeField] internal bool isFlying;
    [SerializeField] internal bool isGliding;

    [Header("Dragon - Roll")]
    [SerializeField] internal float rollTolerance, rollSmootheTime, rollAngle = 90;

    [Header("Dragon - Flight Params")]
    [SerializeField] internal GameObject dragon;
    [SerializeField] public float airSpeed, momentum, stamina;
    [SerializeField] internal int minairSpeed;
    [SerializeField] internal int maxairSpeed, maxStamina, minStamina;
    [SerializeField] internal float glideDecay;
    [SerializeField] internal float acceleration;
    [SerializeField] internal int momentumModifier;
    [SerializeField] internal float glideSpeedDecay;
    [SerializeField] internal float downTimeRight;
    [SerializeField] internal float keydowntime;

    [Header("UI/Stats")]
    public FlightStats flightStats;

    // Internal / debug
    internal float startRollAngle;
    [SerializeField] float slopeAngleX, slopeAngleZ;
    [SerializeField] internal Vector3 rbodyVelocity;
    [SerializeField] internal bool rfeet, lfeet, rarm, larm;

    public GameObject target;

    [Header("Turn Smooth Times")]
    public float hovermodedisttoground, hovermodepostdisttoground, walkTurnSmootheTime, hoverTurnSmootheTime, currentVelocity;

    // Cached components
    private Rigidbody _rb;
    private DragonColliderManager _dragonCollider;
    private Component[] _spineAnimators;

    // Local working vars
    Vector3 oldEulerAngle;
    float oldRotationAngley;
    private bool butOnlyOnce = false;

    // Layer mask (copied from your original)
    int layerMask = 1 << 9;

    private void OnEnable()
    {
        airSpeed = 0;

        if (playerController != null && playerController.inputController != null)
        {
            playerController.inputController.isHoverMode = false;
            playerController.inputController.isCombatMode = false;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        _rb = GetComponent<Rigidbody>();
        _dragonCollider = GetComponent<DragonColliderManager>();

        // Cache spine animators once (if you still want to use them later)
       // _spineAnimators = GetComponents<FIMSpace.FSpine.FSpineAnimator>();
    }

    protected override void Start()
    {
        base.Start();
        oldEulerAngle = transform.rotation.eulerAngles;
        keydowntime = 1;
        oldRotationAngley = transform.rotation.y;
    }

    protected override void Update()
    {
        // Base update:
        // - caches Snapshot (if you implemented step 2)
        // - Walk/Jump/SpeedModifier/GroundCheck/FreeFall/Gravity flow
        base.Update();

        // Dragon-specific loop (keep behavior close to your original)
        Hover();
        GroundCheck();   // dragon has its own grounding logic
        Walk();          // dragon overrides Walk()
        Fly();
        Roll();
        AirSpeedLogic();
        MovementTime();

        // Dragon overrides Gravity() to do nothing (same as your original)
        Gravity();

        // Stats UI
        if (flightStats != null)
        {
            flightStats.CurrentSpeed(airSpeed);
            flightStats.CurrentStamina(stamina);
            flightStats.SetMaxAirSpeed(maxairSpeed);
            flightStats.SetMinAirSpeed(minairSpeed);
            flightStats.SetMaxStamina(maxStamina);
            flightStats.SetMinStamina(minStamina);
        }

        DragonFlightParameters();

        // Debug velocity
        if (_rb != null) rbodyVelocity = _rb.linearVelocity;

        // Safety: if controller not ready yet, stop
        if (controller == null) return;
    }

    // STEP 3 support: only keep this if your ThirdPersonController has UpdateLocomotionState() virtual
    protected override void UpdateLocomotionState()
    {
        if (isFlying)
        {
            Locomotion = LocomotionState.Flying;
            return;
        }

        if (playerController != null && playerController.inputController != null && playerController.inputController.isHoverMode)
        {
            Locomotion = LocomotionState.Hovering;
            return;
        }

        base.UpdateLocomotionState();
    }

    protected override void FreeFall()
    {
        CameraCalculations(out float targetAngle, out float angle);

        if (_rb != null && _rb.linearVelocity.y < -floatfreefall)
        {
            isfreeFall = true;
            transform.rotation = Quaternion.Euler(slopeAngleX, angle, 0);
        }
        else if (isfreeFall)
        {
            transform.Translate(Vector3.forward * 20 * Time.deltaTime);
        }
    }

    protected override void Gravity()
    {
        // Dragon uses Rigidbody movement for hover/fly; keep empty like your original
        return;
    }

    protected override void SpeedModifier()
    {
        if (isModified)
        {
            if (playerController != null && playerController.inputController != null && playerController.inputController.isCombatMode)
                speedModifier = minSpeedModifer;
            else
                speedModifier = maxSpeedModifier;
        }
        else
        {
            speedModifier = 1;
        }
    }

    protected override void Walk()
    {
        CameraCalculations(out float targetAngle, out float angle);

        bool hover = (playerController != null && playerController.inputController != null && playerController.inputController.isHoverMode);

        if (!isMoving && !isFlying && isgrounded && !hover)
        {
            turnSmootheTime = walkTurnSmootheTime;
        }

        if (isMoving && !isFlying && isgrounded && !hover)
        {
            transform.rotation = Quaternion.Euler(slopeAngleX, angle, slopeAngleZ);
            turnSmootheTime = walkTurnSmootheTime;

            oldRotationAngley = angle;
            transform.Translate(Vector3.forward * speed * speedModifier * Time.deltaTime);
        }
    }

    protected virtual void Hover()
    {
        CameraCalculations(out float targetAngle, out float angle);

        bool hover = (playerController != null && playerController.inputController != null && playerController.inputController.isHoverMode);

        if (hover)
        {
            isJumpPressed = false;
            isfreeFall = false;

            turnSmootheTime = hoverTurnSmootheTime;

            if (_rb != null)
            {
                _rb.useGravity = false;
                _rb.linearVelocity = Vector3.zero;
            }

            oldRotationAngley = angle;

            if (isgrounded)
            {
                transform.Translate(Vector3.up * hoverheight * Time.deltaTime);
                DistToGround = hovermodedisttoground;
            }
            else
            {
                hovermodedisttoground = hovermodepostdisttoground;
            }

            // NOTE: This still uses Input.GetAxisRaw("Hover") from your original.
            // If you want it multiplayer-friendly, move this into Snapshot later.
            float hoverAxis = Input.GetAxisRaw("Hover");

            if (hoverAxis != 0 && !isMoving && !isgrounded)
            {
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
                transform.Translate(Vector3.up * hoverAxis * hoverheight * Time.deltaTime);
            }
            else if (hoverAxis == 0 && !isMoving && !isgrounded && airSpeed <= minairSpeed)
            {
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }
            else if (isGliding && airSpeed >= minairSpeed)
            {
                gravityValue = Mathf.Lerp(0, -9.81f, glideDecay / airSpeed);
                if (_rb != null)
                    transform.Translate(_rb.linearVelocity.normalized * gravityValue * -1 * Time.deltaTime);
            }
        }
        else
        {
            DistToGround = hovermodepostdisttoground;

            if (_rb != null) _rb.useGravity = true;
            oldRotationAngley = angle;
        }
    }

    protected override void GroundCheck()
    {
        Ray LeftFeet = new Ray(leftFeet.transform.position, Vector3.down);
        Ray RightFeet = new Ray(rightFeet.transform.position, Vector3.down);
        Ray RightArm = new Ray(rightArm.transform.position, Vector3.down);
        Ray LeftArm = new Ray(leftArm.transform.position, Vector3.down);

        Debug.DrawRay(leftFeet.transform.position, Vector3.down * DistToGround);
        Debug.DrawRay(rightFeet.transform.position, Vector3.down * DistToGround);
        Debug.DrawRay(leftArm.transform.position, Vector3.down * DistToGround);
        Debug.DrawRay(rightArm.transform.position, Vector3.down * DistToGround);

        lfeet = Physics.Raycast(LeftFeet, out hit, DistToGround, ~layerMask);
        rfeet = Physics.Raycast(RightFeet, out hit, DistToGround, ~layerMask);
        rarm = Physics.Raycast(RightArm, out hit, DistToGround, ~layerMask);
        larm = Physics.Raycast(LeftArm, out hit, DistToGround, ~layerMask);

        FineGrounding();
    }

    private void FineGrounding()
    {
        int q = lfeet ? 1 : 0;
        int w = larm ? 1 : 0;
        int e = rfeet ? 1 : 0;
        int r = rarm ? 1 : 0;

        bool hover = (playerController != null && playerController.inputController != null && playerController.inputController.isHoverMode);

        if (!hover)
        {
            if (!lfeet && !rfeet && (rarm && larm))
            {
                isgrounded = true;
                isfreeFall = true;
            }
            else if (lfeet && rfeet && (!rarm && !larm))
            {
                isfreeFall = true;
                isgrounded = false;
            }
            else if (q + w + e + r >= 3)
            {
                isgrounded = true;
                isfreeFall = false;
            }
            else if (q + w + e + r == 0)
            {
                isfreeFall = true;
                isgrounded = false;
            }
            else if ((lfeet && larm) || (rfeet && rarm))
            {
                isgrounded = true;
            }
            else if (!lfeet && !rfeet && !larm && !rarm)
            {
                isgrounded = false;
            }
            else
            {
                isgrounded = false;
            }
        }
        else
        {
            if (lfeet || rfeet || larm || rarm) isgrounded = true;
            else isgrounded = false;
        }
    }

    protected virtual void Fly()
    {
        Quaternion roll = Quaternion.AngleAxis(startRollAngle, Vector3.forward);
        Quaternion rotation = Quaternion.LookRotation(-cam.position + transform.position, Vector3.up);
        Quaternion pitch = Quaternion.LookRotation(-cam.position + transform.position, Vector3.forward);

        bool hover = (playerController != null && playerController.inputController != null && playerController.inputController.isHoverMode);

        if (hover && isMoving)
        {
            isFlying = true;
            isGliding = false;
            isfreeFall = false;

            transform.Translate(Vector3.forward * airSpeed * Time.deltaTime, Space.Self);

            Vector3 pos = transform.position;

            if (isgrounded)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 5 * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, roll.eulerAngles.z);

                if (_dragonCollider != null && _dragonCollider.dragonObstacleDetect)
                    pos.y = Terrain.activeTerrain.SampleHeight(transform.position) + 3f;

                transform.position = pos;

                if (isModified)
                    transform.Translate(Vector3.up * 3, Space.Self);
            }
            else if (input.secondaryHeld) // Snapshot-friendly
            {
                transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            }
            else
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 5 * Time.deltaTime);
                transform.rotation = Quaternion.Euler(pitch.eulerAngles.x, transform.eulerAngles.y, roll.eulerAngles.z);
            }
        }
        else if (hover && airSpeed >= minairSpeed && !isMoving)
        {
            oldRotationAngley = transform.rotation.eulerAngles.y;

            transform.Translate(Vector3.forward * airSpeed * Time.deltaTime, Space.Self);

            isGliding = true;
            isFlying = true;
            isfreeFall = false;

            Vector3 pos = transform.position;

            if (isgrounded)
            {
                transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);

                if (_dragonCollider != null && _dragonCollider.dragonObstacleDetect)
                    pos.y = Terrain.activeTerrain.SampleHeight(transform.position) + 3f;

                transform.position = pos;

                transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 5 * Time.deltaTime);
            }
            else if (input.secondaryHeld)
            {
                transform.rotation = Quaternion.Euler(transform.eulerAngles.x, transform.eulerAngles.y, transform.eulerAngles.z);
            }
            else
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 5 * Time.deltaTime);
                transform.rotation = Quaternion.Euler(transform.eulerAngles.x, transform.eulerAngles.y, roll.eulerAngles.z);
            }
        }
        else
        {
            isFlying = false;
            isGliding = false;
        }
    }

    private void Roll()
    {
        if (oldEulerAngle.y - transform.rotation.eulerAngles.y >= rollTolerance)
        {
            startRollAngle = Mathf.LerpAngle(transform.eulerAngles.z, rollAngle, rollSmootheTime);
        }
        else if (oldEulerAngle.y - transform.rotation.eulerAngles.y <= -rollTolerance)
        {
            startRollAngle = Mathf.LerpAngle(transform.eulerAngles.z, -rollAngle, rollSmootheTime);
        }
        else
        {
            startRollAngle = Mathf.LerpAngle(transform.eulerAngles.z, 0, rollSmootheTime);
            rollAngle = 50;
        }

        oldEulerAngle = transform.rotation.eulerAngles;
    }

    protected override void Jump()
    {
        // Dragon jump is handled by hover/fly behavior
        return;
    }

    protected virtual float AirSpeedLogic()
    {
        bool hover = (playerController != null && playerController.inputController != null && playerController.inputController.isHoverMode);

        // Smaller smooth time => faster decay
        if (hover && isMoving)
        {
            airSpeed = Mathf.SmoothDamp(airSpeed, maxairSpeed, ref flySmoothVelocity, acceleration);
        }

        if (hover && airSpeed >= minairSpeed && !isMoving)
        {
            airSpeed = Mathf.SmoothDamp(airSpeed, 0, ref flySmoothVelocity, momentum);
        }

        if (isGliding && isModified)
        {
            airSpeed = Mathf.SmoothDamp(airSpeed, 0, ref flySmoothVelocity, keydowntime / acceleration);
        }

        if (!hover && !isgrounded && isfreeFall)
        {
            airSpeed = Mathf.SmoothDamp(airSpeed, 0, ref flySmoothVelocity, acceleration);
        }

        if (isgrounded && !hover)
        {
            airSpeed = 0;
        }

        return airSpeed;
    }

    protected virtual void DragonFlightParameters()
    {
        momentum = (airSpeed) / momentumModifier;

        bool hover = (playerController != null && playerController.inputController != null && playerController.inputController.isHoverMode);

        if (isMoving && isFlying)
        {
            if (stamina <= minStamina) stamina = minStamina;
            else stamina = stamina - acceleration / 50;
        }

        if (!isMoving && hover)
        {
            if (stamina >= maxStamina) stamina = maxStamina;
            else stamina = stamina + acceleration / 50;
        }

        if (isgrounded)
        {
            if (stamina >= maxStamina) stamina = maxStamina;
            stamina = stamina + acceleration / 50;
        }
    }

    private void MovementTime()
    {
        if (isMoving)
        {
            downTimeRight += Time.deltaTime;
            butOnlyOnce = true;
        }

        if (!isMoving && butOnlyOnce)
        {
            butOnlyOnce = false;
            keydowntime = downTimeRight;
            downTimeRight = 0;
        }
    }

    // OPTIONAL: if you want to re-enable spine anim toggling later
    //private void SpineAnimatorToggle()
    //{
    //    if (_spineAnimators == null) return;

    //    bool hover = (playerController != null && playerController.inputController != null && playerController.inputController.isHoverMode);

    //    foreach (var c in _spineAnimators)
    //    {
    //        var spine ==null;//c as FSpineAnimator;
    //        if (spine == null) continue;

    //        if (hover) spine.enabled = false;
    //        else if (isgrounded && isMoving) spine.enabled = true;
    //    }
    //}
}
