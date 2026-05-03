using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local HUD for the dragon player. Drives one horizontal stamina fill and three
/// circular zone fills (head / wings / torso) from a VitalManager.
///
/// Two ways to wire it:
///   A) Place this component under the dragon prefab and assign vitalManager —
///      auto-subscribes to OnVitalChanged and updates fills as values change.
///   B) Leave vitalManager null and call BindVitalManager(vm) from external code
///      (e.g. an owner-only activator) once the local dragon spawns.
/// </summary>
public class DragonUI : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("Optional. If unset, you must call BindVitalManager() at runtime.")]
    [SerializeField] private VitalManager vitalManager;

    [Header("Fills")]
    [Tooltip("Horizontal stamina fill. Image type = Filled.")]
    [SerializeField] private Image staminaFill;
    [Tooltip("Circular head HP fill. Image type = Filled / Radial.")]
    [SerializeField] private Image headFill;
    [Tooltip("Circular wings HP fill. Image type = Filled / Radial.")]
    [SerializeField] private Image wingsFill;
    [Tooltip("Circular torso HP fill. Image type = Filled / Radial.")]
    [SerializeField] private Image torsoFill;

    private bool _subscribed;

    private void OnEnable()
    {
        if (vitalManager != null) Subscribe(vitalManager);
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>Wire a VitalManager at runtime (e.g. for a HUD that lives outside the dragon prefab).</summary>
    public void BindVitalManager(VitalManager vm)
    {
        if (vm == vitalManager) return;
        Unsubscribe();
        vitalManager = vm;
        if (vitalManager != null) Subscribe(vitalManager);
    }

    private void Subscribe(VitalManager vm)
    {
        if (_subscribed) return;
        vm.OnVitalChanged += HandleVitalChanged;
        _subscribed = true;
        PaintAll();
    }

    private void Unsubscribe()
    {
        if (!_subscribed || vitalManager == null) return;
        vitalManager.OnVitalChanged -= HandleVitalChanged;
        _subscribed = false;
    }

    private void HandleVitalChanged(string vitalID, float newVal, float oldVal)
    {
        switch (vitalID)
        {
            case "stamina": SetFill(staminaFill, "stamina"); break;
            case "head":    SetFill(headFill,    "head");    break;
            case "wings":   SetFill(wingsFill,   "wings");   break;
            case "torso":   SetFill(torsoFill,   "torso");   break;
        }
    }

    private void PaintAll()
    {
        SetFill(staminaFill, "stamina");
        SetFill(headFill,    "head");
        SetFill(wingsFill,   "wings");
        SetFill(torsoFill,   "torso");
    }

    private void SetFill(Image img, string vitalID)
    {
        if (img == null) return;
        var v = vitalManager?.GetVital(vitalID);
        img.fillAmount = v != null ? v.Normalized : 0f;
    }

    /// <summary>Legacy entry point — preserved so existing prefab references don't break.</summary>
    public void SetStamina(float current, float max)
    {
        if (staminaFill == null) return;
        staminaFill.fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }
}
