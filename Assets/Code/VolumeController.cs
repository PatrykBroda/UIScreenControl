
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

    [Header("Polling")]
    public bool autoPollVolume = true;
    public float pollIntervalSeconds = 120f; // 2 minutes

    [Header("Events")]
    public UnityEvent<int> onVolumeChanged; // subscribe in Inspector or via code

    private Coroutine volumePollCoroutine;
    private int lastVolumeValue = -1;

    void Awake()
    {
        // Hook: automatically apply volume when changed
        onVolumeChanged.AddListener(ApplyDeviceVolume);
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
            yield return GetVolumeCoroutine((success, value, err) => {
                if (success)
                {
                    if (value != lastVolumeValue)
                    {
                        Debug.Log($"[VOLUME][POLL] Changed: {lastVolumeValue} -> {value}");
                        lastVolumeValue = value;
                        onVolumeChanged?.Invoke(value);
                    }
                    else
                    {
                        Debug.Log($"[VOLUME][POLL] No change, value: {value}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[VOLUME][POLL] Failed: {err}");
                }
            });

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
    // Apply to Device (Unity AudioListener)
    // ======================
    public void ApplyDeviceVolume(int volume)
    {
        // Convert 0-100 range to 0-1 range for Unity AudioListener
        float normalizedVolume = Mathf.Clamp01(volume / 100f);
        AudioListener.volume = normalizedVolume;

        Debug.Log($"[VOLUME] Applied to AudioListener: {volume}% (normalized: {normalizedVolume})");

        // Optional: Also apply to specific AudioSource components if you have them
        ApplyToAudioSources(normalizedVolume);
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
            Debug.Log($"[VOLUME] Applied to {audioSources.Length} AudioSource components");
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
                Debug.Log($"[VOLUME][DEBUG] Volume: {value}");
            else
                Debug.LogError($"[VOLUME][DEBUG] Failed: {err}");
        });
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
}
