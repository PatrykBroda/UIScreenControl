using System;
using UnityEngine;
using UnityAtoms.BaseAtoms;

public class AppIdManager : MonoBehaviour
{
    [Header("Unity Atoms Integration")]
    [Tooltip("Unity Atoms StringVariable to store the App ID")]
    public StringVariable appIdVariable;

    [Header("App ID Settings")]
    [SerializeField] private bool regenerateOnStart = false;
    [Tooltip("Prefix for the App ID (leave empty to use device name)")]
    [SerializeField] private string customPrefix = "";

    private const string APP_ID_PREF_KEY = "UnityAppId";
    private const string APP_ID_GENERATED_KEY = "AppIdGenerated";

    private string cachedAppId;

    public string AppId => GetAppId();

    public bool IsAppIdGenerated => PlayerPrefs.HasKey(APP_ID_GENERATED_KEY);

    void Awake()
    {
        // Initialize App ID early in the application lifecycle
        InitializeAppId();
    }

    void Start()
    {
        // Log the current App ID for debugging
        LogAppIdInfo();
    }

    /// <summary>
    /// Initializes the App ID - generates if needed or loads existing one
    /// </summary>
    public void InitializeAppId()
    {
        if (regenerateOnStart || !IsAppIdGenerated)
        {
            GenerateAndSaveAppId();
        }
        else
        {
            LoadExistingAppId();
        }

        // Update Unity Atoms variable if assigned
        UpdateUnityAtomsVariable();
    }

    /// <summary>
    /// Generates a new App ID using device name + 4 random numbers
    /// </summary>
    public string GenerateAndSaveAppId()
    {
        string deviceName = GetCleanDeviceName();
        string randomNumbers = GenerateRandomNumbers();
        string newAppId = $"{deviceName}{randomNumbers}";

        // Save to PlayerPrefs
        PlayerPrefs.SetString(APP_ID_PREF_KEY, newAppId);
        PlayerPrefs.SetInt(APP_ID_GENERATED_KEY, 1);
        PlayerPrefs.Save();

        // Cache the new App ID
        cachedAppId = newAppId;

        Debug.Log($"✅ New App ID generated and saved: {newAppId}");

        // Update Unity Atoms variable
        UpdateUnityAtomsVariable();

        return newAppId;
    }

    /// <summary>
    /// Loads existing App ID from PlayerPrefs
    /// </summary>
    private void LoadExistingAppId()
    {
        if (PlayerPrefs.HasKey(APP_ID_PREF_KEY))
        {
            cachedAppId = PlayerPrefs.GetString(APP_ID_PREF_KEY);
            Debug.Log($"📱 Existing App ID loaded: {cachedAppId}");
        }
        else
        {
            Debug.LogWarning("No existing App ID found, generating new one...");
            GenerateAndSaveAppId();
        }
    }

    /// <summary>
    /// Gets the current App ID (loads from cache or PlayerPrefs)
    /// </summary>
    public string GetAppId()
    {
        if (string.IsNullOrEmpty(cachedAppId))
        {
            if (PlayerPrefs.HasKey(APP_ID_PREF_KEY))
            {
                cachedAppId = PlayerPrefs.GetString(APP_ID_PREF_KEY);
            }
            else
            {
                GenerateAndSaveAppId();
            }
        }
        return cachedAppId;
    }

    /// <summary>
    /// Gets a clean device name suitable for App ID
    /// </summary>
    private string GetCleanDeviceName()
    {
        string deviceName;

        if (!string.IsNullOrEmpty(customPrefix))
        {
            deviceName = customPrefix;
        }
        else
        {
            // Use SystemInfo.deviceName and clean it up
            deviceName = SystemInfo.deviceName;

            if (string.IsNullOrEmpty(deviceName) || deviceName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                // Fallback to device model if device name is not available
                deviceName = SystemInfo.deviceModel;

                if (string.IsNullOrEmpty(deviceName))
                {
                    deviceName = "UnityDevice";
                }
            }
        }

        // Clean the device name: remove spaces, special characters, and limit length
        deviceName = CleanString(deviceName);

        // Limit length to reasonable size
        if (deviceName.Length > 15)
        {
            deviceName = deviceName.Substring(0, 15);
        }

        return deviceName;
    }

    /// <summary>
    /// Cleans a string to be suitable for App ID (alphanumeric only)
    /// </summary>
    private string CleanString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "Device";

