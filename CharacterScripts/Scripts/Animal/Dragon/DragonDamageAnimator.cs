using Unity.Netcode;
using UnityEngine;

/// <summary>//
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
///   Root Transform Rotation  → Bake Into Pose: CHECKED
///   Root Transform Pos (XZ)  → Bake Into Pose: CHECKED
///   (All rotation/movement handled by code, not root motion)
/// </summary>
public class DragonDamageAnimator : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private DamageReceiver damageReceiver;
    [SerializeField] private Animator animator;
    [SerializeField] private DragonGroundController groundController;
    [SerializeField] private AnimalGroundAlignment groundAlignment;
    [SerializeField] private Rigidbody rb;

    [Header("Hit Reaction")]
    [Tooltip("Seconds before GotHit resets to false.")]
    [SerializeField] private float hitResetDelay = 0.1f;

    [Header("Hit Rotation (Code-Driven)")]
    [Tooltip("How fast the dragon rotates toward the attacker during the hit (degrees/sec).")]
    [SerializeField] private float hitTurnSpeed = 360f;
    [Tooltip("Extra degrees to rotate PAST facing the attacker. 0 = face attacker exactly. " +
             "30 = overshoot by 30 degrees. Negative values = turn less than fully facing.")]
    [SerializeField] private float hitTurnOvershoot = 0f;
    [Tooltip("How long the code-driven rotation lasts. First half lerps toward target, " +
             "second half holds. Should roughly match the hit clip length.")]
    [SerializeField] private float hitRotationDuration = 0.8f;

    [Header("Debug")]
    [SerializeField] private bool debugLogging = false;
    [Tooltip("Enable keypad testing: Hit = 8/2/4/6 (FB/LR, supports diagonals), " +
             "Death = 7(-1)/1(-0.5)/3(+0.5)/9(+1), Reset = 5. REMOVE BEFORE SHIPPING.")]
    [SerializeField] private bool debugKeypadTesting = false;

    // Animator param hashes
    private static readonly int Hash_GotHit  = Animator.StringToHash("GotHit");
    private static readonly int Hash_HitFB   = Animator.StringToHash("HitFB");
    private static readonly int Hash_HitLR   = Animator.StringToHash("HitLR");
    private static readonly int Hash_IsDead  = Animator.StringToHash("IsDead");
    private static readonly int Hash_DeathLR = Animator.StringToHash("DeathLR");

    private bool  _isDead = false;
    private float _hitResetTimer = -1f;

    // Code-driven rotation state
    private bool  _isRotatingFromHit = false;
    private float _hitRotationTimer = -1f;
    private Quaternion _hitTargetRotation;

    private void Awake()
    {
        if (damageReceiver == null) damageReceiver = GetComponentInParent<DamageReceiver>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (groundController == null) groundController = GetComponentInParent<DragonGroundController>();
        if (groundAlignment == null) groundAlignment = GetComponentInParent<AnimalGroundAlignment>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
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

        // Code-driven rotation toward attacker during hit
        if (_isRotatingFromHit)
        {
            _hitRotationTimer -= Time.deltaTime;

            if (_hitRotationTimer <= 0f)
            {
                // Hit rotation finished — unsuspend alignment and sync yaw
                _isRotatingFromHit = false;

                if (groundAlignment != null)
                {
                    float finalYaw = rb != null ? rb.rotation.eulerAngles.y : transform.eulerAngles.y;
                    groundAlignment.SetYawImmediate(finalYaw);
                    groundAlignment.SuspendAlignment = false;
                }

                if (groundController != null)
                    groundController.SyncYawAfterHit();

                if (debugLogging)
                    Debug.Log($"[DragonDamageAnimator] Hit rotation finished, yaw={transform.eulerAngles.y:F1}°");
            }
            else if (rb != null)
            {
                // Lerp toward attacker during first half of the hit
                float halfDuration = hitRotationDuration * 0.5f;
                float remaining = _hitRotationTimer;
                bool inTurnPhase = remaining > halfDuration;

                if (inTurnPhase)
                {
                    float step = hitTurnSpeed * Time.deltaTime;
                    Quaternion newRot = Quaternion.RotateTowards(rb.rotation, _hitTargetRotation, step);
                    rb.MoveRotation(newRot);
                }
                // Second half: hold position, let animation play out
            }
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

            if (Input.GetKeyDown(KeyCode.Keypad5)) ResetDeathState();
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
        _isRotatingFromHit = false;
        animator.SetFloat(Hash_DeathLR, lr);
        animator.SetBool(Hash_IsDead, true);
        _hitResetTimer = -1f;
        animator.SetBool(Hash_GotHit, false);
        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] DEBUG Death LR={lr:F1}");
    }

    // ─── Shared hit trigger logic ───

    private void TriggerHit(float fb, float lr, Vector3 attackerWorldPos)
    {
        // Set Animator params on all clients (this runs from ClientRpc)
        animator.SetFloat(Hash_HitFB, fb);
        animator.SetFloat(Hash_HitLR, lr);
        animator.SetBool(Hash_GotHit, true);
        _hitResetTimer = hitResetDelay;

        // Code-driven rotation only on owner — remotes get rotation via NetworkTransform
        if (!IsOwner) return;

        Vector3 toAttacker = attackerWorldPos - transform.position;
        toAttacker.y = 0f;

        if (toAttacker.sqrMagnitude > 0.001f)
        {
            Quaternion faceAttacker = Quaternion.LookRotation(toAttacker.normalized);
            _hitTargetRotation = faceAttacker * Quaternion.Euler(0f, hitTurnOvershoot, 0f);

            _isRotatingFromHit = true;
            _hitRotationTimer = hitRotationDuration;

            if (groundAlignment != null)
                groundAlignment.SuspendAlignment = true;
        }
    }

    // ─── Hit Reaction ───

    private void HandleHitAnimation(Vector3 attackerWorldPos)
    {
        if (_isDead || animator == null) return;
        if (_isRotatingFromHit) return; // Already playing a hit reaction — skip anim, damage still goes through

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
        _isRotatingFromHit = false;

        float deathLR = ComputeDeathLR(attackerWorldPos);

        animator.SetFloat(Hash_DeathLR, deathLR);
        animator.SetBool(Hash_IsDead, true);

        _hitResetTimer = -1f;
        animator.SetBool(Hash_GotHit, false);

        // Unsuspend alignment (owner only)
        if (IsOwner && groundAlignment != null)
            groundAlignment.SuspendAlignment = false;

        if (debugLogging)
            Debug.Log($"[DragonDamageAnimator] Death LR={deathLR:F2}");
    }

    // ─── Respawn ───

    public void ResetDeathState()
    {
        _isDead = false;
        _isRotatingFromHit = false;
        _hitRotationTimer = -1f;

        if (groundController != null)
            groundController.SyncYawAfterHit();

        if (animator != null)
        {
            animator.SetBool(Hash_IsDead, false);
            animator.SetFloat(Hash_DeathLR, 0f);
            animator.SetBool(Hash_GotHit, false);
            animator.SetFloat(Hash_HitFB, 0f);
            animator.SetFloat(Hash_HitLR, 0f);
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
}
