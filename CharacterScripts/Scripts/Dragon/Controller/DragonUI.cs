using UnityEngine;
using UnityEngine.UI;

public class DragonUI : MonoBehaviour
{
    [SerializeField] private Image staminaFill;

    public void SetStamina(float current, float max)
    {
        staminaFill.fillAmount = Mathf.Clamp01(current / max);
    }
}
