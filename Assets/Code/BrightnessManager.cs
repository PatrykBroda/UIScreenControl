using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityAtoms.BaseAtoms;

public class BrightnessManager : MonoBehaviour
{
    [Header("API Config")]
    public string serverURL = "https://unity-server-control-patrykbroda.replit.app";

    [Header("App ID (Unity Atoms)")]
    public StringVariable appIdVariable;
    [SerializeField] private string fallbackAppId = "unity-app-001";
    public string AppId
    {
        get
        {
            if (appIdVariable != null && !string.IsNullOrEmpty(appIdVariable.Value))
                return appIdVariable.Value;
            return fallbackAppId;
        }
    }

    [Header("Authentication")]
    public UserLoginData loginData;

    public bool IsAuthenticated =>
        loginData != null && loginData.IsLoggedIn && loginData.IsTokenValid() && !loginData.IsSessionExpired(30);

    [Header("Brightness Control Options")]
    [Tooltip("Control app window brightness (local to your app)")]
    public bool controlAppBrightness = true;
    [Tooltip("Control Android system brightness (requires WRITE_SETTINGS permission)")]
    public bool controlSystemBrightness = true;
    [Tooltip("Enable automatic brightness control")]
    public bool allowAutoBrightness = false;

    [Header("Polling")]
    public bool autoPollBrightness = true;
    public float pollIntervalSeconds = 120f; // 2 minutes

    [Header("Events")]
    public UnityEvent<int> onBrightnessChanged; // subscribe in Inspector or via code

    [Header("Debug / State")]
    [Tooltip("The most recent brightness value fetched or set.")]
    public int currentBrightness = -1;

    // Android brightness control
    private AndroidJavaObject contentResolver;
    private AndroidJavaObject unityActivity;
    private AndroidJavaObject settingsSystem;
    private bool hasWriteSettingsPermission = false;

    // Brightness polling
    private Coroutine brightnessPollCoroutine;
    private int lastBrightnessValue = -1;

    // Android brightness constants
    private const string SCREEN_BRIGHTNESS = "screen_brightness";
    private const string SCREEN_BRIGHTNESS_MODE = "screen_brightness_mode";
    private const int SCREEN_BRIGHTNESS_MODE_MANUAL = 0;
    private const int SCREEN_BRIGHTNESS_MODE_AUTOMATIC = 1;
    private const int MAX_BRIGHTNESS = 255;

    void Awake()
    {
        // Initialize Android brightness control
        InitializeAndroidBrightnessControl();

        // Hook: automatically apply brightness when changed
        onBrightnessChanged.AddListener(ApplyBrightness);
    }

    void Start()
    {
        if (autoPollBrightness)
            StartBrightnessPolling();
    }

    void OnEnable()
    {
        if (autoPollBrightness && brightnessPollCoroutine == null)
            StartBrightnessPolling();
    }

    void OnDisable()
    {
        StopBrightnessPolling();
    }

