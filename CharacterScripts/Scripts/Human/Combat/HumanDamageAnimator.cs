using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Bridge between DamageReceiver's animation events and RuleAnimancerDriver.
/// Place on the human player prefab alongside DamageReceiver.
/// Subscribes to OnPlayHitAnimation / OnPlayDeathAnimation and forwards
/// to the existing PlayHitReaction / PlayDeath methods on RuleAnimancerDriver.
/// </summary>
public class HumanDamageAnimator : NetworkBehaviour
{
    [SerializeField] private DamageReceiver damageReceiver;
    [SerializeField] private RuleAnimancerDriver animancerDriver;

    private void Awake()
    {
        if (damageReceiver == null) damageReceiver = GetComponentInParent<DamageReceiver>();
        if (animancerDriver == null) animancerDriver = GetComponentInChildren<RuleAnimancerDriver>();
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

    private void HandleHitAnimation(Vector3 attackerPosition)
    {
        if (animancerDriver != null)
            animancerDriver.PlayHitReaction(attackerPosition);
    }

    private void HandleDeathAnimation(Vector3 attackerPosition)
    {
        if (animancerDriver != null)
            animancerDriver.PlayDeath();
    }
}
