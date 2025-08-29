using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityAtoms.BaseAtoms;

public class DeviceSpecificImageManager : MonoBehaviour
{
    // Log tag for easy filtering
    private const string LOG_TAG = "[DeviceSpecificImageManager]";

    [Header("Debugging")]
    [Tooltip("If unchecked, ALL logs (Log/Warning/Error) from this component are suppressed.")]
    [SerializeField] private bool enableLogs = true;

    [Header("UI Toolkit Overlay")]
    public UIDocument overlayDocument;

    [Header("Render Texture Output")]
    public RenderTexture outputRenderTexture;

    [Header("Device Configuration")]
    [Tooltip("Unity Atoms StringVariable for Device ID")]
    public StringVariable deviceIdVariable;

    public string DeviceId
    {
        get
        {
            if (deviceIdVariable != null && !string.IsNullOrEmpty(deviceIdVariable.Value))
            {
                return deviceIdVariable.Value;
            }

            DbgError($"{LOG_TAG} No Device ID configured! Please assign a Unity Atoms StringVariable.");
            return "MISSING_DEVICE_ID";
        }
    }

    [Header("Settings")]
    public float pollingInterval = 2f;
    public string serverURL = "https://unity-server-control-patrykbroda.replit.app";

    [Header("Connection Reference")]
    public ConnectionManager connectionManager;

    [Header("API Media Status (Unity Atoms)")]
    [Tooltip("Unity Atoms BoolVariable - Shows if the API is currently sending an image for this device")]
    public BoolVariable apiHasImageVariable;

    [Tooltip("Unity Atoms BoolVariable - Shows if the API is currently sending a video for this device")]
    public BoolVariable apiHasVideoVariable;

    [Tooltip("Unity Atoms BoolVariable - Shows if the API response contains any media at all")]
    public BoolVariable apiHasAnyMediaVariable;

    // ===== Sticky media kind state =====
    private enum ActiveMediaKind { None, Image, Video }

    [Header("Sticky Media Flags")]
    [Tooltip("Keep image flag ON unless a video is explicitly active.")]
    [SerializeField] private bool stickyImageUntilVideo = true;

    [SerializeField] private ActiveMediaKind currentActiveKind = ActiveMediaKind.None;

    // UI Elements
    private Label statusText;
    private Label serverUrl;
    private Label userInfo;
    private Label pollingStatus;
    private Label lastUpdateTime;
    private Label currentMediaName;
    private Label mediaTypeIndicator;
    private Label deviceIdDisplay;

    private Button manualPollBtn;
    private Button togglePollingBtn;
    private Button debugTokensBtn;
    private Button clearTokensBtn;

    // State Management
    private bool isPolling = false;
    private bool isConnected = false;
    private int currentMediaId = 0;
    private Texture2D currentTexture;
    private MediaType currentMediaType = MediaType.None;

    // ===== Logging wrappers (mute all logs when enableLogs is false) =====
    private void DbgLog(string msg) { if (enableLogs) Debug.Log(msg); }
    private void DbgWarning(string msg) { if (enableLogs) Debug.LogWarning(msg); }
    private void DbgError(string msg) { if (enableLogs) Debug.LogError(msg); }
    // ====================================================================

    public enum MediaType
    {
        None,
        DeviceSpecific,
        GlobalActive
    }

    [System.Serializable]
    public class DeviceMediaResponse
    {
        public bool success;
        public MediaContainer media;
        public string mediaType; // "device-specific" or "global-active"
        public string timestamp;
        public int userId;
        public string deviceId;

        public override string ToString()
        {
            string imageId = media?.image?.id.ToString() ?? "null";
            return $"DeviceMediaResponse(success:{success}, imageId:{imageId}, type:{mediaType}, deviceId:{deviceId}, userId:{userId})";
        }
    }

    [System.Serializable]
    public class MediaContainer
    {
        public DeviceMediaInfo image;
        public VideoMediaInfo video;
    }

    [System.Serializable]
    public class DeviceMediaInfo
    {
        public int id;
        public string filename;
        public string originalName;
        public string url;
        public string mimeType;
        public long fileSize;
        public bool isActive;
        public string assignedAt; // For device-specific media
        public string activatedAt; // For global active media

        public override string ToString()
        {
            return $"DeviceMediaInfo(id:{id}, filename:{filename}, originalName:{originalName}, isActive:{isActive})";
        }
    }

    [System.Serializable]
    public class VideoMediaInfo
    {
        public int id;
        public string filename;
        public string originalName;
        public string url;
        public bool isActive;
    }

