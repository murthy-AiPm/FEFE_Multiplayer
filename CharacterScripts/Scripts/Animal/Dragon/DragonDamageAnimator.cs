using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Bridge between DamageReceiver's animation events and the dragon's Mecanim Animator.
/// Place on the dragon prefab alongside DamageReceiver.
///
/// Hit reaction:
///   Computes hit direction in dragon-local space, sets HitFB / HitLR floats
///   and GotHit bool on the Animator. Code-driven rotation lerps the dragon's
///   transform toward the attacker during the hit (animation handles the visual flinch,
///   code handles the actual facing change).
///
/// Death:
///   1D blend tree using DeathLR only (left/right). Sets DeathLR float and IsDead bool.
///
/// Animator parameter setup:
///   Bool:  GotHit, IsDead
///   Float: HitFB, HitLR, DeathLR
///
/// Hit clip import settings:
///   Root Transform Rotation  → Bake Into Pose: UNCHECKED
///   Root Transform Pos (XZ)  → Bake Into Pose: UNCHECKED
///   Root Transform Pos (Y)   → Bake Into Pose: CHECKED
///   (Hit motion driven by root motion via DragonHitRootMotion StateMachineBehaviour
///    on the hit blend tree state.)
/// </summary>
public class DragonDamageAnimator : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DamageReceiver damageReceiver;
    [SerializeField] private Animator animator;
    [SerializeField] private DragonGroundController groundController;
    [SerializeField] private DragonFlightController flightController;
    [SerializeField] private AnimalGroundAlignment groundAlignment;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private VitalManager vitalManager;

    [Header("Death Fall Ground Detection")]
    [Tooltip("Raycast origin for detecting ground during death fall. Uses transform.position if unset.")]
    [SerializeField] private Transform deathFallRayOrigin;
    [Tooltip("How close to the ground before triggering the impact animation.")]
    [SerializeField] private float deathFallGroundDistance = 3f;
    [Tooltip("Layers that count as ground for death fall detection.")]
    [SerializeField] private LayerMask deathFallGroundMask = ~0;
    [Tooltip("Gravity acceleration during death fall (units/sec²).")]
    [SerializeField] private float deathFallGravity = 20f;
    [Tooltip("Maximum fall speed during death fall.")]
    [SerializeField] private float deathFallMaxSpeed = 40f;

    [Header("Hit Reaction")]
    [Tooltip("Seconds before GotHit resets to false.")]
    [SerializeField] private float hitResetDelay = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool debugLogging = false;
    [Tooltip("Enable keypad testing: Hit = 8/2/4/6 (FB/LR, supports diagonals), " +
             "Death = 7(-1)/1(-0.5)/3(+0.5)/9(+1), Kill = 5, Reset = 0. REMOVE BEFORE SHIPPING.")]
    [SerializeField] private bool debugKeypadTesting = false;

    // Animator param hashes
    private static readonly int Hash_GotHit  = Animator.StringToHash("GotHit");
    private static readonly int Hash_HitFB   = Animator.StringToHash("HitFB");
    private static readonly int Hash_HitLR   = Animator.StringToHash("HitLR");
    private static readonly int Hash_IsDead  = Animator.StringToHash("IsDead");
    private static readonly int Hash_DeathLR = Animator.StringToHash("DeathLR");
    private static readonly int Hash_DeathImpact = Animator.StringToHash("DeathImpact");
    private static readonly int Hash_FlightMode = Animator.StringToHash("FlightMode");

    private bool  _isDead = false;
    private bool  _deathFalling = false; // falling from sky after death
    private float _deathFallVelocity = 0f;
    private float _hitResetTimer = -1f;

    // Hit anim active state — driven by DragonHitRootMotion StateMachineBehaviour.
    // _wasHitAnimActive tracks the previous frame so we can detect the true→false
    // transition and run cleanup once when the hit animation ends.
    private bool _hitAnimActive;
    private bool _wasHitAnimActive;

    /// <summary>
    /// Set by DragonHitRootMotion (StateMachineBehaviour) on the hit blend tree state.
    /// True for the duration of the hit animation. Cleanup runs in Update on the
    /// frame this flips back to false.
    /// </summary>
    public bool HitAnimActive
    {
        get => _hitAnimActive;
        set => _hitAnimActive = value;
    }

    private void Awake()
    {
        if (damageReceiver == null) damageReceiver = GetComponentInParent<DamageReceiver>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (groundController == null) groundController = GetComponentInParent<DragonGroundController>();
        if (flightController == null) flightController = GetComponentInParent<DragonFlightController>();
        if (groundAlignment == null) groundAlignment = GetComponentInParent<AnimalGroundAlignment>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
        if (vitalManager == null) vitalManager = GetComponentInParent<VitalManager>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (damageReceiver != null)
        {
            damageReceiver.OnPlayHitAnimation += HandleHitAnimation;
            damageReceiver.OnPlayDeathAnimation += HandleDeathAnimation;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (damageReceiver != null)
        {
            damageReceiver.OnPlayHitAnimation -= HandleHitAnimation;
            damageReceiver.OnPlayDeathAnimation -= HandleDeathAnimation;
        }

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        // Auto-reset GotHit after a short delay so the Animator transition can fire.
        if (_hitResetTimer > 0f)
        {
            _hitResetTimer -= Time.deltaTime;
            if (_hitResetTimer <= 0f)
            {
                animator.SetBool(Hash_GotHit, false);
            }
        }

        // Detect hit anim ending (true → false transition driven by
        // DragonHitRootMotion StateMachineBehaviour). Runs cleanup once.
        if (_wasHitAnimActive && !_hitAnimActive)
        {
            if (IsOwner)
            {
                if (groundAlignment != null)
                {
                    float finalYaw = rb != null ? rb.rotation.eulerAngles.y : transform.eulerAngles.y;
                    groundAlignment.SetYawImmediate(finalYaw);
                    groundAlignment.SuspendAlignment = false;
                }

                if (groundController != null)
                    groundController.SyncYawAfterHit();
            }

            if (debugLogging)
                Debug.Log($"[DragonDamageAnimator] Hit anim finished, yaw={transform.eulerAngles.y:F1}°");
        }
        _wasHitAnimActive = _hitAnimActive;

        // Death fall — apply gravity and check for ground
        if (_deathFalling && IsOwner)
        {
            ApplyDeathFallGravity();
            CheckDeathFallGround();
        }
        else if (_isDead && IsOwner && !_deathFalling)
        {
            Debug.Log($"[DragonDamageAnimator] Dead but NOT deathFalling. FlightMode={animator?.GetBool(Hash_FlightMode)}, vel={rb?.linearVelocity}");
        }

        // ── DEBUG KEYPAD TESTING — REMOVE BEFORE SHIPPING ──
        if (debugKeypadTesting && IsOwner && animator != null)
        {
            float hitFB = 0f;
            float hitLR = 0f;
            bool hitPressed = false;

            if (Input.GetKeyDown(KeyCode.Keypad8)) { hitFB += 1f; hitPressed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad2)) { hitFB -= 1f; hitPressed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad4)) { hitLR -= 1f; hitPressed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad6)) { hitLR += 1f; hitPressed = true; }

            if (hitPressed)
                DebugTriggerHit(hitFB, hitLR);

            if (Input.GetKeyDown(KeyCode.Keypad7)) DebugTriggerDeath(-1f);
            if (Input.GetKeyDown(KeyCode.Keypad1)) DebugTriggerDeath(-0.5f);
            if (Input.GetKeyDown(KeyCode.Keypad3)) DebugTriggerDeath(0.5f);
            if (Input.GetKeyDown(KeyCode.Keypad9)) DebugTriggerDeath(1f);

            if (Input.GetKeyDown(KeyCode.Keypad5)) DebugInstantKill();
            if (Input.GetKeyDown(KeyCode.Keypad0)) ResetDeathState();
        }
    }

    // ── DEBUG HELPERS — REMOVE BEFORE SHIPPING ──

    private void DebugTriggerHit(float fb, float lr)
    {
        if (_isDead) return;

        // For debug, simulate an attacker position based on FB/LR direction
        Vector3 attackDir = transform.forward * fb + transform.right * lr;
        Vector3 fakeAttackerPos = transform.position + attackDir.normalized * 5f;

        TriggerHit(fb, lr, fakeAttackerPos);

        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] DEBUG Hit FB={fb:F1} LR={lr:F1}");
    }

    private void DebugTriggerDeath(float lr)
    {
        if (_isDead) return;
        _isDead = true;
        animator.SetFloat(Hash_DeathLR, lr);
        animator.SetBool(Hash_IsDead, true);
        _hitResetTimer = -1f;
        animator.SetBool(Hash_GotHit, false);
        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] DEBUG Death LR={lr:F1}");
    }

    private void DebugInstantKill()
    {
        if (_isDead) return;
        if (vitalManager != null)
        {
            vitalManager.ApplyDamage("health", 99999f);
            if (debugLogging)
                Debug.Log("[DragonDamageAnimator] DEBUG Instant kill via VitalManager");
        }
        else
        {
            // Fallback if no VitalManager — trigger death animation directly
            DebugTriggerDeath(Random.value > 0.5f ? 1f : -1f);
            if (debugLogging)
                Debug.Log("[DragonDamageAnimator] DEBUG Instant kill (no VitalManager, direct anim)");
        }
    }

    // ─── Shared hit trigger logic ───

    private void TriggerHit(float fb, float lr, Vector3 attackerWorldPos)
    {
        // Set Animator params on all clients (this runs from ClientRpc).
        // The hit blend tree state will fire DragonHitRootMotion.OnStateEnter,
        // which flips HitAnimActive / HitRootMotionActive on owner and remotes alike.
        // Root motion drives position + rotation; remotes mirror via NetworkTransform.
        animator.SetFloat(Hash_HitFB, fb);
        animator.SetFloat(Hash_HitLR, lr);
        animator.SetBool(Hash_GotHit, true);
        _hitResetTimer = hitResetDelay;

        // Owner suspends alignment for the duration of the hit so the ground
        // alignment system doesn't fight the root-motion rotation.
        if (!IsOwner) return;

        if (groundAlignment != null)
            groundAlignment.SuspendAlignment = true;
    }

    // ─── Hit Reaction ───

    private void HandleHitAnimation(Vector3 attackerWorldPos)
    {
        if (_isDead || animator == null) return;
        if (_hitAnimActive) return; // Already playing a hit reaction — skip anim, damage still goes through

        ComputeDirection(attackerWorldPos, out float fb, out float lr);
        TriggerHit(fb, lr, attackerWorldPos);

        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] Hit from FB={fb:F2} LR={lr:F2}");
    }

    // ─── Death ───

    private void HandleDeathAnimation(Vector3 attackerWorldPos)
    {
        if (_isDead || animator == null) return;
        _isDead = true;

        float deathLR = ComputeDeathLR(attackerWorldPos);

        animator.SetFloat(Hash_DeathLR, deathLR);
        animator.SetBool(Hash_IsDead, true);

        _hitResetTimer = -1f;
        animator.SetBool(Hash_GotHit, false);

        // Disable root motion so the death animation doesn't slide the rigidbody
        animator.applyRootMotion = false;

        // Stop flight processing but keep FlightMode TRUE on animator
        // so ground death anim (IsGrounded + IsDead) doesn't fight flight death anim
        if (IsOwner && flightController != null && flightController.IsFlightMode)
        {
            flightController.ExitFlight();
            // Re-enable FlightMode on animator — ExitFlight cleared it,
            // but we need it true so the Animator stays in flight death states
            if (animator != null)
                animator.SetBool(Hash_FlightMode, true);
            _deathFalling = true;
            Debug.Log($"[DragonDamageAnimator] _deathFalling=TRUE, FlightMode on animator={animator?.GetBool(Hash_FlightMode)}");
        }

        // Freeze horizontal velocity but allow vertical (gravity)
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Unsuspend alignment (owner only)
        if (IsOwner && groundAlignment != null)
            groundAlignment.SuspendAlignment = false;

        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] Death LR={deathLR:F2}, deathFalling={_deathFalling}");
    }

    // ─── Respawn ───

    public void ResetDeathState(float? spawnYaw = null)
    {
        _isDead = false;
        _deathFalling = false;
        _deathFallVelocity = 0f;
        _hitAnimActive = false;
        _wasHitAnimActive = false;

        // Sync alignment on owner
        if (IsOwner)
        {
            if (groundAlignment != null)
            {
                float yaw = spawnYaw ?? (rb != null ? rb.rotation.eulerAngles.y : transform.eulerAngles.y);
                groundAlignment.SetYawImmediate(yaw);
            }

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        // Unsuspend alignment on ALL clients
        if (groundAlignment != null)
            groundAlignment.SuspendAlignment = false;

        // Re-enable root motion on OWNER only (was disabled on death).
        // On remotes, applyRootMotion must stay false — otherwise Unity's default
        // root motion fights NetworkTransform and inverts the dragon.
        if (animator != null)
        {
            if (IsOwner)
                animator.applyRootMotion = true;

            animator.SetBool(Hash_IsDead, false);
            animator.SetFloat(Hash_DeathLR, 0f);
            animator.SetBool(Hash_GotHit, false);
            animator.SetFloat(Hash_HitFB, 0f);
            animator.SetFloat(Hash_HitLR, 0f);
            animator.SetBool(Hash_FlightMode, false);
        }
    }

    // ─── Direction Computation ───

    private void ComputeDirection(Vector3 attackerWorldPos, out float fb, out float lr)
    {
        Vector3 toAttacker = (attackerWorldPos - transform.position);
        toAttacker.y = 0f;

        if (toAttacker.sqrMagnitude < 0.001f)
        {
            fb = 1f;
            lr = 0f;
            return;
        }

        Vector3 localDir = transform.InverseTransformDirection(toAttacker.normalized);

        fb = localDir.z;
        lr = localDir.x;

        float absFB = Mathf.Abs(fb);
        float absLR = Mathf.Abs(lr);

        if (absFB > absLR)
        {
            fb = Mathf.Sign(fb);
            lr = 0f;
        }
        else
        {
            fb = 0f;
            lr = Mathf.Sign(lr);
        }
    }

    private float ComputeDeathLR(Vector3 attackerWorldPos)
    {
        Vector3 toAttacker = (attackerWorldPos - transform.position);
        toAttacker.y = 0f;

        if (toAttacker.sqrMagnitude < 0.001f)
            return Random.value > 0.5f ? 1f : -1f;

        Vector3 localDir = transform.InverseTransformDirection(toAttacker.normalized);

        if (Mathf.Abs(localDir.x) < 0.1f)
            return Random.value > 0.5f ? 0.5f : -0.5f;

        return Mathf.Clamp(localDir.x, -1f, 1f);
    }

    // ─── Death Fall ───────────────────────────────────

    /// <summary>
    /// Applies fake gravity during death fall via rigidbody velocity.
    /// Uses velocity instead of MovePosition so physics collisions are respected.
    /// DragonGroundController is disabled during death so nothing will zero this.
    /// </summary>
    private void ApplyDeathFallGravity()
    {
        if (rb == null) return;

        _deathFallVelocity += deathFallGravity * Time.deltaTime;
        _deathFallVelocity = Mathf.Min(_deathFallVelocity, deathFallMaxSpeed);
        rb.linearVelocity = Vector3.down * _deathFallVelocity;
    }

    // ─── Death Fall Ground Detection ─────────────────────

    /// <summary>
    /// Ground check during death fall using Vector3.down.
    /// Fires DeathImpact trigger when ground is close enough.
    /// </summary>
    private void CheckDeathFallGround()
    {
        Vector3 origin = deathFallRayOrigin != null ? deathFallRayOrigin.position : transform.position;
        Vector3 direction = Vector3.down;

        if (Physics.Raycast(origin, direction, out RaycastHit hit,
            deathFallGroundDistance, deathFallGroundMask))
        {
            Debug.DrawLine(origin, hit.point, Color.red);
            Debug.Log($"[DragonDamageAnimator] GROUND HIT! distance={hit.distance:F1}, collider={hit.collider.name}");

            _deathFalling = false;
            DeathImpactServerRpc();
        }
        else
        {
            Debug.DrawRay(origin, direction * deathFallGroundDistance, Color.red);
        }
    }

    [ServerRpc]
    private void DeathImpactServerRpc()
    {
        DeathImpactClientRpc();
    }

    [ClientRpc]
    private void DeathImpactClientRpc()
    {
        if (animator != null)
            animator.SetTrigger(Hash_DeathImpact);
    }

    private void OnDrawGizmos()
    {
        Vector3 origin = deathFallRayOrigin != null ? deathFallRayOrigin.position : transform.position;

        Gizmos.color = Color.red;
        Gizmos.DrawRay(origin, Vector3.down * deathFallGroundDistance);
        Gizmos.DrawSphere(origin, 0.15f);
    }
}
