using Unity.Netcode;
using UnityEngine;

public enum DamageDefenseResult
{
    None,
    Block,
    Parry
}

public interface IDamageDefenseProvider
{
    bool IsDefenseInvincible { get; }

    DamageDefenseResult EvaluateDefense(NetworkObject attacker, Vector3 hitPoint, float rawDamage);

    void OnServerDefenseResolved(
        DamageDefenseResult result,
        float rawDamage,
        float finalDamage,
        Vector3 hitPoint,
        NetworkObject attacker);
}