    void Start()
    {
        DbgLog($"{LOG_TAG} Start() called");

        InitializeAtomVariables();
        InitializeOverlay();
        LogDeviceIdInfo();

        if (string.IsNullOrEmpty(serverURL))
        {
            serverURL = "https://unity-server-control-patrykbroda.replit.app";
            DbgLog($"{LOG_TAG} Set default serverURL to: {serverURL}");
        }

        UpdateStatus("Initializing...", ConnectionState.Connecting);

        if (connectionManager == null)
        {
            connectionManager = FindFirstObjectByType<ConnectionManager>();
            if (connectionManager == null)
            {
                DbgWarning($"{LOG_TAG} No ConnectionManager found! Will try to start polling anyway.");
            }
        }

        StartCoroutine(WaitForConnectionThenPoll());
    }

    void InitializeAtomVariables()
    {
        DbgLog($"{LOG_TAG} === UNITY ATOMS API VARIABLES INITIALIZATION ===");

        // Initialize API status variables to false
        if (apiHasImageVariable != null)
        {
            apiHasImageVariable.Value = false;
            DbgLog($"{LOG_TAG} ✅ apiHasImageVariable initialized: {apiHasImageVariable.name}");
        }
        else
        {
            DbgWarning($"{LOG_TAG} ⚠️ apiHasImageVariable not assigned - please assign a BoolVariable in inspector");
        }

        if (apiHasVideoVariable != null)
        {
            apiHasVideoVariable.Value = false;
            DbgLog($"{LOG_TAG} ✅ apiHasVideoVariable initialized: {apiHasVideoVariable.name}");
        }
        else
        {
            DbgWarning($"{LOG_TAG} ⚠️ apiHasVideoVariable not assigned - please assign a BoolVariable in inspector");
        }

        if (apiHasAnyMediaVariable != null)
        {
            apiHasAnyMediaVariable.Value = false;
            DbgLog($"{LOG_TAG} ✅ apiHasAnyMediaVariable initialized: {apiHasAnyMediaVariable.name}");
        }
        else
        {
            DbgWarning($"{LOG_TAG} ⚠️ apiHasAnyMediaVariable not assigned - please assign a BoolVariable in inspector");
        }

        DbgLog($"{LOG_TAG} === END UNITY ATOMS API VARIABLES INITIALIZATION ===");
    }

    void LogDeviceIdInfo()
    {
        DbgLog($"{LOG_TAG} === DEVICE ID CONFIGURATION ===");
        if (deviceIdVariable != null)
        {
            DbgLog($"{LOG_TAG} ✅ Unity Atoms StringVariable found: {deviceIdVariable.name}");
            DbgLog($"{LOG_TAG}    Value: '{deviceIdVariable.Value}'");
            DbgLog($"{LOG_TAG}    Using Device ID: '{DeviceId}'");
        }
        else
        {
            DbgError($"{LOG_TAG} ❌ No Unity Atoms StringVariable assigned for Device ID!");
            DbgError($"{LOG_TAG}    Please assign a StringVariable in the inspector!");
        }
        DbgLog($"{LOG_TAG} === END DEVICE ID CONFIG ===");
    }

