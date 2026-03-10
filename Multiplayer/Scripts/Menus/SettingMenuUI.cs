using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;

public class SettingsMenuUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    [Header("Volume Sliders")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider footstepsSlider;
    [SerializeField] private Slider combatSlider;
    [SerializeField] private Slider ambientSlider;

    private Resolution[] _resolutions;
    private List<Resolution> _uniqueResolutions = new();
    private int _selectedIndex;

    // PlayerPrefs keys
    private const string PrefW          = "Settings_Width";
    private const string PrefH          = "Settings_Height";
    private const string PrefFS         = "Settings_Fullscreen";
    private const string PrefMaster     = "Vol_Master";
    private const string PrefSFX        = "Vol_SFX";
    private const string PrefFootsteps  = "Vol_Footsteps";
    private const string PrefCombat     = "Vol_Combat";
    private const string PrefAmbient    = "Vol_Ambient";

    private void Awake()
    {
        BuildResolutionList();
        LoadAndApplySavedSettings();
        LoadAndApplyVolumes();
        if (panel != null) panel.SetActive(false);
    }

    // ═══════════════════════════════════════════════════════
    //  RESOLUTION
    // ═══════════════════════════════════════════════════════

    private void BuildResolutionList()
    {
        _resolutions = Screen.resolutions;
        var seen = new HashSet<(int w, int h)>();
        _uniqueResolutions.Clear();

        foreach (var r in _resolutions)
        {
            var key = (r.width, r.height);
            if (seen.Add(key)) _uniqueResolutions.Add(r);
        }

        resolutionDropdown.ClearOptions();
        var options = new List<string>();
        int currentIndex = 0;
        _selectedIndex = 0;

        for (int i = 0; i < _uniqueResolutions.Count; i++)
        {
            var r = _uniqueResolutions[i];
            options.Add($"{r.width} x {r.height}");
            if (r.width == Screen.width && r.height == Screen.height) currentIndex = i;
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentIndex;
        resolutionDropdown.RefreshShownValue();
        _selectedIndex = resolutionDropdown.value;
        resolutionDropdown.onValueChanged.AddListener(i => _selectedIndex = i);

        if (fullscreenToggle != null) fullscreenToggle.isOn = Screen.fullScreen;
    }

    // ═══════════════════════════════════════════════════════
    //  VOLUME
    // ═══════════════════════════════════════════════════════

    private void LoadAndApplyVolumes()
    {
        SetupSlider(masterSlider,    PrefMaster,    "MasterVolume",    1f);
        SetupSlider(sfxSlider,       PrefSFX,       "SFXVolume",       1f);
        SetupSlider(footstepsSlider, PrefFootsteps, "FootstepsVolume", 1f);
        SetupSlider(combatSlider,    PrefCombat,    "CombatVolume",    1f);
        SetupSlider(ambientSlider,   PrefAmbient,   "AmbientVolume",   1f);
    }

    private void SetupSlider(Slider slider, string prefKey, string mixerParam, float defaultValue)
    {
        if (slider == null) return;
        float saved = PlayerPrefs.GetFloat(prefKey, defaultValue);
        slider.value = saved;
        ApplyVolume(mixerParam, saved);
        slider.onValueChanged.AddListener(value =>
        {
            ApplyVolume(mixerParam, value);
            PlayerPrefs.SetFloat(prefKey, value);
            PlayerPrefs.Save();
        });
    }

    // Converts 0-1 slider value to decibels and applies to mixer
    private void ApplyVolume(string mixerParam, float value)
    {
        if (audioMixer == null) return;
        // Clamp to avoid log(0). Maps 0→1 to -80dB→0dB
        float dB = value > 0.001f ? Mathf.Log10(value) * 20f : -80f;
        audioMixer.SetFloat(mixerParam, dB);
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC
    // ═══════════════════════════════════════════════════════

    public void Open()
    {
        if (panel != null) panel.SetActive(true);
    }

    public void Close()
    {
        if (panel != null) panel.SetActive(false);
    }

    public void Apply()
    {
        if (_uniqueResolutions.Count == 0) return;
        var r = _uniqueResolutions[Mathf.Clamp(_selectedIndex, 0, _uniqueResolutions.Count - 1)];
        bool fs = fullscreenToggle != null ? fullscreenToggle.isOn : Screen.fullScreen;
        Screen.SetResolution(r.width, r.height, fs);
        PlayerPrefs.SetInt(PrefW, r.width);
        PlayerPrefs.SetInt(PrefH, r.height);
        PlayerPrefs.SetInt(PrefFS, fs ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void LoadAndApplySavedSettings()
    {
        int w = PlayerPrefs.GetInt(PrefW, Screen.width);
        int h = PlayerPrefs.GetInt(PrefH, Screen.height);
        bool fs = PlayerPrefs.GetInt(PrefFS, Screen.fullScreen ? 1 : 0) == 1;
        Screen.SetResolution(w, h, fs);

        if (fullscreenToggle != null) fullscreenToggle.isOn = fs;

        if (resolutionDropdown != null && _uniqueResolutions.Count > 0)
        {
            for (int i = 0; i < _uniqueResolutions.Count; i++)
            {
                if (_uniqueResolutions[i].width == w && _uniqueResolutions[i].height == h)
                {
                    resolutionDropdown.value = i;
                    resolutionDropdown.RefreshShownValue();
                    _selectedIndex = i;
                    break;
                }
            }
        }
    }
}
