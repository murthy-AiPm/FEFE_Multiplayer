using UnityEngine;

/// <summary>
/// Put this on the same GameObject as the orc Animator. Animation Events call
/// methods on the Animator object, and this forwards them to OrcAI on the root.
/// </summary>
public class OrcAnimationEventRelay : MonoBehaviour
{
    [SerializeField] private OrcAI orcAI;

    private void Awake()
    {
        if (orcAI == null)
            orcAI = GetComponentInParent<OrcAI>();
    }

    public void HitboxEnable()
    {
        orcAI?.HitboxEnable();
    }

    public void HitboxDisable()
    {
        orcAI?.HitboxDisable();
    }

    public void MainHandHitboxEnable()
    {
        orcAI?.MainHandHitboxEnable();
    }

    public void MainHandHitboxDisable()
    {
        orcAI?.MainHandHitboxDisable();
    }

    public void OffHandHitboxEnable()
    {
        orcAI?.OffHandHitboxEnable();
    }

    public void OffHandHitboxDisable()
    {
        orcAI?.OffHandHitboxDisable();
    }

    public void BothHitboxesEnable()
    {
        orcAI?.BothHitboxesEnable();
    }

    public void BothHitboxesDisable()
    {
        orcAI?.BothHitboxesDisable();
    }
}