    private void InitializeOverlay()
    {
        DbgLog($"{LOG_TAG} Initializing UI overlay...");

        if (overlayDocument == null)
        {
            overlayDocument = GetComponent<UIDocument>();
        }

        if (overlayDocument != null)
        {
            VisualElement root = overlayDocument.rootVisualElement;

            statusText = root.Q<Label>("status-text");
            serverUrl = root.Q<Label>("server-url");
            userInfo = root.Q<Label>("user-info");
            pollingStatus = root.Q<Label>("polling-status");
            lastUpdateTime = root.Q<Label>("last-update-time");
            currentMediaName = root.Q<Label>("current-media-name");
            mediaTypeIndicator = root.Q<Label>("media-type-indicator");
            deviceIdDisplay = root.Q<Label>("device-id-display");

            manualPollBtn = root.Q<Button>("manual-poll-btn");
            togglePollingBtn = root.Q<Button>("toggle-polling-btn");
            debugTokensBtn = root.Q<Button>("debug-tokens-btn");
            clearTokensBtn = root.Q<Button>("clear-tokens-btn");

            if (manualPollBtn != null)
                manualPollBtn.clicked += ManualPoll;

            if (togglePollingBtn != null)
                togglePollingBtn.clicked += TogglePolling;

            if (debugTokensBtn != null)
                debugTokensBtn.clicked += DebugTokenSources;

            if (clearTokensBtn != null)
                clearTokensBtn.clicked += ClearAllTokens;

            if (serverUrl != null)
                serverUrl.text = $"Server: {serverURL}";

            if (deviceIdDisplay != null)
                deviceIdDisplay.text = $"Device: {DeviceId}";

            DbgLog($"{LOG_TAG} UI overlay initialized successfully");
        }
        else
        {
            DbgLog($"{LOG_TAG} No UI overlay document assigned - running without overlay");
        }

        if (outputRenderTexture != null)
        {
            DbgLog($"{LOG_TAG} RenderTexture assigned - {outputRenderTexture.width}x{outputRenderTexture.height} format:{outputRenderTexture.format}");
        }
        else
        {
            DbgWarning($"{LOG_TAG} No RenderTexture assigned - images will not be displayed!");
        }
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    private void UpdateStatus(string message, ConnectionState state)
    {
        if (statusText != null)
        {
            string stateText = state switch
            {
                ConnectionState.Connected => "🟢",
                ConnectionState.Connecting => "🟡",
                ConnectionState.Disconnected => "🔴",
                _ => "⚪"
            };
            statusText.text = $"{stateText} {message}";
        }

        DbgLog($"{LOG_TAG} Status: {message} (State: {state}, MediaType: {currentMediaType})");
    }

    private void UpdateOverlayInfo()
    {
        if (userInfo != null && connectionManager?.loginData != null)
        {
            userInfo.text = $"User: {connectionManager.loginData.UserEmail} (ID: {connectionManager.loginData.UserId})";
        }
        else if (userInfo != null)
        {
            userInfo.text = "User: Not authenticated";
        }

        if (pollingStatus != null)
        {
            pollingStatus.text = $"Polling: {(isPolling ? "Active" : "Inactive")}";
        }

        if (lastUpdateTime != null)
        {
            lastUpdateTime.text = $"Last Update: {System.DateTime.Now:HH:mm:ss}";
        }

        if (mediaTypeIndicator != null)
        {
            string typeText = currentMediaType switch
            {
                MediaType.DeviceSpecific => "🎯 Device-Specific",
                MediaType.GlobalActive => "🌐 Global Active",
                MediaType.None => "❌ No Media",
                _ => "❓ Unknown"
            };
            mediaTypeIndicator.text = $"Type: {typeText}";
        }

        if (deviceIdDisplay != null)
        {
            deviceIdDisplay.text = $"Device: {DeviceId}";
        }
    }

    private IEnumerator WaitForConnectionThenPoll()
    {
        DbgLog($"{LOG_TAG} Waiting for connection...");

        if (connectionManager != null)
        {
            while (!IsConnectionReady())
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
        else
        {
            yield return new WaitForSeconds(2f);
        }

        DbgLog($"{LOG_TAG} Connection established! Starting device-specific media polling...");
        UpdateStatus("Connected! Starting device-specific polling...", ConnectionState.Connected);

        isConnected = true;
        StartDeviceMediaPolling();
    }

    private bool IsConnectionReady()
    {
        if (connectionManager == null) return true;

        try
        {
            var connectedProperty = connectionManager.GetType().GetProperty("IsConnected");
            var authenticatedProperty = connectionManager.GetType().GetProperty("IsAuthenticated");

            if (connectedProperty != null && authenticatedProperty != null)
            {
                bool connected = (bool)connectedProperty.GetValue(connectionManager);
                bool authenticated = (bool)authenticatedProperty.GetValue(connectionManager);

                DbgLog($"{LOG_TAG} Connection status - Connected: {connected}, Authenticated: {authenticated}");
                return connected && authenticated;
            }
        }
        catch (System.Exception e)
        {
            DbgLog($"{LOG_TAG} Could not check connection status: {e.Message}");
        }

        return true;
    }

    public void StartDeviceMediaPolling()
    {
        if (isPolling)
        {
            DbgLog($"{LOG_TAG} Already polling, skipping StartDeviceMediaPolling");
            return;
        }

        DbgLog($"{LOG_TAG} StartDeviceMediaPolling() called");
        DbgLog($"{LOG_TAG} serverURL = '{serverURL}'");
        DbgLog($"{LOG_TAG} deviceId = '{DeviceId}'");

        if (string.IsNullOrEmpty(serverURL))
        {
            serverURL = "https://unity-server-control-patrykbroda.replit.app";
            DbgWarning($"{LOG_TAG} serverURL was empty! Set to default: {serverURL}");
        }

        isPolling = true;
        DbgLog($"{LOG_TAG} Started device-specific media polling every {pollingInterval} seconds");

        UpdateStatus("Polling for device-specific media...", ConnectionState.Connected);
        UpdateOverlayInfo();

        StartCoroutine(DeviceMediaPollingLoop());
    }

    public void StopDeviceMediaPolling()
    {
        DbgLog($"{LOG_TAG} Stopping device-specific media polling");
        isPolling = false;
        UpdateStatus("Polling stopped", ConnectionState.Disconnected);
        UpdateOverlayInfo();
    }

    private IEnumerator DeviceMediaPollingLoop()
    {
        DbgLog($"{LOG_TAG} DeviceMediaPollingLoop started");

        while (isPolling && isConnected)
        {
            yield return StartCoroutine(CheckForDeviceMedia());
            yield return new WaitForSeconds(pollingInterval);
        }

        DbgLog($"{LOG_TAG} DeviceMediaPollingLoop ended");
    }

    private IEnumerator CheckForDeviceMedia()
    {
        string url = $"{serverURL}/api/device/{DeviceId}/media";
        DbgLog($"{LOG_TAG} Polling device-specific URL: {url}");

        string authToken = GetAuthToken();
        if (string.IsNullOrEmpty(authToken))
        {
            DbgError($"{LOG_TAG} ❌ No auth token found!");
            UpdateStatus("No authentication token found", ConnectionState.Disconnected);
            yield break;
        }

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("Authorization", "Bearer " + authToken);

            yield return www.SendWebRequest();

            DbgLog($"{LOG_TAG} Response Code: {www.responseCode}");

            if (www.result != UnityWebRequest.Result.Success)
            {
                DbgError($"{LOG_TAG} Request failed - {www.error}");

                if (www.responseCode == 401)
                {
                    DbgError($"{LOG_TAG} Authentication failed - token may be expired");
                    UpdateStatus("Authentication expired", ConnectionState.Disconnected);
                    HandleTokenExpired();
                    yield break;
                }

                UpdateStatus($"Request failed: {www.error}", ConnectionState.Disconnected);
            }
            else
            {
                StartCoroutine(ProcessDeviceMediaResponse(www.downloadHandler.text));
            }
        }

        UpdateOverlayInfo();
    }

    private string GetAuthToken()
    {
        string authToken = "";

        if (connectionManager != null && connectionManager.loginData != null && connectionManager.loginData.IsTokenValid())
        {
            authToken = connectionManager.loginData.AuthToken;
            DbgLog($"{LOG_TAG} ✅ Using auth token from UserLoginData ScriptableObject (primary source)");
        }
        else if (!string.IsNullOrEmpty(PlayerPrefs.GetString("auth_token", "")))
        {
            authToken = PlayerPrefs.GetString("auth_token", "");
            DbgWarning($"{LOG_TAG} ⚠️ Using auth token from PlayerPrefs 'auth_token' (fallback)");

            if (connectionManager?.loginData != null)
            {
                connectionManager.loginData.SetAuthToken(authToken);
                DbgLog($"{LOG_TAG} 🔄 Restored token to ScriptableObject from PlayerPrefs");
            }
        }
        else if (!string.IsNullOrEmpty(PlayerPrefs.GetString("AuthToken", "")))
        {
            authToken = PlayerPrefs.GetString("AuthToken", "");
            DbgWarning($"{LOG_TAG} ⚠️ Using auth token from PlayerPrefs 'AuthToken' (legacy fallback)");
        }

        return authToken;
    }

    // ===== Sticky helpers =====
    private void SetAtom(BoolVariable v, bool val)
    {
        if (v != null) v.Value = val;
    }

    private void UpdateApiFlags(bool sawImageThisPoll, bool sawVideoThisPoll, bool forceClear = false)
    {
        if (forceClear)
        {
            currentActiveKind = ActiveMediaKind.None;
            SetAtom(apiHasImageVariable, false);
            SetAtom(apiHasVideoVariable, false);
            SetAtom(apiHasAnyMediaVariable, false);
            DbgLog($"{LOG_TAG} Flags cleared (force).");
            return;
        }

        // Precedence: video wins over image
        if (sawVideoThisPoll)
        {
            currentActiveKind = ActiveMediaKind.Video;
        }
        else if (sawImageThisPoll)
        {
            currentActiveKind = ActiveMediaKind.Image;
        }
        else
        {
            // No positive assertion this poll → stick to last known (if sticky), else drop to None
            if (!stickyImageUntilVideo)
            {
                currentActiveKind = ActiveMediaKind.None;
            }
        }

        bool imageActive = (currentActiveKind == ActiveMediaKind.Image);
        bool videoActive = (currentActiveKind == ActiveMediaKind.Video);
        SetAtom(apiHasImageVariable, imageActive);
        SetAtom(apiHasVideoVariable, videoActive);
        SetAtom(apiHasAnyMediaVariable, imageActive || videoActive);

        DbgLog($"{LOG_TAG} StickyFlags => Kind:{currentActiveKind}, Image:{imageActive}, Video:{videoActive}, Any:{imageActive || videoActive}");
    }
    // ==========================

    private IEnumerator ProcessDeviceMediaResponse(string rawResponse)
    {
        DbgLog($"{LOG_TAG} RAW SERVER RESPONSE: {rawResponse}");

        DeviceMediaResponse response = null;
        bool parseSuccess = false;

        try
        {
            response = JsonUtility.FromJson<DeviceMediaResponse>(rawResponse);
            parseSuccess = true;
            DbgLog($"{LOG_TAG} ✅ JSON parsing successful");
            DbgLog($"{LOG_TAG} Parsed response: {response}");

            // Debug the media structure
            if (response.media != null)
            {
                DbgLog($"{LOG_TAG} Media container found - Image: {(response.media.image != null ? "✅" : "❌")}, Video: {(response.media.video != null ? "✅" : "❌")}");
                if (response.media.image != null)
                {
                    DbgLog($"{LOG_TAG} Image details: {response.media.image}");
                }
            }
            else
            {
                DbgLog($"{LOG_TAG} No media container in response");
            }
        }
        catch (System.Exception e)
        {
            DbgError($"{LOG_TAG} JSON parsing failed: {e.Message}");
            UpdateStatus($"JSON parsing failed", ConnectionState.Disconnected);
            parseSuccess = false;
        }

        if (!parseSuccess || response == null)
        {
            DbgError($"{LOG_TAG} Could not parse server response");
            yield break;
        }

        // Observations from this poll
        bool hasImageInResponse = response.media?.image != null && response.media.image.id > 0;
        bool hasVideoInResponse = response.media?.video != null && response.media.video.id > 0;

        // Validate user ID
        int expectedUserId = 0;
        if (connectionManager?.loginData != null)
        {
            expectedUserId = connectionManager.loginData.UserId;

            if (response.userId != expectedUserId)
            {
                DbgError($"{LOG_TAG} 🚨 CRITICAL USER ID MISMATCH! Expected: {expectedUserId}, Got: {response.userId}");
                UpdateStatus("🚨 SECURITY ERROR: User ID mismatch!", ConnectionState.Disconnected);
                yield break;
            }
            else
            {
                DbgLog($"{LOG_TAG} ✅ User ID matches correctly: {expectedUserId}");
            }
        }

        // Validate device ID
        if (!string.IsNullOrEmpty(response.deviceId) && response.deviceId != DeviceId)
        {
            DbgError($"{LOG_TAG} 🚨 DEVICE ID MISMATCH! Expected: {DeviceId}, Got: {response.deviceId}");
            UpdateStatus("🚨 DEVICE ID MISMATCH!", ConnectionState.Disconnected);
            yield break;
        }

        // Determine media type
        MediaType newMediaType = MediaType.None;
        if (!string.IsNullOrEmpty(response.mediaType))
        {
            newMediaType = response.mediaType.ToLower() switch
            {
                "device-specific" => MediaType.DeviceSpecific,
                "global-active" => MediaType.GlobalActive,
                _ => MediaType.None
            };
        }
        else
        {
            DbgLog($"{LOG_TAG} No mediaType in response, assuming device-specific");
            newMediaType = MediaType.DeviceSpecific;
        }

        DbgLog($"{LOG_TAG} Detected media type: {newMediaType}");

        // Sticky flags first (so UI stays stable even if we early-exit)
        UpdateApiFlags(hasImageInResponse, hasVideoInResponse);

        // VIDEO PRECEDENCE
        if (hasVideoInResponse && response.media.video != null)
        {
            currentMediaType = newMediaType;
            currentMediaId = response.media.video.id;

            string typeDisplay = currentMediaType == MediaType.DeviceSpecific ? "Device-Specific" :
                                 currentMediaType == MediaType.GlobalActive ? "Global Active" : "Unknown";

            if (currentMediaName != null)
                currentMediaName.text = response.media.video.originalName ?? $"Video #{currentMediaId}";

            UpdateStatus($"Playing video: {currentMediaName?.text}", ConnectionState.Connected);
            UpdateOverlayInfo();
            yield break; // Don’t process image when video is active
        }

        // IMAGE HANDLING
        if (hasImageInResponse)
        {
            DeviceMediaInfo mediaInfo = response.media.image;

            if (mediaInfo.id != currentMediaId || newMediaType != currentMediaType)
            {
                DbgLog($"{LOG_TAG} New media detected!");
                DbgLog($"{LOG_TAG}   Media ID: {currentMediaId} -> {mediaInfo.id}");
                DbgLog($"{LOG_TAG}   Media Type: {currentMediaType} -> {newMediaType}");

                currentMediaType = newMediaType;
                yield return StartCoroutine(DownloadAndDisplayMedia(mediaInfo));
            }
            else
            {
                DbgLog($"{LOG_TAG} Same media as before (ID: {currentMediaId}, Type: {currentMediaType}), no download needed");

                string typeDisplay = currentMediaType switch
                {
                    MediaType.DeviceSpecific => "Device-Specific",
                    MediaType.GlobalActive => "Global Active",
                    _ => "Unknown"
                };

                UpdateStatus($"Current: {response.media.image.originalName} ({typeDisplay})", ConnectionState.Connected);
            }
        }
        else
        {
            // No positive assertion this poll — keep previous state; DO NOT clear or flip flags
            DbgLog($"{LOG_TAG} ❌ No media detected this poll — keeping previous state.");
            UpdateStatus("No media change (keeping previous)", ConnectionState.Connected);
        }

        UpdateOverlayInfo();
    }

    private IEnumerator DownloadAndDisplayMedia(DeviceMediaInfo mediaInfo)
    {
        DbgLog($"{LOG_TAG} Starting download of media: {mediaInfo}");

        string mediaUrl = $"{serverURL}{mediaInfo.url}";
        DbgLog($"{LOG_TAG} Full media URL: {mediaUrl}");

        string typeDisplay = currentMediaType switch
        {
            MediaType.DeviceSpecific => "Device-Specific",
            MediaType.GlobalActive => "Global Active",
            _ => "Unknown"
        };

        UpdateStatus($"Downloading: {mediaInfo.originalName} ({typeDisplay})...", ConnectionState.Connected);

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(mediaUrl))
        {
            yield return www.SendWebRequest();

            DbgLog($"{LOG_TAG} Download response code: {www.responseCode}");

            if (www.result != UnityWebRequest.Result.Success)
            {
                DbgError($"{LOG_TAG} Media download failed: {www.error}");
                UpdateStatus($"Download failed: {www.error}", ConnectionState.Disconnected);
            }
            else
            {
                DbgLog($"{LOG_TAG} ✅ Media download successful!");

                Texture2D downloadedTexture = ((DownloadHandlerTexture)www.downloadHandler).texture;
                DbgLog($"{LOG_TAG} Downloaded texture size: {downloadedTexture.width}x{downloadedTexture.height}");

                if (currentTexture != null)
                {
                    Destroy(currentTexture);
                }

                currentTexture = downloadedTexture;
                currentMediaId = mediaInfo.id;

                if (outputRenderTexture != null)
                {
                    CopyTextureToRenderTexture(currentTexture, outputRenderTexture);
                    DbgLog($"{LOG_TAG} ✅ RenderTexture updated successfully");
                }
                else
                {
                    DbgWarning($"{LOG_TAG} ⚠️ No RenderTexture assigned");
                }

                if (currentMediaName != null)
                {
                    currentMediaName.text = mediaInfo.originalName;
                }

                // Ensure flags reflect that image is currently active (sticky)
                UpdateApiFlags(true, false);

                UpdateStatus($"Displaying: {mediaInfo.originalName} ({typeDisplay})", ConnectionState.Connected);
                DbgLog($"{LOG_TAG} Successfully loaded and displayed media: {mediaInfo.originalName} (Type: {currentMediaType})");
            }
        }

        UpdateOverlayInfo();
    }