        System.Text.StringBuilder cleaned = new System.Text.StringBuilder();

        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                cleaned.Append(c);
            }
        }

        string result = cleaned.ToString();
        return string.IsNullOrEmpty(result) ? "Device" : result;
    }

    /// <summary>
    /// Generates 4 random numbers
    /// </summary>
    private string GenerateRandomNumbers()
    {
        System.Random random = new System.Random();
        int randomNumber = random.Next(1000, 10000); // Generates number between 1000-9999
        return randomNumber.ToString();
    }

    /// <summary>
    /// Updates the Unity Atoms StringVariable if assigned
    /// </summary>
    private void UpdateUnityAtomsVariable()
    {
        if (appIdVariable != null && !string.IsNullOrEmpty(cachedAppId))
        {
            appIdVariable.Value = cachedAppId;
            Debug.Log($"🔗 Unity Atoms StringVariable updated with App ID: {cachedAppId}");
        }
    }

    /// <summary>
    /// Forces regeneration of App ID (useful for testing)
    /// </summary>
    [ContextMenu("Regenerate App ID")]
    public void ForceRegenerateAppId()
    {
        Debug.Log("🔄 Forcing App ID regeneration...");
        GenerateAndSaveAppId();
        LogAppIdInfo();
    }

    /// <summary>
    /// Clears saved App ID (useful for testing)
    /// </summary>
    [ContextMenu("Clear Saved App ID")]
    public void ClearSavedAppId()
    {
        PlayerPrefs.DeleteKey(APP_ID_PREF_KEY);
        PlayerPrefs.DeleteKey(APP_ID_GENERATED_KEY);
        PlayerPrefs.Save();
        cachedAppId = null;

        Debug.Log("🗑️ Saved App ID cleared from PlayerPrefs");

        // Regenerate immediately
        InitializeAppId();
    }

    /// <summary>
    /// Logs detailed App ID information for debugging
    /// </summary>
    private void LogAppIdInfo()
    {
        Debug.Log($"=== APP ID MANAGER INFO ===");
        Debug.Log($"Current App ID: '{GetAppId()}'");
        Debug.Log($"Device Name: '{SystemInfo.deviceName}'");
        Debug.Log($"Device Model: '{SystemInfo.deviceModel}'");
        Debug.Log($"Clean Device Name: '{GetCleanDeviceName()}'");
        Debug.Log($"Custom Prefix: '{customPrefix}'");
        Debug.Log($"Is Generated: {IsAppIdGenerated}");
        Debug.Log($"Regenerate On Start: {regenerateOnStart}");
        Debug.Log($"Unity Atoms Variable: {(appIdVariable != null ? appIdVariable.name : "Not Assigned")}");

        if (appIdVariable != null)
        {
            Debug.Log($"Unity Atoms Value: '{appIdVariable.Value}'");
        }

        Debug.Log($"=== END APP ID MANAGER INFO ===");
    }

    /// <summary>
    /// Context menu method for debugging
    /// </summary>
    [ContextMenu("Debug App ID Info")]
    public void DebugAppIdInfo()
    {
        LogAppIdInfo();
    }

    /// <summary>
    /// Sets a custom App ID (overrides generated one)
    /// </summary>
    public void SetCustomAppId(string customAppId)
    {
        if (string.IsNullOrEmpty(customAppId))
        {
            Debug.LogError("Cannot set empty App ID");
            return;
        }

        cachedAppId = customAppId;
        PlayerPrefs.SetString(APP_ID_PREF_KEY, customAppId);
        PlayerPrefs.SetInt(APP_ID_GENERATED_KEY, 1);
        PlayerPrefs.Save();

        UpdateUnityAtomsVariable();

        Debug.Log($"✏️ Custom App ID set: {customAppId}");
    }

    /// <summary>
    /// Gets system information for debugging
    /// </summary>
    public void LogSystemInfo()
    {
        Debug.Log($"=== SYSTEM INFORMATION ===");
        Debug.Log($"Device Name: '{SystemInfo.deviceName}'");
        Debug.Log($"Device Model: '{SystemInfo.deviceModel}'");
        Debug.Log($"Device Type: {SystemInfo.deviceType}");
        Debug.Log($"Operating System: '{SystemInfo.operatingSystem}'");
        Debug.Log($"Platform: {Application.platform}");
        Debug.Log($"=== END SYSTEM INFO ===");
    }

    void OnDestroy()
    {
        // Ensure PlayerPrefs are saved when the object is destroyed
        if (!string.IsNullOrEmpty(cachedAppId))
        {
            PlayerPrefs.Save();
        }
    }
}