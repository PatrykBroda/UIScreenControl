using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.Video;
using UnityAtoms.BaseAtoms;

public class DeviceSpecificVideoManager : MonoBehaviour
{
    // Log tag for easy filtering
    private const string LOG_TAG = "[DeviceSpecificVideoManager]";

    [Header("Video Player")]
    public VideoPlayer videoPlayer;

    [Header("UI Toolkit Overlay")]
    public UIDocument overlayDocument;

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

            Debug.LogError($"{LOG_TAG} No Device ID configured! Please assign a Unity Atoms StringVariable.");
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

    // UI Elements
    private Label statusText;
    private Label serverUrl;
    private Label userInfo;
    private Label pollingStatus;
    private Label lastUpdateTime;
    private Label currentVideoName;
    private Label videoInfo;
    private Label mediaTypeIndicator;
    private Label deviceIdDisplay;

    private Button manualPollBtn;
    private Button togglePollingBtn;
    private Button debugTokensBtn;
    private Button clearTokensBtn;
    private Button playPauseBtn;

    // State Management
    private bool isPolling = false;
    private bool isConnected = false;
    private bool isLoadingVideo = false; // Prevent concurrent video loading
    private int currentVideoId = 0;
    private string currentVideoUrl = "";
    private int lastRequestedVideoId = 0; // Track what we last requested
    private MediaType currentMediaType = MediaType.None;
    private Coroutine currentVideoLoadCoroutine; // Track active video loading

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
            string videoId = media?.video?.id.ToString() ?? "null";
            return $"DeviceMediaResponse(success:{success}, videoId:{videoId}, type:{mediaType}, deviceId:{deviceId}, userId:{userId})";
        }
    }

    [System.Serializable]
    public class MediaContainer
    {
        public ImageMediaInfo image;
        public DeviceVideoInfo video;
    }

    [System.Serializable]
    public class ImageMediaInfo
    {
        public int id;
        public string filename;
        public string originalName;
        public string url;
        public bool isActive;
    }

    [System.Serializable]
    public class DeviceVideoInfo
    {
        public int id;
        public string filename;
        public string originalName;
        public string url;
        public string mimeType;
        public long fileSize;
        public bool isActive;
        public string duration;
        public string assignedAt; // For device-specific media
        public string activatedAt; // For global active media

        public override string ToString()
        {
            return $"DeviceVideoInfo(id:{id}, filename:{filename}, originalName:{originalName}, isActive:{isActive})";
        }
    }

    void Start()
    {
        Debug.Log($"{LOG_TAG} Start() called");

        InitializeAtomVariables();
        InitializeVideoPlayer();
        InitializeOverlay();
        LogDeviceIdInfo();

        if (string.IsNullOrEmpty(serverURL))
        {
            serverURL = "https://unity-server-control-patrykbroda.replit.app";
            Debug.Log($"{LOG_TAG} Set default serverURL to: {serverURL}");
        }

        UpdateStatus("Initializing...", ConnectionState.Connecting);

        if (connectionManager == null)
        {
            connectionManager = FindFirstObjectByType<ConnectionManager>();
            if (connectionManager == null)
            {
                Debug.LogWarning($"{LOG_TAG} No ConnectionManager found! Will try to start polling anyway.");
            }
        }

        StartCoroutine(WaitForConnectionThenPoll());
    }

    void InitializeAtomVariables()
    {
        Debug.Log($"{LOG_TAG} === UNITY ATOMS API VARIABLES INITIALIZATION ===");

        // Initialize API status variables to false
        if (apiHasImageVariable != null)
        {
            apiHasImageVariable.Value = false;
            Debug.Log($"{LOG_TAG} ✅ apiHasImageVariable initialized: {apiHasImageVariable.name}");
        }
        else
        {
            Debug.LogWarning($"{LOG_TAG} ⚠️ apiHasImageVariable not assigned - please assign a BoolVariable in inspector");
        }

        if (apiHasVideoVariable != null)
        {
            apiHasVideoVariable.Value = false;
            Debug.Log($"{LOG_TAG} ✅ apiHasVideoVariable initialized: {apiHasVideoVariable.name}");
        }
        else
        {
            Debug.LogWarning($"{LOG_TAG} ⚠️ apiHasVideoVariable not assigned - please assign a BoolVariable in inspector");
        }

        if (apiHasAnyMediaVariable != null)
        {
            apiHasAnyMediaVariable.Value = false;
            Debug.Log($"{LOG_TAG} ✅ apiHasAnyMediaVariable initialized: {apiHasAnyMediaVariable.name}");
        }
        else
        {
            Debug.LogWarning($"{LOG_TAG} ⚠️ apiHasAnyMediaVariable not assigned - please assign a BoolVariable in inspector");
        }

        Debug.Log($"{LOG_TAG} === END UNITY ATOMS API VARIABLES INITIALIZATION ===");
    }

    void LogDeviceIdInfo()
    {
        Debug.Log($"{LOG_TAG} === DEVICE ID CONFIGURATION ===");
        if (deviceIdVariable != null)
        {
            Debug.Log($"{LOG_TAG} ✅ Unity Atoms StringVariable found: {deviceIdVariable.name}");
            Debug.Log($"{LOG_TAG}    Value: '{deviceIdVariable.Value}'");
            Debug.Log($"{LOG_TAG}    Using Device ID: '{DeviceId}'");
        }
        else
        {
            Debug.LogError($"{LOG_TAG} ❌ No Unity Atoms StringVariable assigned for Device ID!");
            Debug.LogError($"{LOG_TAG}    Please assign a StringVariable in the inspector!");
        }
        Debug.Log($"{LOG_TAG} === END DEVICE ID CONFIG ===");
    }

    private void InitializeVideoPlayer()
    {
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                Debug.LogError($"{LOG_TAG} No VideoPlayer component found! Please assign one.");
                return;
            }
        }

        // Configure video player for safe operation
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        videoPlayer.skipOnDrop = true;
        videoPlayer.waitForFirstFrame = true;

        // Subscribe to video player events
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.started += OnVideoStarted;
        videoPlayer.loopPointReached += OnVideoLoopCompleted;

        Debug.Log($"{LOG_TAG} VideoPlayer initialized - Render Mode: {videoPlayer.renderMode}");
    }

    private void InitializeOverlay()
    {
        Debug.Log($"{LOG_TAG} Initializing UI overlay...");

        if (overlayDocument == null)
        {
            overlayDocument = GetComponent<UIDocument>();
        }

        if (overlayDocument != null)
        {
            VisualElement root = overlayDocument.rootVisualElement;

            statusText = root.Q<Label>("video-status-text");
            serverUrl = root.Q<Label>("video-server-url");
            userInfo = root.Q<Label>("video-user-info");
            pollingStatus = root.Q<Label>("video-polling-status");
            lastUpdateTime = root.Q<Label>("video-last-update-time");
            currentVideoName = root.Q<Label>("current-video-name");
            videoInfo = root.Q<Label>("video-info");
            mediaTypeIndicator = root.Q<Label>("video-media-type-indicator");
            deviceIdDisplay = root.Q<Label>("video-device-id-display");

            manualPollBtn = root.Q<Button>("video-manual-poll-btn");
            togglePollingBtn = root.Q<Button>("video-toggle-polling-btn");
            debugTokensBtn = root.Q<Button>("video-debug-tokens-btn");
            clearTokensBtn = root.Q<Button>("video-clear-tokens-btn");
            playPauseBtn = root.Q<Button>("video-play-pause-btn");

            if (manualPollBtn != null)
                manualPollBtn.clicked += ManualPoll;

            if (togglePollingBtn != null)
                togglePollingBtn.clicked += TogglePolling;

            if (debugTokensBtn != null)
                debugTokensBtn.clicked += DebugTokenSources;

            if (clearTokensBtn != null)
                clearTokensBtn.clicked += ClearAllTokens;

            if (playPauseBtn != null)
                playPauseBtn.clicked += TogglePlayPause;

            if (serverUrl != null)
                serverUrl.text = $"Server: {serverURL}";

            if (deviceIdDisplay != null)
                deviceIdDisplay.text = $"Device: {DeviceId}";

            Debug.Log($"{LOG_TAG} UI overlay initialized successfully");
        }
        else
        {
            Debug.Log($"{LOG_TAG} No UI overlay document assigned - running without overlay");
        }

        if (videoPlayer != null)
        {
            Debug.Log($"{LOG_TAG} VideoPlayer assigned - Render Mode: {videoPlayer.renderMode}");
        }
        else
        {
            Debug.LogWarning($"{LOG_TAG} No VideoPlayer assigned - videos will not be displayed!");
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

        Debug.Log($"{LOG_TAG} Status: {message} (State: {state}, MediaType: {currentMediaType}, Loading: {isLoadingVideo})");
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
            lastUpdateTime.text = $"Last Update: {System.DateTime.Now.ToString("HH:mm:ss")}";
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

        if (videoInfo != null && videoPlayer != null)
        {
            string loadingStatus = isLoadingVideo ? " (Loading...)" : "";
            string playerStatus = videoPlayer.isPlaying ? "Playing" : (videoPlayer.isPrepared ? "Ready" : "Stopped");
            videoInfo.text = $"Player: {playerStatus}{loadingStatus}";
        }

        if (playPauseBtn != null && videoPlayer != null)
        {
            playPauseBtn.text = videoPlayer.isPlaying ? "Pause" : "Play";
        }
    }

    private IEnumerator WaitForConnectionThenPoll()
    {
        Debug.Log($"{LOG_TAG} Waiting for connection...");

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

        Debug.Log($"{LOG_TAG} Connection established! Starting device-specific video polling...");
        UpdateStatus("Connected! Starting device-specific video polling...", ConnectionState.Connected);

        isConnected = true;
        StartDeviceVideoPolling();
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
                bool isConnected = (bool)connectedProperty.GetValue(connectionManager);
                bool isAuthenticated = (bool)authenticatedProperty.GetValue(connectionManager);

                Debug.Log($"{LOG_TAG} Connection status - Connected: {isConnected}, Authenticated: {isAuthenticated}");
                return isConnected && isAuthenticated;
            }
        }
        catch (System.Exception e)
        {
            Debug.Log($"{LOG_TAG} Could not check connection status: {e.Message}");
        }

        return true;
    }

    public void StartDeviceVideoPolling()
    {
        if (isPolling)
        {
            Debug.Log($"{LOG_TAG} Already polling, skipping StartDeviceVideoPolling");
            return;
        }

        Debug.Log($"{LOG_TAG} StartDeviceVideoPolling() called");
        Debug.Log($"{LOG_TAG} serverURL = '{serverURL}'");
        Debug.Log($"{LOG_TAG} deviceId = '{DeviceId}'");

        if (string.IsNullOrEmpty(serverURL))
        {
            serverURL = "https://unity-server-control-patrykbroda.replit.app";
            Debug.LogWarning($"{LOG_TAG} serverURL was empty! Set to default: {serverURL}");
        }

        isPolling = true;
        Debug.Log($"{LOG_TAG} Started device-specific video polling every {pollingInterval} seconds");

        UpdateStatus("Polling for device-specific videos...", ConnectionState.Connected);
        UpdateOverlayInfo();

        StartCoroutine(DeviceVideoPollingLoop());
    }

    public void StopDeviceVideoPolling()
    {
        Debug.Log($"{LOG_TAG} Stopping device-specific video polling");
        isPolling = false;
        UpdateStatus("Polling stopped", ConnectionState.Disconnected);
        UpdateOverlayInfo();
    }

    private IEnumerator DeviceVideoPollingLoop()
    {
        Debug.Log($"{LOG_TAG} DeviceVideoPollingLoop started");

        while (isPolling && isConnected)
        {
            // Only poll if we're not currently loading a video
            if (!isLoadingVideo)
            {
                yield return StartCoroutine(CheckForDeviceVideo());
            }
            else
            {
                Debug.Log($"{LOG_TAG} Skipping poll - video loading in progress");
            }

            yield return new WaitForSeconds(pollingInterval);
        }

        Debug.Log($"{LOG_TAG} DeviceVideoPollingLoop ended");
    }

    private IEnumerator CheckForDeviceVideo()
    {
        string url = $"{serverURL}/api/device/{DeviceId}/media";
        Debug.Log($"{LOG_TAG} Polling device-specific URL: {url}");

        string authToken = GetAuthToken();
        if (string.IsNullOrEmpty(authToken))
        {
            Debug.LogError($"{LOG_TAG} ❌ No auth token found!");
            UpdateStatus("No authentication token found", ConnectionState.Disconnected);
            yield break;
        }

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("Authorization", "Bearer " + authToken);
            www.SetRequestHeader("Cache-Control", "no-cache");

            yield return www.SendWebRequest();

            Debug.Log($"{LOG_TAG} Response Code: {www.responseCode}");

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{LOG_TAG} Request failed - {www.error}");

                if (www.responseCode == 401)
                {
                    Debug.LogError($"{LOG_TAG} Authentication failed - token may be expired");
                    UpdateStatus("Authentication expired", ConnectionState.Disconnected);
                    HandleTokenExpired();
                    yield break;
                }

                UpdateStatus($"Request failed: {www.error}", ConnectionState.Disconnected);
            }
            else
            {
                StartCoroutine(ProcessDeviceVideoResponse(www.downloadHandler.text));
            }
        }

        UpdateOverlayInfo();
    }

    private string GetAuthToken()
    {
        if (connectionManager != null && connectionManager.loginData != null && connectionManager.loginData.IsTokenValid())
        {
            return connectionManager.loginData.AuthToken;
        }
        else if (!string.IsNullOrEmpty(PlayerPrefs.GetString("auth_token", "")))
        {
            string token = PlayerPrefs.GetString("auth_token", "");
            if (connectionManager?.loginData != null)
            {
                connectionManager.loginData.SetAuthToken(token);
            }
            return token;
        }
        else if (!string.IsNullOrEmpty(PlayerPrefs.GetString("AuthToken", "")))
        {
            return PlayerPrefs.GetString("AuthToken", "");
        }

        return "";
    }

    private IEnumerator ProcessDeviceVideoResponse(string rawResponse)
    {
        Debug.Log($"{LOG_TAG} RAW SERVER RESPONSE: {rawResponse}");

        DeviceMediaResponse response = null;
        try
        {
            response = JsonUtility.FromJson<DeviceMediaResponse>(rawResponse);
            Debug.Log($"{LOG_TAG} ✅ JSON parsing successful");
            Debug.Log($"{LOG_TAG} Parsed response: {response}");

            // Debug the media structure
            if (response.media != null)
            {
                Debug.Log($"{LOG_TAG} Media container found - Image: {(response.media.image != null ? "✅" : "❌")}, Video: {(response.media.video != null ? "✅" : "❌")}");
                if (response.media.video != null)
                {
                    Debug.Log($"{LOG_TAG} Video details: {response.media.video}");
                }
            }
            else
            {
                Debug.Log($"{LOG_TAG} No media container in response");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LOG_TAG} JSON parsing failed: {e.Message}");
            UpdateStatus($"JSON parsing failed", ConnectionState.Disconnected);
            yield break;
        }

        if (response == null)
        {
            Debug.LogError($"{LOG_TAG} Could not parse server response");
            yield break;
        }

        // Update API status indicators for inspector visibility
        bool hasImageInResponse = response.media?.image != null && response.media.image.id > 0;
        bool hasVideoInResponse = response.media?.video != null && response.media.video.id > 0;

        // Update Unity Atoms BoolVariables
        if (apiHasImageVariable != null)
            apiHasImageVariable.Value = hasImageInResponse;

        if (apiHasVideoVariable != null)
            apiHasVideoVariable.Value = hasVideoInResponse;

        if (apiHasAnyMediaVariable != null)
            apiHasAnyMediaVariable.Value = hasImageInResponse || hasVideoInResponse;

        Debug.Log($"{LOG_TAG} API Media Status - Image: {(hasImageInResponse ? "✅" : "❌")}, Video: {(hasVideoInResponse ? "✅" : "❌")}, Any: {(hasImageInResponse || hasVideoInResponse ? "✅" : "❌")}");

        // Validate user ID
        if (connectionManager?.loginData != null)
        {
            int expectedUserId = connectionManager.loginData.UserId;

            if (response.userId != expectedUserId)
            {
                Debug.LogError($"{LOG_TAG} 🚨 CRITICAL USER ID MISMATCH! Expected: {expectedUserId}, Got: {response.userId}");
                UpdateStatus("🚨 SECURITY ERROR: User ID mismatch!", ConnectionState.Disconnected);
                yield break;
            }
            else
            {
                Debug.Log($"{LOG_TAG} ✅ User ID matches correctly: {expectedUserId}");
            }
        }

        // Validate device ID
        if (!string.IsNullOrEmpty(response.deviceId) && response.deviceId != DeviceId)
        {
            Debug.LogError($"{LOG_TAG} 🚨 DEVICE ID MISMATCH! Expected: {DeviceId}, Got: {response.deviceId}");
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
            // If no mediaType specified, assume device-specific since we're polling device endpoint
            Debug.Log($"{LOG_TAG} No mediaType in response, assuming device-specific");
            newMediaType = MediaType.DeviceSpecific;
        }

        Debug.Log($"{LOG_TAG} Detected media type: {newMediaType}");

        bool hasVideo = response.media?.video != null && response.media.video.id > 0;

        if (hasVideo)
        {
            DeviceVideoInfo videoInfo = response.media.video;

            // Check if this is a different video AND we're not already loading it
            if (videoInfo.id != currentVideoId && videoInfo.id != lastRequestedVideoId && !isLoadingVideo)
            {
                Debug.Log($"{LOG_TAG} New video detected!");
                Debug.Log($"{LOG_TAG}   Video ID: {currentVideoId} -> {videoInfo.id}");
                Debug.Log($"{LOG_TAG}   Media Type: {currentMediaType} -> {newMediaType}");

                lastRequestedVideoId = videoInfo.id; // Track what we're about to request
                currentMediaType = newMediaType;

                // Cancel any existing video load operation
                if (currentVideoLoadCoroutine != null)
                {
                    StopCoroutine(currentVideoLoadCoroutine);
                    currentVideoLoadCoroutine = null;
                }

                currentVideoLoadCoroutine = StartCoroutine(LoadAndPlayVideoSafely(videoInfo));
            }
            else if (videoInfo.id == currentVideoId)
            {
                Debug.Log($"{LOG_TAG} Same video as before (ID: {currentVideoId}, Type: {currentMediaType}), no loading needed");

                string typeDisplay = currentMediaType switch
                {
                    MediaType.DeviceSpecific => "Device-Specific",
                    MediaType.GlobalActive => "Global Active",
                    _ => "Unknown"
                };

                UpdateStatus($"Current: {videoInfo.originalName} ({typeDisplay})", ConnectionState.Connected);
            }
            else if (isLoadingVideo)
            {
                Debug.Log($"{LOG_TAG} Video loading in progress, skipping new request");
            }
        }
        else
        {
            Debug.Log($"{LOG_TAG} ❌ No video detected");
            UpdateStatus("No video assigned", ConnectionState.Connected);

            if (currentVideoName != null)
            {
                currentVideoName.text = "No video";
            }

            currentMediaType = MediaType.None;

            if (currentVideoId > 0)
            {
                Debug.Log($"{LOG_TAG} Clearing previous video");
                yield return StartCoroutine(ClearCurrentVideoSafely());
            }
        }

        UpdateOverlayInfo();
    }

    // Safe video loading with proper cleanup and error handling
    private IEnumerator LoadAndPlayVideoSafely(DeviceVideoInfo videoInfo)
    {
        if (isLoadingVideo)
        {
            Debug.LogWarning($"{LOG_TAG} Already loading a video, skipping new request");
            yield break;
        }

        isLoadingVideo = true;
        Debug.Log($"{LOG_TAG} Starting SAFE load of video: {videoInfo}");

        if (videoPlayer == null)
        {
            Debug.LogError($"{LOG_TAG} No VideoPlayer assigned!");
            UpdateStatus("No VideoPlayer assigned", ConnectionState.Disconnected);
            isLoadingVideo = false;
            currentVideoLoadCoroutine = null;
            yield break;
        }

        // Validate URL first
        string videoUrl = $"{serverURL}{videoInfo.url}";
        Debug.Log($"{LOG_TAG} Full video URL: {videoUrl}");

        if (!IsValidVideoUrl(videoUrl))
        {
            Debug.LogError($"{LOG_TAG} Invalid video URL: {videoUrl}");
            UpdateStatus("Invalid video URL", ConnectionState.Disconnected);
            isLoadingVideo = false;
            currentVideoLoadCoroutine = null;
            yield break;
        }

        string typeDisplay = currentMediaType switch
        {
            MediaType.DeviceSpecific => "Device-Specific",
            MediaType.GlobalActive => "Global Active",
            _ => "Unknown"
        };

        UpdateStatus($"Loading: {videoInfo.originalName} ({typeDisplay})...", ConnectionState.Connected);

        // Stop current video completely
        yield return StartCoroutine(StopCurrentVideoCompletely());

        // Wait before setting new URL to avoid conflicts
        yield return new WaitForSeconds(0.3f);

        // Set new video
        videoPlayer.url = videoUrl;
        currentVideoUrl = videoUrl;

        // Update UI immediately
        if (currentVideoName != null)
        {
            currentVideoName.text = videoInfo.originalName;
        }

        UpdateVideoInfoDisplay(videoInfo);

        Debug.Log($"{LOG_TAG} Preparing video...");
        videoPlayer.Prepare();

        // Better preparation waiting with timeout
        float timeout = 20f;
        float checkInterval = 0.1f;
        float timer = 0f;

        while (!videoPlayer.isPrepared && timer < timeout)
        {
            timer += checkInterval;
            yield return new WaitForSeconds(checkInterval);

            // Update UI during loading
            if (timer % 1f < checkInterval) // Update every second
            {
                UpdateStatus($"Loading: {videoInfo.originalName} ({typeDisplay})... ({timer:F0}s)", ConnectionState.Connected);
                UpdateOverlayInfo();
            }
        }

        // Check if preparation was successful
        if (!videoPlayer.isPrepared)
        {
            Debug.LogError($"{LOG_TAG} Video preparation timed out!");
            UpdateStatus($"Failed to load: {videoInfo.originalName} (timeout)", ConnectionState.Disconnected);
            yield return StartCoroutine(StopCurrentVideoCompletely());
            isLoadingVideo = false;
            currentVideoLoadCoroutine = null;
            yield break;
        }

        Debug.Log($"{LOG_TAG} ✅ Video prepared successfully, starting playback");

        // Small delay before playing to ensure everything is ready
        yield return new WaitForSeconds(0.1f);

        videoPlayer.Play();
        currentVideoId = videoInfo.id;

        UpdateStatus($"Playing: {videoInfo.originalName} ({typeDisplay})", ConnectionState.Connected);
        UpdateOverlayInfo();

        Debug.Log($"{LOG_TAG} Successfully loaded and started video: {videoInfo.originalName} (Type: {currentMediaType})");

        // Clean up loading state
        isLoadingVideo = false;
        currentVideoLoadCoroutine = null;
    }

    // Comprehensive video stopping
    private IEnumerator StopCurrentVideoCompletely()
    {
        if (videoPlayer == null) yield break;

        Debug.Log($"{LOG_TAG} Stopping current video completely...");

        // Stop if playing
        if (videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
            yield return new WaitForSeconds(0.1f);
        }

        // Clear URL to release resources
        videoPlayer.url = "";
        yield return new WaitForEndOfFrame();

        Debug.Log($"{LOG_TAG} Video stopped and resources cleared");
    }

    // Safe video clearing
    private IEnumerator ClearCurrentVideoSafely()
    {
        Debug.Log($"{LOG_TAG} ClearCurrentVideoSafely() called");

        yield return StartCoroutine(StopCurrentVideoCompletely());

        if (currentVideoName != null)
        {
            currentVideoName.text = "No video";
        }

        if (videoInfo != null)
        {
            videoInfo.text = "No video loaded";
        }

        currentVideoId = 0;
        currentVideoUrl = "";
        lastRequestedVideoId = 0;
        currentMediaType = MediaType.None;

        // Reset Unity Atoms BoolVariables
        if (apiHasImageVariable != null)
            apiHasImageVariable.Value = false;

        if (apiHasVideoVariable != null)
            apiHasVideoVariable.Value = false;

        if (apiHasAnyMediaVariable != null)
            apiHasAnyMediaVariable.Value = false;

        Debug.Log($"{LOG_TAG} Current video cleared safely");
        UpdateOverlayInfo();
    }

    // URL validation
    private bool IsValidVideoUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        try
        {
            System.Uri uri = new System.Uri(url);
            return uri.Scheme == "http" || uri.Scheme == "https";
        }
        catch
        {
            return false;
        }
    }

    private void UpdateVideoInfoDisplay(DeviceVideoInfo videoInfo)
    {
        if (videoInfo != null && this.videoInfo != null)
        {
            string sizeText = FormatFileSize(videoInfo.fileSize);
            string durationText = !string.IsNullOrEmpty(videoInfo.duration) ? videoInfo.duration : "Unknown";
            this.videoInfo.text = $"Size: {sizeText} | Duration: {durationText} | Type: {videoInfo.mimeType}";
        }
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes == 0) return "0 B";

        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:F1} {sizes[order]}";
    }

    private void HandleTokenExpired()
    {
        Debug.Log($"{LOG_TAG} Token expired, logging out...");

        if (connectionManager?.loginData != null)
        {
            connectionManager.loginData.Logout();
        }

        PlayerPrefs.DeleteKey("auth_token");
        PlayerPrefs.DeleteKey("AuthToken");
        PlayerPrefs.Save();

        StopDeviceVideoPolling();
    }

    // Video Player Event Handlers
    private void OnVideoPrepared(VideoPlayer source)
    {
        Debug.Log($"{LOG_TAG} Video prepared successfully - Duration: {source.length:F1}s");
        UpdateOverlayInfo();
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"{LOG_TAG} Video error - {message}");
        UpdateStatus($"Video error: {message}", ConnectionState.Disconnected);

        // Clear loading state on error
        isLoadingVideo = false;
        currentVideoLoadCoroutine = null;
    }

    private void OnVideoStarted(VideoPlayer source)
    {
        Debug.Log($"{LOG_TAG} Video playback started");
        UpdateOverlayInfo();
    }

    private void OnVideoLoopCompleted(VideoPlayer source)
    {
        Debug.Log($"{LOG_TAG} Video loop completed");
    }

    // Method to change device ID at runtime using Unity Atoms
    public void SetDeviceId(string newDeviceId)
    {
        if (deviceIdVariable != null)
        {
            deviceIdVariable.Value = newDeviceId;
            Debug.Log($"{LOG_TAG} Device ID changed via Unity Atoms to: {newDeviceId}");

            if (deviceIdDisplay != null)
            {
                deviceIdDisplay.text = $"Device: {DeviceId}";
            }
        }
        else
        {
            Debug.LogWarning($"{LOG_TAG} Cannot set Device ID - no Unity Atoms StringVariable assigned");
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
        public int currentVideoId;

        public override string ToString()
        {
            return $"ApiMediaStatus(Image:{hasImage}, Video:{hasVideo}, Any:{hasAnyMedia}, Type:{currentMediaType}, VideoID:{currentVideoId})";
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
            currentVideoId = currentVideoId
        };
    }

    void OnDestroy()
    {
        Debug.Log($"{LOG_TAG} OnDestroy() called");
        StopDeviceVideoPolling();

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.started -= OnVideoStarted;
            videoPlayer.loopPointReached -= OnVideoLoopCompleted;

            videoPlayer.Stop();
        }

        // Clean up coroutines
        if (currentVideoLoadCoroutine != null)
        {
            StopCoroutine(currentVideoLoadCoroutine);
        }
    }

    // UI Button Methods
    public void ManualPoll()
    {
        Debug.Log($"{LOG_TAG} Manual poll triggered");
        if (isConnected && !isLoadingVideo)
        {
            StartCoroutine(CheckForDeviceVideo());
        }
        else if (isLoadingVideo)
        {
            Debug.LogWarning($"{LOG_TAG} Cannot manual poll - video loading in progress");
        }
        else
        {
            Debug.LogWarning($"{LOG_TAG} Cannot manual poll - not connected");
        }
    }

    public void TogglePolling()
    {
        if (isPolling)
        {
            StopDeviceVideoPolling();
        }
        else
        {
            StartDeviceVideoPolling();
        }
    }

    public void TogglePlayPause()
    {
        if (videoPlayer == null) return;

        if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
            Debug.Log($"{LOG_TAG} Video paused");
        }
        else if (videoPlayer.isPrepared)
        {
            videoPlayer.Play();
            Debug.Log($"{LOG_TAG} Video resumed");
        }
        else
        {
            Debug.LogWarning($"{LOG_TAG} Video not prepared for playback");
        }

        UpdateOverlayInfo();
    }

    public void OnAuthenticationRestored()
    {
        Debug.Log($"{LOG_TAG} Authentication restored, restarting polling");
        if (!isPolling && isConnected)
        {
            StartDeviceVideoPolling();
        }
    }

    // Debug Methods
    [ContextMenu("Emergency Stop All Video Operations")]
    public void EmergencyStopAllVideoOperations()
    {
        Debug.Log($"{LOG_TAG} 🚨 EMERGENCY STOP: Stopping all video operations");

        isLoadingVideo = false;

        if (currentVideoLoadCoroutine != null)
        {
            StopCoroutine(currentVideoLoadCoroutine);
            currentVideoLoadCoroutine = null;
        }

        StartCoroutine(StopCurrentVideoCompletely());

        currentVideoId = 0;
        currentVideoUrl = "";
        lastRequestedVideoId = 0;
        currentMediaType = MediaType.None;

        UpdateStatus("Emergency stop completed", ConnectionState.Disconnected);
        Debug.Log($"{LOG_TAG} 🚨 Emergency stop completed");
    }

    [ContextMenu("Debug Token Sources")]
    public void DebugTokenSources()
    {
        Debug.Log($"{LOG_TAG} === DEVICE SPECIFIC VIDEO TOKEN SOURCE DEBUG ===");

        if (connectionManager != null && connectionManager.loginData != null)
        {
            Debug.Log($"{LOG_TAG} ✅ ScriptableObject: {connectionManager.loginData.UserEmail} (ID: {connectionManager.loginData.UserId})");
            Debug.Log($"{LOG_TAG}    Token Valid: {connectionManager.loginData.IsTokenValid()}");
        }
        else
        {
            Debug.LogError($"{LOG_TAG} ❌ No ScriptableObject available");
        }

        string authToken1 = PlayerPrefs.GetString("auth_token", "");
        string authToken2 = PlayerPrefs.GetString("AuthToken", "");
        Debug.Log($"{LOG_TAG} PlayerPrefs 'auth_token': {(authToken1.Length > 0 ? "✅ Available" : "❌ Empty")}");
        Debug.Log($"{LOG_TAG} PlayerPrefs 'AuthToken': {(authToken2.Length > 0 ? "✅ Available" : "❌ Empty")}");

        Debug.Log($"{LOG_TAG} === DEVICE SPECIFIC VIDEO TOKEN SOURCE DEBUG END ===");
    }

    [ContextMenu("Clear All Tokens")]
    public void ClearAllTokens()
    {
        Debug.Log($"{LOG_TAG} 🧹 Clearing all authentication tokens...");

        if (connectionManager?.loginData != null)
        {
            connectionManager.loginData.Logout();
        }

        PlayerPrefs.DeleteKey("auth_token");
        PlayerPrefs.DeleteKey("AuthToken");
        PlayerPrefs.Save();

        UpdateStatus("Tokens cleared - please log in", ConnectionState.Disconnected);
        Debug.Log($"{LOG_TAG} 🧹 All tokens cleared");
    }

    [ContextMenu("Test Manual Video Request")]
    public void TestManualVideoRequest()
    {
        Debug.Log($"{LOG_TAG} 🧪 Testing manual device video request...");
        StartCoroutine(CheckForDeviceVideo());
    }

    [ContextMenu("Debug API Media Status")]
    public void DebugAPIMediaStatus()
    {
        Debug.Log($"{LOG_TAG} === API MEDIA STATUS DEBUG ===");
        Debug.Log($"{LOG_TAG} API Has Image: {(apiHasImageVariable?.Value == true ? "✅ YES" : "❌ NO")} (Variable: {(apiHasImageVariable != null ? "✅ Assigned" : "❌ Missing")})");
        Debug.Log($"{LOG_TAG} API Has Video: {(apiHasVideoVariable?.Value == true ? "✅ YES" : "❌ NO")} (Variable: {(apiHasVideoVariable != null ? "✅ Assigned" : "❌ Missing")})");
        Debug.Log($"{LOG_TAG} API Has Any Media: {(apiHasAnyMediaVariable?.Value == true ? "✅ YES" : "❌ NO")} (Variable: {(apiHasAnyMediaVariable != null ? "✅ Assigned" : "❌ Missing")})");
        Debug.Log($"{LOG_TAG} Current Media Type: {currentMediaType}");
        Debug.Log($"{LOG_TAG} Current Video ID: {currentVideoId}");
        Debug.Log($"{LOG_TAG} === API MEDIA STATUS DEBUG END ===");
    }

    [ContextMenu("Debug System Status")]
    public void DebugSystemStatus()
    {
        Debug.Log($"{LOG_TAG} === DEVICE SPECIFIC VIDEO SYSTEM STATUS DEBUG ===");
        Debug.Log($"{LOG_TAG} Device ID: '{DeviceId}'");
        Debug.Log($"{LOG_TAG} VideoPlayer: {(videoPlayer != null ? "✅ Assigned" : "❌ Missing")}");
        Debug.Log($"{LOG_TAG} Is Loading Video: {(isLoadingVideo ? "🔄 YES" : "✅ No")}");
        Debug.Log($"{LOG_TAG} Current Video ID: {currentVideoId}");
        Debug.Log($"{LOG_TAG} Last Requested ID: {lastRequestedVideoId}");
        Debug.Log($"{LOG_TAG} Current Video URL: {(string.IsNullOrEmpty(currentVideoUrl) ? "❌ None" : "✅ Set")}");
        Debug.Log($"{LOG_TAG} Video prepared: {(videoPlayer?.isPrepared == true ? "✅ Yes" : "❌ No")}");
        Debug.Log($"{LOG_TAG} Video playing: {(videoPlayer?.isPlaying == true ? "✅ Yes" : "❌ No")}");
        Debug.Log($"{LOG_TAG} Current media type: {currentMediaType}");
        Debug.Log($"{LOG_TAG} Polling: {(isPolling ? "✅ Active" : "❌ Inactive")}");
        Debug.Log($"{LOG_TAG} Connected: {(isConnected ? "✅ Yes" : "❌ No")}");
        Debug.Log($"{LOG_TAG} Active Video Load Coroutine: {(currentVideoLoadCoroutine != null ? "🔄 Running" : "✅ None")}");
        Debug.Log($"{LOG_TAG} === DEVICE SPECIFIC VIDEO SYSTEM STATUS DEBUG END ===");
    }

    [ContextMenu("Force Device Registration Check")]
    public void ForceDeviceRegistrationCheck()
    {
        Debug.Log($"{LOG_TAG} 🔧 CHECKING DEVICE REGISTRATION...");

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
                Debug.LogWarning($"{LOG_TAG} Could not call DebugConnectionState: {e.Message}");
            }
        }

        DebugSystemStatus();
    }
}