    private void ClearCurrentMedia()
    {
        DbgLog($"{LOG_TAG} ClearCurrentMedia() called");

        if (currentTexture != null)
        {
            Destroy(currentTexture);
            currentTexture = null;
        }

        if (outputRenderTexture != null)
        {
            ClearRenderTexture(outputRenderTexture);
            DbgLog($"{LOG_TAG} ✅ RenderTexture cleared");
        }

        if (currentMediaName != null)
        {
            currentMediaName.text = "No media";
        }

        currentMediaId = 0;
        currentMediaType = MediaType.None;

        // Reset Atoms via sticky helper
        UpdateApiFlags(false, false, forceClear: true);

        DbgLog($"{LOG_TAG} Current media cleared");
        UpdateOverlayInfo();
    }

    private void CopyTextureToRenderTexture(Texture2D sourceTexture, RenderTexture targetRenderTexture)
    {
        if (sourceTexture == null || targetRenderTexture == null)
        {
            DbgWarning($"{LOG_TAG} Cannot copy to render texture - source or target is null");
            return;
        }

        try
        {
            RenderTexture previousActive = RenderTexture.active;

            if (!targetRenderTexture.IsCreated())
            {
                targetRenderTexture.Create();
                DbgLog($"{LOG_TAG} Created RenderTexture");
            }

            RenderTexture.active = targetRenderTexture;
            GL.Clear(true, true, Color.black);

            Graphics.Blit(sourceTexture, targetRenderTexture);

            RenderTexture.active = previousActive;

            DbgLog($"{LOG_TAG} Successfully copied {sourceTexture.width}x{sourceTexture.height} texture to {targetRenderTexture.width}x{targetRenderTexture.height} RenderTexture using Blit");
        }
        catch (System.Exception e)
        {
            DbgError($"{LOG_TAG} Failed to copy texture to render texture: {e.Message}");
        }
    }