    private void InitializeAndroidBrightnessControl()
    {
        if (!Application.isEditor && Application.platform == RuntimePlatform.Android && controlSystemBrightness)
        {
            try
            {
                // Get the Unity activity
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }

                // Get ContentResolver for system settings
                contentResolver = unityActivity.Call<AndroidJavaObject>("getContentResolver");

                // Get Settings.System class
                settingsSystem = new AndroidJavaClass("android.provider.Settings$System");

                // Check if we have WRITE_SETTINGS permission
                CheckWriteSettingsPermission();

                Debug.Log($"[BRIGHTNESS] Android brightness control initialized. Write permission: {hasWriteSettingsPermission}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BRIGHTNESS] Failed to initialize Android brightness control: {ex.Message}");
                controlSystemBrightness = false;
            }
        }
    }

    private void CheckWriteSettingsPermission()
    {
        try
        {
            using (AndroidJavaClass settingsClass = new AndroidJavaClass("android.provider.Settings$System"))
            {
                hasWriteSettingsPermission = settingsClass.CallStatic<bool>("canWrite", unityActivity);
            }

            Debug.Log($"[BRIGHTNESS] WRITE_SETTINGS permission: {hasWriteSettingsPermission}");

            if (!hasWriteSettingsPermission)
            {
                Debug.LogWarning("[BRIGHTNESS] WRITE_SETTINGS permission not granted. System brightness control will be limited.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BRIGHTNESS] Failed to check WRITE_SETTINGS permission: {ex.Message}");
            hasWriteSettingsPermission = false;
        }
    }

    public void StartBrightnessPolling()
    {
        if (brightnessPollCoroutine != null)
            StopCoroutine(brightnessPollCoroutine);

        brightnessPollCoroutine = StartCoroutine(PollBrightnessCoroutine());
    }

    public void StopBrightnessPolling()
    {
        if (brightnessPollCoroutine != null)
            StopCoroutine(brightnessPollCoroutine);
        brightnessPollCoroutine = null;
    }

    private IEnumerator PollBrightnessCoroutine()
    {
        while (autoPollBrightness)
        {
            yield return GetBrightnessCoroutine((success, value, err) => {
                if (success)
                {
                    if (value != lastBrightnessValue)
                    {
                        Debug.Log($"[BRIGHTNESS][POLL] Changed: {lastBrightnessValue} -> {value}");
                        lastBrightnessValue = value;
                        currentBrightness = value;
                        onBrightnessChanged?.Invoke(value);
                    }
                    else
                    {
                        Debug.Log($"[BRIGHTNESS][POLL] No change, value: {value}");
                        currentBrightness = value;
                    }
                }
                else
                {
                    Debug.LogWarning($"[BRIGHTNESS][POLL] Failed: {err}");
                }
            });

            yield return new WaitForSeconds(pollIntervalSeconds);
        }
    }

    // ======================
    // GET BRIGHTNESS
    // ======================
    public void GetBrightness(Action<bool, int, string> callback = null)
    {
        StartCoroutine(GetBrightnessCoroutine(callback));
    }

    private IEnumerator GetBrightnessCoroutine(Action<bool, int, string> callback)
    {
        if (!IsAuthenticated)
        {
            Debug.LogError("[BRIGHTNESS] Not authenticated or missing loginData");
            callback?.Invoke(false, -1, "Not authenticated");
            yield break;
        }

        string endpoint = $"/api/unity/brightness/{AppId}";
        string fullUrl = serverURL.TrimEnd('/') + endpoint;

        Debug.Log($"[BRIGHTNESS][GET] Request: {fullUrl}");

        using (UnityWebRequest req = UnityWebRequest.Get(fullUrl))
        {
            req.SetRequestHeader("Authorization", "Bearer " + loginData.AuthToken);
            yield return req.SendWebRequest();

            Debug.Log($"[BRIGHTNESS][GET] HTTP: {req.responseCode}");
            Debug.Log($"[BRIGHTNESS][GET] Response: '{req.downloadHandler.text}'");

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var response = JsonUtility.FromJson<BrightnessResponse>(req.downloadHandler.text);
                    Debug.Log($"[BRIGHTNESS][GET] Success: {response.brightness}");
                    currentBrightness = response.brightness;
                    callback?.Invoke(true, response.brightness, null);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BRIGHTNESS][GET] JSON Parse Error: {ex.Message}");
                    callback?.Invoke(false, -1, "Parse error");
                }
            }
            else
            {
                Debug.LogError($"[BRIGHTNESS][GET] Failed: {req.error} {req.responseCode}");
                callback?.Invoke(false, -1, $"HTTP {req.responseCode} {req.error}");
            }
        }
    }

    // ======================
    // SET BRIGHTNESS (to API, not device)
    // ======================
    public void SetBrightness(int value, Action<bool, string> callback = null)
    {
        StartCoroutine(SetBrightnessCoroutine(value, callback));
    }

    private IEnumerator SetBrightnessCoroutine(int value, Action<bool, string> callback)
    {
        if (!IsAuthenticated)
        {
            Debug.LogError("[BRIGHTNESS] Not authenticated or missing loginData");
            callback?.Invoke(false, "Not authenticated");
            yield break;
        }

        value = Mathf.Clamp(value, 0, 100);

        string endpoint = $"/api/unity/brightness/{AppId}";
        string fullUrl = serverURL.TrimEnd('/') + endpoint;

        var payload = new BrightnessPayload { brightness = value };
        string json = JsonUtility.ToJson(payload);

        Debug.Log($"[BRIGHTNESS][POST] Request: {fullUrl}");
        Debug.Log($"[BRIGHTNESS][POST] Payload: {json}");

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + loginData.AuthToken);

            yield return req.SendWebRequest();

            Debug.Log($"[BRIGHTNESS][POST] HTTP: {req.responseCode}");
            Debug.Log($"[BRIGHTNESS][POST] Response: '{req.downloadHandler.text}'");

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[BRIGHTNESS][POST] Success! Set to {value}");
                currentBrightness = value;
                onBrightnessChanged?.Invoke(value);
                callback?.Invoke(true, null);
            }
            else
            {
                Debug.LogError($"[BRIGHTNESS][POST] Failed: {req.error} {req.responseCode}");
                callback?.Invoke(false, $"HTTP {req.responseCode} {req.error}");
            }
        }
    }

    // ======================
    // Data Classes
    // ======================
    [Serializable]
    private class BrightnessResponse
    {
        public int brightness;
    }
    [Serializable]
    private class BrightnessPayload
    {
        public int brightness;
    }

    // ======================
    // Apply Brightness (App + Android System)
    // ======================
    public void ApplyBrightness(int brightness)
    {
        brightness = Mathf.Clamp(brightness, 0, 100);

        // Apply to app window brightness
        if (controlAppBrightness)
        {
            ApplyAppBrightness(brightness);
        }

        // Apply to Android system brightness
        if (controlSystemBrightness && Application.platform == RuntimePlatform.Android && !Application.isEditor)
        {
            ApplyAndroidSystemBrightness(brightness);
        }
    }

    private void ApplyAppBrightness(int brightness)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            float brightness01 = Mathf.Clamp01(brightness / 100f);
            
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                var window = activity.Call<AndroidJavaObject>("getWindow");
                var layoutParams = window.Call<AndroidJavaObject>("getAttributes");
                layoutParams.Set("screenBrightness", brightness01); // 0.0 - 1.0
                window.Call("setAttributes", layoutParams);
            }
            
            Debug.Log($"[BRIGHTNESS][APP] Applied app window brightness: {brightness}% (normalized: {brightness01})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BRIGHTNESS][APP] Failed to set app brightness: {e.Message}");
        }
