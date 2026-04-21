using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lives on the dragon's fire breath VFX particle system. Forwards particle collisions
/// to DragonFireBreathDamage (damage / burn) and GroundFireSpawner (ground patches).
///
/// Only the OWNER dragon's local VFX instance is bound — remote clients still instantiate
/// the VFX so they see the flame, but their handler stays unbound and inert so we don't
/// get N-clients of duplicated RPCs.
///
/// Required setup on the particle system:
///   - Collision module enabled, Type = World
///   - "Send Collision Messages" = on
///   - Layer mask covering ground + character/zombie layers
///   - Quality = High recommended for tight detection on small/fast particles
///
/// DragonCombatController auto-adds this component to every ParticleSystem in the VFX
/// that has its Collision module enabled, then calls Bind() on the owner only.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class FireBreathParticleHandler : MonoBehaviour
{
    private ParticleSystem _ps;
    private DragonFireBreathDamage _damage;
    private GroundFireSpawner _spawner;
    private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>();

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
    }

    public void Bind(DragonFireBreathDamage damage, GroundFireSpawner spawner)
    {
        _damage = damage;
        _spawner = spawner;
    }

    private void OnParticleCollision(GameObject other)
    {
        if (_damage == null && _spawner == null) return;
        if (_ps == null || other == null) return;

        int count = _ps.GetCollisionEvents(other, _events);
        for (int i = 0; i < count; i++)
        {
            var ev = _events[i];
            if (_damage != null) _damage.HandleParticleHit(other, ev.intersection);
            if (_spawner != null) _spawner.HandleParticleHit(ev.intersection, ev.normal);
        }
    }
}