    private void ClearRenderTexture(RenderTexture renderTexture)
    {
        if (renderTexture == null) return;

        try
        {
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previousActive;
        }
        catch (System.Exception e)
        {
            DbgError($"{LOG_TAG} Failed to clear render texture: {e.Message}");
        }
    }

    private void HandleTokenExpired()
    {
        DbgLog($"{LOG_TAG} Token expired, logging out...");

        if (connectionManager?.loginData != null)
        {
            connectionManager.loginData.Logout();
        }

        PlayerPrefs.DeleteKey("auth_token");
        PlayerPrefs.DeleteKey("AuthToken");
        PlayerPrefs.Save();

        StopDeviceMediaPolling();
    }

    // Method to change device ID at runtime using Unity Atoms
    public void SetDeviceId(string newDeviceId)
    {
        if (deviceIdVariable != null)
        {
            deviceIdVariable.Value = newDeviceId;
            DbgLog($"{LOG_TAG} Device ID changed via Unity Atoms to: {newDeviceId}");

            if (deviceIdDisplay != null)
            {
                deviceIdDisplay.text = $"Device: {DeviceId}";
            }
        }
        else
        {
            DbgWarning($"{LOG_TAG} Cannot set Device ID - no Unity Atoms StringVariable assigned");
        }
    }

