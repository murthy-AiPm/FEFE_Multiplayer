using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

[DefaultExecutionOrder(-50)]
public class OwnerOnlyFreeLook : NetworkBehaviour
{
    [SerializeField] private CinemachineCamera vcam;
    [SerializeField] private int ownerPriority = 20;
    [SerializeField] private int nonOwnerPriority = 0;

    private CinemachineInputAxisController _inputAxisController;
    private float _baseGainX;
    private float _baseGainY;
    private const string PrefGainX = "Gain_X";

    [Tooltip("Optional: GameObjects to only enable for the owner (e.g. input controller)")]
    [SerializeField] private GameObject[] ownerOnlyObjects;

    private void OnEnable()
    {
        _inputAxisController = vcam ? vcam.GetComponent<CinemachineInputAxisController>() : null;
        if (_inputAxisController != null)
        {
            foreach (var controller in _inputAxisController.Controllers)
            {
                if (controller.Name == "Look Orbit X") _baseGainX = controller.Input.Gain;
                else if (controller.Name == "Look Orbit Y") _baseGainY = controller.Input.Gain;
            }
        }
        ApplyGain(PlayerPrefs.GetFloat(PrefGainX, 1f));
        SettingsMenuUI.OnSensitivityChanged += ApplyGain;
    }

    private void OnDisable()
    {
        SettingsMenuUI.OnSensitivityChanged -= ApplyGain;
    }

    private void ApplyGain(float multiplier)
    {
        if (_inputAxisController == null) return;
        foreach (var controller in _inputAxisController.Controllers)
        {
            if (controller.Name == "Look Orbit X") controller.Input.Gain = _baseGainX * multiplier;
            else if (controller.Name == "Look Orbit Y") controller.Input.Gain = _baseGainY * multiplier;
        }
    }

    private void Awake()
    {
        if (!vcam) vcam = GetComponentInChildren<CinemachineCamera>(true);
        if (vcam)
        {
            vcam.Priority = nonOwnerPriority;
            vcam.gameObject.SetActive(false);
        }

        foreach (var obj in ownerOnlyObjects)
            if (obj) obj.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        Apply(IsOwner);
    }

    public override void OnGainedOwnership() => Apply(true);
    public override void OnLostOwnership() => Apply(false);

    private void Apply(bool owner)
    {
        if (vcam)
        {
            vcam.gameObject.SetActive(owner);
            vcam.Priority = owner ? ownerPriority : nonOwnerPriority;
        }

        foreach (var obj in ownerOnlyObjects)
            if (obj) obj.SetActive(owner);
    }
}