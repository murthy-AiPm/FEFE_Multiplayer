using UnityEngine;

public class HorseGroundController : DragonGroundController
{
    //[Header("Horse Gravity")]
    //[SerializeField] private float fakeGravity = 20f;
    //[SerializeField] private float maxFallSpeed = 30f;

    //private float _fallVelocity;

    //private void Start()
    //{
    //    if (rb != null)
    //        rb.useGravity = false;
    //}

    //// You'll need to make groundingSystem and rb protected in the base class
    //// (currently private)

    //protected override void FixedUpdate()
    //{
    //    if (groundingSystem != null && groundingSystem.IsGrounded)
    //    {
    //        _fallVelocity = 0f;
    //    }
    //    else
    //    {
    //        // Fake gravity
    //        _fallVelocity += fakeGravity * Time.fixedDeltaTime;
    //        _fallVelocity = Mathf.Min(_fallVelocity, maxFallSpeed);

    //        if (rb != null)
    //            rb.MovePosition(rb.position + Vector3.down * _fallVelocity * Time.fixedDeltaTime);
    //    }

    //    base.FixedUpdate();
    //}
}