    // Public methods to get current API media status (for external scripts)
    public bool GetApiHasImage()
    {
        return apiHasImageVariable?.Value ?? false;
    }

    public bool GetApiHasVideo()
    {
        return apiHasVideoVariable?.Value ?? false;
    }

    public bool GetApiHasAnyMedia()
    {
        return apiHasAnyMediaVariable?.Value ?? false;
    }

    // Helper struct for getting all API status at once
    [System.Serializable]
    public struct ApiMediaStatus
    {
        public bool hasImage;
        public bool hasVideo;
        public bool hasAnyMedia;
        public MediaType currentMediaType;
        public int currentMediaId;

        public override string ToString()
        {
            return $"ApiMediaStatus(Image:{hasImage}, Video:{hasVideo}, Any:{hasAnyMedia}, Type:{currentMediaType}, ID:{currentMediaId})";
        }
    }

    public ApiMediaStatus GetApiMediaStatus()
    {
        return new ApiMediaStatus
        {
            hasImage = GetApiHasImage(),
            hasVideo = GetApiHasVideo(),
            hasAnyMedia = GetApiHasAnyMedia(),
            currentMediaType = currentMediaType,
            currentMediaId = currentMediaId
        };
    }

    void OnDestroy()
    {
        DbgLog($"{LOG_TAG} OnDestroy() called");
        StopDeviceMediaPolling();

        if (currentTexture != null)
        {
            Destroy(currentTexture);
        }

        if (outputRenderTexture != null)
        {
            ClearRenderTexture(outputRenderTexture);
        }
    }