#endif
    }

    private void ApplyAndroidSystemBrightness(int brightness)
    {
        if (contentResolver == null || settingsSystem == null)
        {
            Debug.LogError("[BRIGHTNESS][SYSTEM] Android brightness control not initialized");
            return;
        }

        try
        {
            // Convert 0-100 to 0-255 (Android brightness range)
            int androidBrightness = Mathf.RoundToInt((brightness / 100f) * MAX_BRIGHTNESS);
            androidBrightness = Mathf.Clamp(androidBrightness, 1, MAX_BRIGHTNESS); // Minimum 1 to avoid black screen

            if (hasWriteSettingsPermission)
            {
                // Disable auto brightness first if needed
                if (!allowAutoBrightness)
                {
                    settingsSystem.CallStatic<bool>("putInt", contentResolver, SCREEN_BRIGHTNESS_MODE, SCREEN_BRIGHTNESS_MODE_MANUAL);
                }

                // Set system brightness
                bool success = settingsSystem.CallStatic<bool>("putInt", contentResolver, SCREEN_BRIGHTNESS, androidBrightness);

                if (success)
                {
                    Debug.Log($"[BRIGHTNESS][SYSTEM] Set system brightness: {brightness}% -> {androidBrightness}/255");

                    // Also apply to current window for immediate effect
                    ApplyAppBrightness(brightness);
                }
                else
                {
                    Debug.LogError("[BRIGHTNESS][SYSTEM] Failed to set system brightness via Settings");
                    // Fallback to app-only brightness
                    ApplyAppBrightness(brightness);
                }
            }
            else
            {
                Debug.LogWarning("[BRIGHTNESS][SYSTEM] No WRITE_SETTINGS permission, using app-only brightness");
                ApplyAppBrightness(brightness);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BRIGHTNESS][SYSTEM] Failed to set system brightness: {ex.Message}");
            // Fallback to app brightness
            ApplyAppBrightness(brightness);
        }
    }

    // ======================
    // Android System Brightness Utilities
    // ======================
    public int GetCurrentAndroidSystemBrightness()
    {
        if (contentResolver == null || settingsSystem == null || Application.isEditor)
            return -1;

        try
        {
            int androidBrightness = settingsSystem.CallStatic<int>("getInt", contentResolver, SCREEN_BRIGHTNESS, 128);
            int brightnessPercentage = Mathf.RoundToInt((androidBrightness / (float)MAX_BRIGHTNESS) * 100);
            return brightnessPercentage;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BRIGHTNESS][SYSTEM] Failed to get current system brightness: {ex.Message}");
            return -1;
        }
    }

    public bool IsAutoBrightnessEnabled()
    {
        if (contentResolver == null || settingsSystem == null || Application.isEditor)
            return false;

        try
        {
            int mode = settingsSystem.CallStatic<int>("getInt", contentResolver, SCREEN_BRIGHTNESS_MODE, SCREEN_BRIGHTNESS_MODE_AUTOMATIC);
            return mode == SCREEN_BRIGHTNESS_MODE_AUTOMATIC;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BRIGHTNESS][SYSTEM] Failed to check auto brightness mode: {ex.Message}");
            return false;
        }
    }

    public void SetAutoBrightnessEnabled(bool enabled)
    {
        if (contentResolver == null || settingsSystem == null || !hasWriteSettingsPermission)
        {
            Debug.LogError("[BRIGHTNESS][SYSTEM] Cannot set auto brightness - missing permission or not initialized");
            return;
        }

        try
        {
            int mode = enabled ? SCREEN_BRIGHTNESS_MODE_AUTOMATIC : SCREEN_BRIGHTNESS_MODE_MANUAL;
            bool success = settingsSystem.CallStatic<bool>("putInt", contentResolver, SCREEN_BRIGHTNESS_MODE, mode);

            if (success)
            {
                Debug.Log($"[BRIGHTNESS][SYSTEM] Set auto brightness: {enabled}");
                allowAutoBrightness = enabled;
            }
            else
            {
                Debug.LogError("[BRIGHTNESS][SYSTEM] Failed to set auto brightness mode");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BRIGHTNESS][SYSTEM] Failed to set auto brightness: {ex.Message}");
        }
    }

    public void RequestWriteSettingsPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (!hasWriteSettingsPermission)
            {
                using (AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent"))
                using (AndroidJavaClass settingsClass = new AndroidJavaClass("android.provider.Settings"))
                using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
                {
                    AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent");
                    intent.Call<AndroidJavaObject>("setAction", settingsClass.GetStatic<string>("ACTION_MANAGE_WRITE_SETTINGS"));
                    
                    string packageName = unityActivity.Call<string>("getPackageName");
                    AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + packageName);
                    intent.Call<AndroidJavaObject>("setData", uri);
                    
                    unityActivity.Call("startActivity", intent);
                    
                    Debug.Log("[BRIGHTNESS] Requested WRITE_SETTINGS permission");
                }
            }
            else
            {
                Debug.Log("[BRIGHTNESS] WRITE_SETTINGS permission already granted");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BRIGHTNESS] Failed to request WRITE_SETTINGS permission: {ex.Message}");
        }
