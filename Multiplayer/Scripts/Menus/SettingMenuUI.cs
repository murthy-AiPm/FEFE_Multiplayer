using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsMenuUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    private Resolution[] _resolutions;
    private List<Resolution> _uniqueResolutions = new();
    private int _selectedIndex;

    private const string PrefW = "Settings_Width";
    private const string PrefH = "Settings_Height";
    private const string PrefFS = "Settings_Fullscreen";

    private void Awake()
    {
        BuildResolutionList();
        LoadAndApplySavedSettings(); // applies on menu load (safe)
        if (panel != null) panel.SetActive(false);
    }

    private void BuildResolutionList()
    {
        _resolutions = Screen.resolutions;

        // Make list unique by width/height (ignore refresh rate duplicates)
        var seen = new HashSet<(int w, int h)>();
        _uniqueResolutions.Clear();

        foreach (var r in _resolutions)
        {
            var key = (r.width, r.height);
            if (seen.Add(key))
                _uniqueResolutions.Add(r);
        }

        resolutionDropdown.ClearOptions();
        var options = new List<string>();

        int currentIndex = 0;
        _selectedIndex = 0;

        for (int i = 0; i < _uniqueResolutions.Count; i++)
        {
            var r = _uniqueResolutions[i];
            options.Add($"{r.width} x {r.height}");

            if (r.width == Screen.width && r.height == Screen.height)
                currentIndex = i;
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentIndex;
        resolutionDropdown.RefreshShownValue();
        _selectedIndex = resolutionDropdown.value;

        resolutionDropdown.onValueChanged.AddListener(i => _selectedIndex = i);

        if (fullscreenToggle != null)
            fullscreenToggle.isOn = Screen.fullScreen;
    }

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

        // Apply immediately so your menu renders correctly next launch
        Screen.SetResolution(w, h, fs);

        if (fullscreenToggle != null)
            fullscreenToggle.isOn = fs;

        // Update dropdown selection if present
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