    // UI Button Methods
    public void ManualPoll()
    {
        DbgLog($"{LOG_TAG} Manual poll triggered");
        if (isConnected)
        {
            StartCoroutine(CheckForDeviceMedia());
        }
        else
        {
            DbgWarning($"{LOG_TAG} Cannot manual poll - not connected");
        }
    }

    public void TogglePolling()
    {
        if (isPolling)
        {
            StopDeviceMediaPolling();
        }
        else
        {
            StartDeviceMediaPolling();
        }
    }

    public void OnAuthenticationRestored()
    {
        DbgLog($"{LOG_TAG} Authentication restored, restarting polling");
        if (!isPolling && isConnected)
        {
            StartDeviceMediaPolling();
        }
    }

    // Debug Methods
    [ContextMenu("Debug Token Sources")]
    public void DebugTokenSources()
    {
        DbgLog($"{LOG_TAG} === DEVICE SPECIFIC TOKEN SOURCE DEBUG ===");

        if (connectionManager != null && connectionManager.loginData != null)
        {
            DbgLog($"{LOG_TAG} ✅ ScriptableObject: {connectionManager.loginData.UserEmail} (ID: {connectionManager.loginData.UserId})");
            DbgLog($"{LOG_TAG}    Token Valid: {connectionManager.loginData.IsTokenValid()}");
        }
        else
        {
            DbgError($"{LOG_TAG} ❌ No ScriptableObject available");
        }

        string authToken1 = PlayerPrefs.GetString("auth_token", "");
        string authToken2 = PlayerPrefs.GetString("AuthToken", "");
        DbgLog($"{LOG_TAG} PlayerPrefs 'auth_token': {(authToken1.Length > 0 ? "✅ Available" : "❌ Empty")}");
        DbgLog($"{LOG_TAG} PlayerPrefs 'AuthToken': {(authToken2.Length > 0 ? "✅ Available" : "❌ Empty")}");

        DbgLog($"{LOG_TAG} === DEVICE SPECIFIC TOKEN SOURCE DEBUG END ===");
    }

