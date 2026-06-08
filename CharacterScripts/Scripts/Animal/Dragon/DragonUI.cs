using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    [Tooltip("Parent NetworkObject used to show this HUD only for the local owner.")]
    [SerializeField] private NetworkObject networkObject;

    [Header("Fills")]
    [Tooltip("Horizontal stamina fill. Image type = Filled.")]
    [SerializeField] private Image staminaFill;
    [Tooltip("Circular head HP fill. Image type = Filled / Radial.")]
    [SerializeField] private Image headFill;
    [Tooltip("Circular wings HP fill. Image type = Filled / Radial.")]
    [SerializeField] private Image wingsFill;
    [Tooltip("Circular torso HP fill. Image type = Filled / Radial.")]
    [SerializeField] private Image torsoFill;

    [Header("Thrust Readout")]
    [Tooltip("Optional. TMP text that displays the dragon's current flight thrust as a percentage (e.g. '70%' / '-30%'). Leave null to disable.")]
    [SerializeField] private TMP_Text thrustText;
    [Tooltip("Source of the thrust value. Auto-found via GetComponentInParent in OnEnable if left null.")]
    [SerializeField] private DragonAnimatorController dragonAnimatorController;
    [Tooltip("Format string applied to thrust percent (signed integer). Use '{0}%' for '70%' / '-30%'.")]
    [SerializeField] private string thrustFormat = "{0}%";

    private bool _subscribed;
    private bool _firstPaintDone;
    private int _lastThrustPercent = int.MinValue;

    private void OnEnable()
    {
        if (networkObject == null)
            networkObject = GetComponentInParent<NetworkObject>();
        if (vitalManager != null) Subscribe(vitalManager);
        if (dragonAnimatorController == null)
            dragonAnimatorController = GetComponentInParent<DragonAnimatorController>();
    }

    private void Start()
    {
        DisableForRemoteOwner();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (DisableForRemoteOwner()) return;

        UpdateThrustText();

        // VitalManager.OnNetworkSpawn populates _vitals later than DragonUI.OnEnable,
        // so the initial PaintAll in Subscribe sees null vitals and zeroes all fills.
        // Retry until at least one vital is resolvable, then paint once and stop.
        if (!_subscribed || _firstPaintDone || vitalManager == null) return;

        if (vitalManager.GetVital("stamina") == null
            && vitalManager.GetVital("head") == null
            && vitalManager.GetVital("wings") == null
            && vitalManager.GetVital("torso") == null) return;

        PaintAll();
        _firstPaintDone = true;
    }

    private void UpdateThrustText()
    {
        if (thrustText == null || dragonAnimatorController == null) return;

        int pct = Mathf.RoundToInt(dragonAnimatorController.NetFlightThrust * 100f);
        if (pct == _lastThrustPercent) return;
        _lastThrustPercent = pct;
        thrustText.text = string.Format(thrustFormat, pct);
    }

    private bool DisableForRemoteOwner()
    {
        if (networkObject == null)
            networkObject = GetComponentInParent<NetworkObject>();

        if (networkObject != null && networkObject.IsSpawned && !networkObject.IsOwner)
        {
            gameObject.SetActive(false);
            return true;
        }

        return false;
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
        _firstPaintDone = false;
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
