using System;
using System.Threading.Tasks;
using Unity.Services.RemoteConfig;
using UnityEngine;

/// <summary>
/// Fetches the allowed host version from UGS Remote Config and compares it
/// against this build's hardcoded version string.
///
/// SETUP:
///   1. In UGS Dashboard → Remote Config, create a key://
///        Key:   allowed_version
///        Type:  string
///        Value: v1   (or whatever your current version is)
///   2. Change BUILD_VERSION below to match when making a new build.
///   3. MainMenu reads HostingAllowed after calling CheckAsync().
///
/// When you ship v2:
///   - Set BUILD_VERSION = "v2" in this file before building
///   - Update allowed_version to "v2" in the UGS dashboard
///   - All v1 builds will fetch "v2", see a mismatch, and hide the host button
/// </summary>
public class VersionChecker : MonoBehaviour
{
    public static VersionChecker Instance { get; private set; }

    [Tooltip("This build's version string. Change this for every new build you distribute.")]
    [SerializeField] private string buildVersion = "v1";

    private const string RemoteConfigKey = "allowed_version";

    public string BuildVersion => buildVersion;

    public bool HostingAllowed { get; private set; } = false;
    public bool CheckComplete { get; private set; } = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Call this from MainMenu after UGS is initialized (i.e. after authentication).
    /// Returns true if this build is allowed to host.
    /// </summary>
    public async Task<bool> CheckAsync()
    {
        CheckComplete = false;
        HostingAllowed = false;

        // Wait for UGS authentication to complete before hitting Remote Config
        float authTimeout = 15f;
        float authElapsed = 0f;
        while (AuthenticationWrapper.AuthState != AuthState.Authenticated && authElapsed < authTimeout)
        {
            await System.Threading.Tasks.Task.Delay(200);
            authElapsed += 0.2f;
        }

        if (AuthenticationWrapper.AuthState != AuthState.Authenticated)
        {
            Debug.LogWarning("[VersionChecker] Auth not ready - blocking host.");
            CheckComplete = true;
            return false;
        }

        try
        {
            // userAttributes and appAttributes are required by the API but can be empty structs
            await RemoteConfigService.Instance.FetchConfigsAsync(
                new UserAttributes(), new AppAttributes());

            string allowedVersion = RemoteConfigService.Instance.appConfig.GetString(RemoteConfigKey, "");

            if (string.IsNullOrEmpty(allowedVersion))
            {
                Debug.LogWarning("[VersionChecker] Remote Config key 'allowed_version' not found or empty. Blocking host.");
                HostingAllowed = false;
            }
            else
            {
                HostingAllowed = string.Equals(buildVersion, allowedVersion, StringComparison.OrdinalIgnoreCase);
                Debug.Log($"[VersionChecker] Build: '{buildVersion}' | Allowed: '{allowedVersion}' | HostingAllowed: {HostingAllowed}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VersionChecker] Remote Config fetch failed: {e.Message}. Blocking host as fallback.");
            HostingAllowed = false;
        }

        CheckComplete = true;
        return HostingAllowed;
    }
}

// Required empty structs for RemoteConfigService.FetchConfigsAsync
public struct UserAttributes { }
public struct AppAttributes { }