    [ContextMenu("Clear All Tokens")]
    public void ClearAllTokens()
    {
        DbgLog($"{LOG_TAG} 🧹 Clearing all authentication tokens...");

        if (connectionManager?.loginData != null)
        {
            connectionManager.loginData.Logout();
        }

        PlayerPrefs.DeleteKey("auth_token");
        PlayerPrefs.DeleteKey("AuthToken");
        PlayerPrefs.Save();

        UpdateStatus("Tokens cleared - please log in", ConnectionState.Disconnected);
        DbgLog($"{LOG_TAG} 🧹 All tokens cleared");

        // Also clear sticky flags when user explicitly clears tokens
        UpdateApiFlags(false, false, forceClear: true);
    }

    [ContextMenu("Test Manual Media Request")]
    public void TestManualMediaRequest()
    {
        DbgLog($"{LOG_TAG} 🧪 Testing manual device media request...");
        StartCoroutine(CheckForDeviceMedia());
    }

    [ContextMenu("Debug API Media Status")]
    public void DebugAPIMediaStatus()
    {
        DbgLog($"{LOG_TAG} === API MEDIA STATUS DEBUG ===");
        DbgLog($"{LOG_TAG} API Has Image: {(apiHasImageVariable?.Value == true ? "✅ YES" : "❌ NO")} (Variable: {(apiHasImageVariable != null ? "✅ Assigned" : "❌ Missing")})");
        DbgLog($"{LOG_TAG} API Has Video: {(apiHasVideoVariable?.Value == true ? "✅ YES" : "❌ NO")} (Variable: {(apiHasVideoVariable != null ? "✅ Assigned" : "❌ Missing")})");
        DbgLog($"{LOG_TAG} API Has Any Media: {(apiHasAnyMediaVariable?.Value == true ? "✅ YES" : "❌ NO")} (Variable: {(apiHasAnyMediaVariable != null ? "✅ Assigned" : "❌ Missing")})");
        DbgLog($"{LOG_TAG} Current Media Type: {currentMediaType}");
        DbgLog($"{LOG_TAG} Current Media ID: {currentMediaId}");
        DbgLog($"{LOG_TAG} === API MEDIA STATUS DEBUG END ===");
    }

    [ContextMenu("Debug System Status")]
    public void DebugSystemStatus()
    {
        DbgLog($"{LOG_TAG} === DEVICE SPECIFIC SYSTEM STATUS DEBUG ===");
        DbgLog($"{LOG_TAG} Device ID: '{DeviceId}'");
        DbgLog($"{LOG_TAG} RenderTexture: {(outputRenderTexture != null ? "✅ Assigned" : "❌ Missing")}");
        DbgLog($"{LOG_TAG} UI Overlay: {(overlayDocument != null ? "✅ Assigned" : "❌ Missing")}");
        DbgLog($"{LOG_TAG} Current texture: {(currentTexture != null ? "✅ Loaded" : "❌ None")}");
        DbgLog($"{LOG_TAG} Current media ID: {currentMediaId}");
        DbgLog($"{LOG_TAG} Current media type: {currentMediaType}");
        DbgLog($"{LOG_TAG} Polling: {(isPolling ? "✅ Active" : "❌ Inactive")}");
        DbgLog($"{LOG_TAG} Connected: {(isConnected ? "✅ Yes" : "❌ No")}");
        DbgLog($"{LOG_TAG} === DEVICE SPECIFIC SYSTEM STATUS DEBUG END ===");
    }

    [ContextMenu("Force Device Registration Check")]
    public void ForceDeviceRegistrationCheck()
    {
        DbgLog($"{LOG_TAG} 🔧 CHECKING DEVICE REGISTRATION...");

        if (connectionManager != null)
        {
            try
            {
                var method = connectionManager.GetType().GetMethod("DebugConnectionState");
                if (method != null)
                {
                    method.Invoke(connectionManager, null);
                }
            }
            catch (System.Exception e)
            {
                DbgWarning($"{LOG_TAG} Could not call DebugConnectionState: {e.Message}");
            }
        }

        DebugSystemStatus();
    }
}
