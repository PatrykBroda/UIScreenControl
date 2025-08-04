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

    [Header("Polling")]
    public bool autoPollBrightness = true;
    public float pollIntervalSeconds = 120f; // 2 minutes

    [Header("Events")]
    public UnityEvent<int> onBrightnessChanged; // subscribe in Inspector or via code

    private Coroutine brightnessPollCoroutine;
    private int lastBrightnessValue = -1;

    void Awake()
    {
        // Hook: automatically apply brightness when changed
        onBrightnessChanged.AddListener(ApplyDeviceBrightness);
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
                        onBrightnessChanged?.Invoke(value);
                    }
                    else
                    {
                        Debug.Log($"[BRIGHTNESS][POLL] No change, value: {value}");
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
    // Apply to Device (Android only)
    // ======================
    public void ApplyDeviceBrightness(int brightness)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        SetAndroidScreenBrightness(Mathf.Clamp01(brightness / 100f));
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // Set screen brightness for your app's window (Android only)
    public static void SetAndroidScreenBrightness(float brightness01)
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                var window = activity.Call<AndroidJavaObject>("getWindow");
                var layoutParams = window.Call<AndroidJavaObject>("getAttributes");
                layoutParams.Set("screenBrightness", brightness01); // 0.0 - 1.0
                window.Call("setAttributes", layoutParams);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[ANDROID BRIGHTNESS] Failed: " + e);
        }
    }
#endif

    // ======================
    // Debug Methods (Inspector)
    // ======================
    [ContextMenu("Debug Get Brightness")]
    public void DebugGet()
    {
        GetBrightness((success, value, err) => {
            if (success)
                Debug.Log($"[BRIGHTNESS][DEBUG] Brightness: {value}");
            else
                Debug.LogError($"[BRIGHTNESS][DEBUG] Failed: {err}");
        });
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
}