#endif
    }

    // ======================
    // Debug Methods (Inspector)
    // ======================
    [ContextMenu("Debug Get Brightness")]
    public void DebugGet()
    {
        GetBrightness((success, value, err) => {
            if (success)
                Debug.Log($"[BRIGHTNESS][DEBUG] API Brightness: {value}");
            else
                Debug.LogError($"[BRIGHTNESS][DEBUG] Failed: {err}");
        });

        // Also get current Android system brightness
        int androidBrightness = GetCurrentAndroidSystemBrightness();
        if (androidBrightness >= 0)
        {
            Debug.Log($"[BRIGHTNESS][DEBUG] Current Android System Brightness: {androidBrightness}%");
        }

        bool autoEnabled = IsAutoBrightnessEnabled();
        Debug.Log($"[BRIGHTNESS][DEBUG] Auto brightness enabled: {autoEnabled}");
    }

    [ContextMenu("Debug Set Brightness To 75")]
    public void DebugSet()
    {
        SetBrightness(75, (success, err) => {
            if (success)
                Debug.Log("[BRIGHTNESS][DEBUG] Set to 75 successfully");
            else
                Debug.LogError($"[BRIGHTNESS][DEBUG] Set failed: {err}");
        });
    }

    [ContextMenu("Debug Min Brightness")]
    public void DebugMinBrightness()
    {
        ApplyAndroidSystemBrightness(5); // 5% minimum to avoid completely black screen
        Debug.Log("[BRIGHTNESS][DEBUG] Set to minimum brightness (5%)");
    }

    [ContextMenu("Debug Max Brightness")]
    public void DebugMaxBrightness()
    {
        ApplyAndroidSystemBrightness(100);
        Debug.Log("[BRIGHTNESS][DEBUG] Set to maximum brightness (100%)");
    }

    [ContextMenu("Debug Toggle Auto Brightness")]
    public void DebugToggleAutoBrightness()
    {
        bool currentAuto = IsAutoBrightnessEnabled();
        SetAutoBrightnessEnabled(!currentAuto);
        Debug.Log($"[BRIGHTNESS][DEBUG] Toggled auto brightness: {currentAuto} -> {!currentAuto}");
    }

    [ContextMenu("Debug Request Write Settings Permission")]
    public void DebugRequestPermission()
    {
        RequestWriteSettingsPermission();
    }

    [ContextMenu("Debug Check Permission Status")]
    public void DebugCheckPermission()
    {
        CheckWriteSettingsPermission();
        Debug.Log($"[BRIGHTNESS][DEBUG] Write Settings Permission: {hasWriteSettingsPermission}");
    }

    [ContextMenu("Test Direct Android Brightness Control")]
    public void TestDirectAndroidBrightnessControl()
    {
        Debug.Log("Testing direct Android brightness control...");
        ApplyAndroidSystemBrightness(50);

        int currentBright = GetCurrentAndroidSystemBrightness();
        Debug.Log($"Current Android system brightness after test: {currentBright}%");
    }
}