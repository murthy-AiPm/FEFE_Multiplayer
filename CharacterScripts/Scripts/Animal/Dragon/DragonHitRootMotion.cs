using UnityEngine;

/// <summary>
/// StateMachineBehaviour attached to the dragon's hit blend tree state.
/// Toggles root-motion flags on DragonGroundController and DragonDamageAnimator
/// for the duration of the hit animation.
///
/// OnStateEnter  → enable hit root motion (controller applies animator delta to rigidbody)
///               → mark hit anim active on damage animator (suspends ground alignment)
/// OnStateExit   → clear both flags (damage animator runs cleanup in Update on the
///                 frame it sees the flag fall to false)
///
/// No IsOwner check here — the flags are local; the controllers themselves gate
/// owner-only behavior (only the owner writes to the rigidbody; remotes get the
/// result via NetworkTransform).
/// </summary>
public class DragonHitRootMotion : StateMachineBehaviour
{
    private DragonGroundController _groundController;
    private DragonDamageAnimator   _damageAnimator;
    private bool _refsResolved;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        ResolveRefs(animator);

        if (_groundController != null)
            _groundController.HitRootMotionActive = true;

        if (_damageAnimator != null)
            _damageAnimator.HitAnimActive = true;
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (_groundController != null)
            _groundController.HitRootMotionActive = false;

        if (_damageAnimator != null)
            _damageAnimator.HitAnimActive = false;
    }

    private void ResolveRefs(Animator animator)
    {
        if (_refsResolved) return;
        if (animator == null) return;

        _groundController = animator.GetComponentInParent<DragonGroundController>();
        _damageAnimator   = animator.GetComponentInParent<DragonDamageAnimator>();
        _refsResolved = true;
    }
}
