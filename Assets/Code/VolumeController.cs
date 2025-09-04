using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityAtoms.BaseAtoms;

public class VolumeManager : MonoBehaviour
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

    [Header("Volume Control Options")]
    [Tooltip("Control Unity AudioListener volume")]
    public bool controlUnityVolume = true;
    [Tooltip("Control Android system volume (requires Android)")]
    public bool controlSystemVolume = true;
    [Tooltip("Android audio stream type")]
    public AndroidVolumeStreamType streamType = AndroidVolumeStreamType.Music;

    [Header("Polling")]
    public bool autoPollVolume = true;

    [Tooltip("How often to poll the server for volume (seconds).")]
    [Range(1f, 300f)]
    public float pollIntervalSeconds = 7f;

    [Header("Events")]
    public UnityEvent<int> onVolumeChanged;

    [Header("Debug / State")]
    [Tooltip("The most recent volume value fetched or set.")]
    public int currentVolume = -1;

    // Android volume control
    private AndroidJavaObject audioManager;
    private AndroidJavaObject unityActivity;
    private int maxSystemVolume = 100;

    // Volume polling
    private Coroutine volumePollCoroutine;
    private int lastVolumeValue = -1;

    public enum AndroidVolumeStreamType
    {
        Music = 3,          // STREAM_MUSIC - most common for apps
        Ring = 2,           // STREAM_RING
        Notification = 5,   // STREAM_NOTIFICATION
        System = 1,         // STREAM_SYSTEM
        VoiceCall = 0,      // STREAM_VOICE_CALL
        Alarm = 4           // STREAM_ALARM
    }

    void Awake()
    {
        // Initialize Android volume control
        InitializeAndroidVolumeControl();

        // Hook: automatically apply volume when changed
        onVolumeChanged.AddListener(ApplyVolume);
    }

    void Start()
    {
        if (autoPollVolume)
            StartVolumePolling();
    }

    void OnEnable()
    {
        if (autoPollVolume && volumePollCoroutine == null)
            StartVolumePolling();
    }

    void OnDisable()
    {
        StopVolumePolling();
    }

    private void InitializeAndroidVolumeControl()
    {
        if (!Application.isEditor && Application.platform == RuntimePlatform.Android && controlSystemVolume)
        {
            try
            {
                // Get the Unity activity
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }

                // Get the AudioManager
                audioManager = unityActivity.Call<AndroidJavaObject>("getSystemService", "audio");

                // Get max volume for the stream type
                maxSystemVolume = audioManager.Call<int>("getStreamMaxVolume", (int)streamType);

                Debug.Log($"[VOLUME] Android AudioManager initialized. Max volume: {maxSystemVolume} for stream type: {streamType}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VOLUME] Failed to initialize Android volume control: {ex.Message}");
                controlSystemVolume = false;
            }
        }
    }

    public void StartVolumePolling()
    {
        if (volumePollCoroutine != null)
            StopCoroutine(volumePollCoroutine);

        volumePollCoroutine = StartCoroutine(PollVolumeCoroutine());
    }

    public void StopVolumePolling()
    {
        if (volumePollCoroutine != null)
            StopCoroutine(volumePollCoroutine);
        volumePollCoroutine = null;
    }

    private IEnumerator PollVolumeCoroutine()
    {
        while (autoPollVolume)
        {
            // Immediate fetch
            yield return GetVolumeCoroutine((success, value, err) => {
                if (success)
                {
                    if (value != lastVolumeValue)
                    {
                        Debug.Log($"[VOLUME][POLL] Changed: {lastVolumeValue} -> {value}");
                        lastVolumeValue = value;
                        currentVolume = value;
                        onVolumeChanged?.Invoke(value);
                    }
                    else
                    {
                        Debug.Log($"[VOLUME][POLL] No change, value: {value}");
                        currentVolume = value;
                    }
                }
                else
                {
                    Debug.LogWarning($"[VOLUME][POLL] Failed: {err}");
                }
            });

            // Wait for next tick
            yield return new WaitForSeconds(pollIntervalSeconds);
        }
    }

    // ======================
    // GET VOLUME
    // ======================
    public void GetVolume(Action<bool, int, string> callback = null)
    {
        StartCoroutine(GetVolumeCoroutine(callback));
    }

    private IEnumerator GetVolumeCoroutine(Action<bool, int, string> callback)
    {
        if (!IsAuthenticated)
        {
            Debug.LogError("[VOLUME] Not authenticated or missing loginData");
            callback?.Invoke(false, -1, "Not authenticated");
            yield break;
        }

        string endpoint = $"/api/unity/volume/{AppId}";
        string fullUrl = serverURL.TrimEnd('/') + endpoint;

        Debug.Log($"[VOLUME][GET] Request: {fullUrl}");

        using (UnityWebRequest req = UnityWebRequest.Get(fullUrl))
        {
            req.SetRequestHeader("Authorization", "Bearer " + loginData.AuthToken);
            yield return req.SendWebRequest();

            Debug.Log($"[VOLUME][GET] HTTP: {req.responseCode}");
            Debug.Log($"[VOLUME][GET] Response: '{req.downloadHandler.text}'");

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var response = JsonUtility.FromJson<VolumeResponse>(req.downloadHandler.text);
                    Debug.Log($"[VOLUME][GET] Success: {response.volume}");
                    currentVolume = response.volume;
                    callback?.Invoke(true, response.volume, null);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VOLUME][GET] JSON Parse Error: {ex.Message}");
                    callback?.Invoke(false, -1, "Parse error");
                }
            }
            else
            {
                Debug.LogError($"[VOLUME][GET] Failed: {req.error} {req.responseCode}");
                callback?.Invoke(false, -1, $"HTTP {req.responseCode} {req.error}");
            }
        }
    }

    // ======================
    // SET VOLUME (to API, not device)
    // ======================
    public void SetVolume(int value, Action<bool, string> callback = null)
    {
        StartCoroutine(SetVolumeCoroutine(value, callback));
    }

    private IEnumerator SetVolumeCoroutine(int value, Action<bool, string> callback)
    {
        if (!IsAuthenticated)
        {
            Debug.LogError("[VOLUME] Not authenticated or missing loginData");
            callback?.Invoke(false, "Not authenticated");
            yield break;
        }

        value = Mathf.Clamp(value, 0, 100);

        string endpoint = $"/api/unity/volume/{AppId}";
        string fullUrl = serverURL.TrimEnd('/') + endpoint;

        var payload = new VolumePayload { volume = value };
        string json = JsonUtility.ToJson(payload);

        Debug.Log($"[VOLUME][POST] Request: {fullUrl}");
        Debug.Log($"[VOLUME][POST] Payload: {json}");

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + loginData.AuthToken);

            yield return req.SendWebRequest();

            Debug.Log($"[VOLUME][POST] HTTP: {req.responseCode}");
            Debug.Log($"[VOLUME][POST] Response: '{req.downloadHandler.text}'");

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[VOLUME][POST] Success! Set to {value}");
                currentVolume = value;
                onVolumeChanged?.Invoke(value);
                callback?.Invoke(true, null);
            }
            else
            {
                Debug.LogError($"[VOLUME][POST] Failed: {req.error} {req.responseCode}");
                callback?.Invoke(false, $"HTTP {req.responseCode} {req.error}");
            }
        }
    }

    // ======================
    // Data Classes
    // ======================
    [Serializable]
    private class VolumeResponse
    {
        public int volume;
    }
    [Serializable]
    private class VolumePayload
    {
        public int volume;
    }

    // ======================
    // Apply Volume (Unity + Android System)
    // ======================
    public void ApplyVolume(int volume)
    {
        volume = Mathf.Clamp(volume, 0, 100);

        // Apply to Unity AudioListener
        if (controlUnityVolume)
        {
            ApplyUnityVolume(volume);
        }

        // Apply to Android system volume
        if (controlSystemVolume && Application.platform == RuntimePlatform.Android && !Application.isEditor)
        {
            ApplyAndroidSystemVolume(volume);
        }
    }

    private void ApplyUnityVolume(int volume)
    {
        // Convert 0-100 range to 0-1 range for Unity AudioListener
        float normalizedVolume = Mathf.Clamp01(volume / 100f);
        AudioListener.volume = normalizedVolume;

        Debug.Log($"[VOLUME][UNITY] Applied to AudioListener: {volume}% (normalized: {normalizedVolume})");

        // Optional: Also apply to specific AudioSource components if you have them
        ApplyToAudioSources(normalizedVolume);
    }

    private void ApplyAndroidSystemVolume(int volume)
    {
        if (audioManager == null)
        {
            Debug.LogError("[VOLUME][ANDROID] AudioManager not initialized");
            return;
        }

        try
        {
            // Convert 0-100 range to Android system volume range
            int androidVolume = Mathf.RoundToInt((volume / 100f) * maxSystemVolume);
            androidVolume = Mathf.Clamp(androidVolume, 0, maxSystemVolume);

            // Set the system volume
            // Parameters: streamType, index, flags
            // flags = 0 means no UI feedback, use 1 for showing volume UI
            audioManager.Call("setStreamVolume", (int)streamType, androidVolume, 0);

            Debug.Log($"[VOLUME][ANDROID] Set system volume: {volume}% -> {androidVolume}/{maxSystemVolume} for stream {streamType}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VOLUME][ANDROID] Failed to set system volume: {ex.Message}");
        }
    }

    private void ApplyToAudioSources(float normalizedVolume)
    {
        // Find all AudioSource components in the scene and apply volume
        AudioSource[] audioSources = FindObjectsOfType<AudioSource>();
        foreach (AudioSource audioSource in audioSources)
        {
            audioSource.volume = normalizedVolume;
        }

        if (audioSources.Length > 0)
        {
            Debug.Log($"[VOLUME][UNITY] Applied to {audioSources.Length} AudioSource components");
        }
    }

    // ======================
    // Android System Volume Utilities
    // ======================
    public int GetCurrentAndroidSystemVolume()
    {
        if (audioManager == null || Application.isEditor)
            return -1;

        try
        {
            int currentAndroidVolume = audioManager.Call<int>("getStreamVolume", (int)streamType);
            int volumePercentage = Mathf.RoundToInt((currentAndroidVolume / (float)maxSystemVolume) * 100);
            return volumePercentage;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VOLUME][ANDROID] Failed to get current system volume: {ex.Message}");
            return -1;
        }
    }

    public void ShowAndroidVolumeUI(int volume)
    {
        if (audioManager == null || Application.isEditor)
            return;

        try
        {
            int androidVolume = Mathf.RoundToInt((volume / 100f) * maxSystemVolume);
            // Use flag 1 to show the volume UI
            audioManager.Call("setStreamVolume", (int)streamType, androidVolume, 1);
            Debug.Log($"[VOLUME][ANDROID] Set volume with UI: {volume}%");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VOLUME][ANDROID] Failed to set volume with UI: {ex.Message}");
        }
    }

    // ======================
    // Debug Methods (Inspector)
    // ======================
    [ContextMenu("Debug Get Volume")]
    public void DebugGet()
    {
        GetVolume((success, value, err) => {
            if (success)
                Debug.Log($"[VOLUME][DEBUG] API Volume: {value}");
            else
                Debug.LogError($"[VOLUME][DEBUG] Failed: {err}");
        });

        // Also get current Android system volume
        int androidVolume = GetCurrentAndroidSystemVolume();
        if (androidVolume >= 0)
        {
            Debug.Log($"[VOLUME][DEBUG] Current Android System Volume: {androidVolume}%");
        }
    }

    [ContextMenu("Debug Set Volume To 75")]
    public void DebugSet()
    {
        SetVolume(75, (success, err) => {
            if (success)
                Debug.Log("[VOLUME][DEBUG] Set to 75 successfully");
            else
                Debug.LogError($"[VOLUME][DEBUG] Set failed: {err}");
        });
    }

    [ContextMenu("Debug Mute")]
    public void DebugMute()
    {
        SetVolume(0, (success, err) => {
            if (success)
                Debug.Log("[VOLUME][DEBUG] Muted successfully");
            else
                Debug.LogError($"[VOLUME][DEBUG] Mute failed: {err}");
        });
    }

    [ContextMenu("Debug Max Volume")]
    public void DebugMaxVolume()
    {
        SetVolume(100, (success, err) => {
            if (success)
                Debug.Log("[VOLUME][DEBUG] Max volume set successfully");
            else
                Debug.LogError($"[VOLUME][DEBUG] Max volume failed: {err}");
        });
    }

    [ContextMenu("Debug Android Volume With UI")]
    public void DebugAndroidVolumeWithUI()
    {
        ShowAndroidVolumeUI(50);
    }

    [ContextMenu("Test Direct Android Volume Control")]
    public void TestDirectAndroidVolumeControl()
    {
        Debug.Log("Testing direct Android volume control...");
        ApplyAndroidSystemVolume(75);

        int currentVol = GetCurrentAndroidSystemVolume();
        Debug.Log($"Current Android system volume after test: {currentVol}%");
    }
}