using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.Video;
using UIButton = UnityEngine.UIElements.Button;

public class VideoManager : MonoBehaviour
{
    [Header("Debug Settings")]
    [Tooltip("Enable/disable all debug logging for this component")]
    public bool enableDebugLogs = true;

    [Header("Video Player")]
    public VideoPlayer videoPlayer;

    [Header("UI Toolkit Overlay (Simple Text Only)")]
    public UIDocument overlayDocument;

    [Header("Settings")]
    public float pollingInterval = 2f;
    public string serverURL = "https://unity-server-control-patrykbroda.replit.app";

    [Header("Connection Reference")]
    public ConnectionManager connectionManager;

    // UI Elements
    private Label statusText;
    private Label serverUrl;
    private Label userInfo;
    private Label pollingStatus;
    private Label lastUpdateTime;
    private Label currentVideoName;
    private Label videoInfo;

    private UIButton manualPollBtn;
    private UIButton togglePollingBtn;
    private UIButton debugTokensBtn;
    private UIButton clearTokensBtn;
    private UIButton playPauseBtn;

    // State Management
    private bool isPolling = false;
    private bool isConnected = false;
    private bool isLoadingVideo = false; // 🔧 NEW: Prevent concurrent video loading
    private string currentVideoId = "";
    private string currentVideoUrl = "";
    private string lastRequestedVideoId = ""; // 🔧 NEW: Track what we last requested
    private Coroutine currentVideoLoadCoroutine; // 🔧 NEW: Track active video loading

    [System.Serializable]
    public class VideoCurrentResponse
    {
        public bool success;
        public VideoInfo video;
        public string timestamp;
        public int userId = 0;

        public override string ToString()
        {
            return $"VideoCurrentResponse(success:{success}, video:{(video?.id ?? "null")}, timestamp:{timestamp}, userId:{userId})";
        }
    }

    [System.Serializable]
    public class VideoInfo
    {
        public string id;
        public string filename;
        public string originalName;
        public string url;
        public string timestamp;
        public string duration;
        public int fileSize;
        public string mimeType;

        public override string ToString()
        {
            return $"VideoInfo(id:{id}, filename:{filename}, originalName:{originalName}, size:{fileSize}, type:{mimeType})";
        }
    }

    // Debug logging wrapper methods
    private void DebugLog(string message)
    {
        if (enableDebugLogs) Debug.Log(message);
    }

    private void DebugLogWarning(string message)
    {
        if (enableDebugLogs) Debug.LogWarning(message);
    }

    private void DebugLogError(string message)
    {
        if (enableDebugLogs) Debug.LogError(message);
    }

    void Start()
    {
        DebugLog("VideoManager: Start() called");

        InitializeVideoPlayer();
        InitializeOverlay();

        if (string.IsNullOrEmpty(serverURL))
        {
            serverURL = "https://unity-server-control-patrykbroda.replit.app";
            DebugLog($"VideoManager: Set default serverURL to: {serverURL}");
        }

        UpdateStatus("Initializing...", ConnectionState.Connecting);

        if (connectionManager == null)
        {
            connectionManager = FindFirstObjectByType<ConnectionManager>();
            if (connectionManager == null)
            {
                DebugLogWarning("VideoManager: No ConnectionManager found! Will try to start polling anyway.");
            }
        }

        StartCoroutine(WaitForConnectionThenPoll());
    }

    private void InitializeVideoPlayer()
    {
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                DebugLogError("VideoManager: No VideoPlayer component found! Please assign one.");
                return;
            }
        }

        // 🔧 IMPROVED: Better video player configuration
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        videoPlayer.skipOnDrop = true;
        videoPlayer.waitForFirstFrame = true; // NEW: Wait for first frame before considering prepared

        // Subscribe to video player events
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.started += OnVideoStarted;
        videoPlayer.loopPointReached += OnVideoLoopCompleted;

        DebugLog($"VideoManager: VideoPlayer initialized - Render Mode: {videoPlayer.renderMode}");
    }

    private void InitializeOverlay()
    {
        DebugLog("VideoManager: Initializing video UI overlay...");

        if (overlayDocument == null)
        {
            overlayDocument = GetComponent<UIDocument>();
        }

        if (overlayDocument != null)
        {
            VisualElement root = overlayDocument.rootVisualElement;

            // Video-specific UI elements
            statusText = root.Q<Label>("video-status-text");
            serverUrl = root.Q<Label>("video-server-url");
            userInfo = root.Q<Label>("video-user-info");
            pollingStatus = root.Q<Label>("video-polling-status");
            lastUpdateTime = root.Q<Label>("video-last-update-time");
            currentVideoName = root.Q<Label>("current-video-name");
            videoInfo = root.Q<Label>("video-info");

            manualPollBtn = root.Q<UIButton>("video-manual-poll-btn");
            togglePollingBtn = root.Q<UIButton>("video-toggle-polling-btn");
            debugTokensBtn = root.Q<UIButton>("video-debug-tokens-btn");
            clearTokensBtn = root.Q<UIButton>("video-clear-tokens-btn");
            playPauseBtn = root.Q<UIButton>("video-play-pause-btn");

            // Bind button events
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
            {
                serverUrl.text = $"Server: {serverURL}";
            }

            DebugLog("VideoManager: Video overlay initialized successfully");
        }
        else
        {
            DebugLog("VideoManager: No UI overlay document assigned - running without overlay");
        }

        if (videoPlayer != null)
        {
            DebugLog($"VideoManager: VideoPlayer assigned - Render Mode: {videoPlayer.renderMode}");
        }
        else
        {
            DebugLogWarning("VideoManager: No VideoPlayer assigned - videos will not be displayed!");
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

        DebugLog($"VideoManager Status: {message} (State: {state}, Loading: {isLoadingVideo})");
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
        DebugLog("VideoManager: Waiting for connection...");

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

        DebugLog("VideoManager: Connection established! Starting video polling...");
        UpdateStatus("Connected! Starting video polling...", ConnectionState.Connected);

        isConnected = true;
        StartVideoPolling();
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

                return isConnected && isAuthenticated;
            }
        }
        catch (System.Exception e)
        {
            DebugLog($"VideoManager: Could not check connection status: {e.Message}");
        }

        return true;
    }

    public void StartVideoPolling()
    {
        if (isPolling)
        {
            DebugLog("VideoManager: Already polling, skipping StartVideoPolling");
            return;
        }

        DebugLog($"VideoManager: StartVideoPolling() called");
        isPolling = true;
        UpdateStatus("Polling for videos...", ConnectionState.Connected);
        UpdateOverlayInfo();

        StartCoroutine(VideoPollingLoop());
    }

    public void StopVideoPolling()
    {
        DebugLog("VideoManager: Stopping video polling");
        isPolling = false;
        UpdateStatus("Polling stopped", ConnectionState.Disconnected);
        UpdateOverlayInfo();
    }

    private IEnumerator VideoPollingLoop()
    {
        DebugLog("VideoManager: VideoPollingLoop started");

        while (isPolling && isConnected)
        {
            // 🔧 FIX: Only poll if we're not currently loading a video
            if (!isLoadingVideo)
            {
                yield return StartCoroutine(CheckForNewVideo());
            }
            else
            {
                DebugLog("VideoManager: Skipping poll - video loading in progress");
            }

            yield return new WaitForSeconds(pollingInterval);
        }

        DebugLog("VideoManager: VideoPollingLoop ended");
    }

    private IEnumerator CheckForNewVideo()
    {
        string url = $"{serverURL}/api/video/current";
        DebugLog($"VideoManager: Polling URL: {url}");

        string authToken = GetAuthToken();
        if (string.IsNullOrEmpty(authToken))
        {
            DebugLogError("VideoManager: ❌ No auth token found!");
            UpdateStatus("No authentication token found", ConnectionState.Disconnected);
            yield break;
        }

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("Authorization", "Bearer " + authToken);
            www.SetRequestHeader("Cache-Control", "no-cache");

            yield return www.SendWebRequest();

            DebugLog($"VideoManager: Response Code: {www.responseCode}");

            if (www.result != UnityWebRequest.Result.Success)
            {
                DebugLogError($"VideoManager: Request failed - {www.error}");

                if (www.responseCode == 401)
                {
                    HandleTokenExpired();
                    yield break;
                }

                UpdateStatus($"Request failed: {www.error}", ConnectionState.Disconnected);
            }
            else
            {
                StartCoroutine(ProcessVideoResponse(www.downloadHandler.text));
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

    private void HandleTokenExpired()
    {
        DebugLogError("VideoManager: Authentication failed - token may be expired");
        UpdateStatus("Authentication expired", ConnectionState.Disconnected);

        PlayerPrefs.DeleteKey("auth_token");
        PlayerPrefs.DeleteKey("AuthToken");
        PlayerPrefs.Save();

        if (connectionManager?.loginData != null)
        {
            connectionManager.loginData.Logout();
        }

        StopVideoPolling();
    }

    private IEnumerator ProcessVideoResponse(string rawResponse)
    {
        DebugLog($"VideoManager: RAW SERVER RESPONSE: {rawResponse}");

        VideoCurrentResponse response = null;
        try
        {
            response = JsonUtility.FromJson<VideoCurrentResponse>(rawResponse);
            DebugLog("VideoManager: ✅ JSON parsing successful");
        }
        catch (System.Exception e)
        {
            DebugLogError($"VideoManager: JSON parsing failed: {e.Message}");
            UpdateStatus($"JSON parsing failed", ConnectionState.Disconnected);
            yield break;
        }

        if (response == null)
        {
            DebugLogError("VideoManager: Could not parse server response");
            yield break;
        }

        // Validate user ID
        if (connectionManager?.loginData != null)
        {
            int expectedUserId = connectionManager.loginData.UserId;
            bool isNoVideoResponse = response.userId == 0 && response.video == null;

            if (isNoVideoResponse)
            {
                DebugLog("VideoManager: 📺 No video set for user - this is normal");
            }
            else if (response.userId != expectedUserId)
            {
                DebugLogError($"🚨 CRITICAL USER ID MISMATCH! Expected: {expectedUserId}, Got: {response.userId}");
                UpdateStatus("🚨 SECURITY ERROR: User ID mismatch!", ConnectionState.Disconnected);
                yield break;
            }
        }

        bool hasCurrentVideo = response.video != null && !string.IsNullOrEmpty(response.video.id);

        if (hasCurrentVideo)
        {
            // 🔧 KEY FIX: Check if this is a different video AND we're not already loading it
            if (response.video.id != currentVideoId && response.video.id != lastRequestedVideoId && !isLoadingVideo)
            {
                DebugLog($"VideoManager: New video detected! Old: '{currentVideoId}' -> New: '{response.video.id}'");
                lastRequestedVideoId = response.video.id; // Track what we're about to request

                // 🔧 FIX: Cancel any existing video load operation
                if (currentVideoLoadCoroutine != null)
                {
                    StopCoroutine(currentVideoLoadCoroutine);
                    currentVideoLoadCoroutine = null;
                }

                currentVideoLoadCoroutine = StartCoroutine(LoadAndPlayVideoSafely(response.video));
            }
            else if (response.video.id == currentVideoId)
            {
                DebugLog($"VideoManager: Same video as before (ID: {currentVideoId}), no loading needed");
                UpdateStatus($"Current: {response.video.originalName}", ConnectionState.Connected);
            }
            else if (isLoadingVideo)
            {
                DebugLog($"VideoManager: Video loading in progress, skipping new request");
            }
        }
        else
        {
            DebugLog("VideoManager: ❌ No current video detected");
            UpdateStatus("No video set on server", ConnectionState.Connected);

            if (!string.IsNullOrEmpty(currentVideoId))
            {
                DebugLog("VideoManager: Clearing previous video");
                yield return StartCoroutine(ClearCurrentVideoSafely());
            }
        }
    }

    // 🔧 NEW: Safe video loading with proper cleanup and error handling
    private IEnumerator LoadAndPlayVideoSafely(VideoInfo videoInfo)
    {
        if (isLoadingVideo)
        {
            DebugLogWarning("VideoManager: Already loading a video, skipping new request");
            yield break;
        }

        isLoadingVideo = true;
        DebugLog($"VideoManager: Starting SAFE load of video: {videoInfo}");

        if (videoPlayer == null)
        {
            DebugLogError("VideoManager: No VideoPlayer assigned!");
            UpdateStatus("No VideoPlayer assigned", ConnectionState.Disconnected);
            isLoadingVideo = false;
            yield break;
        }

        // Validate URL first (no yield in validation)
        string videoUrl = $"{serverURL}{videoInfo.url}";
        DebugLog($"VideoManager: Full video URL: {videoUrl}");

        if (!IsValidVideoUrl(videoUrl))
        {
            DebugLogError($"VideoManager: Invalid video URL: {videoUrl}");
            UpdateStatus("Invalid video URL", ConnectionState.Disconnected);
            isLoadingVideo = false;
            yield break;
        }

        // Start the loading process
        UpdateStatus($"Loading: {videoInfo.originalName}...", ConnectionState.Connected);

        // 🔧 CRITICAL FIX: Proper video cleanup sequence
        yield return StartCoroutine(StopCurrentVideoCompletely());

        // 🔧 FIX: Wait before setting new URL to avoid conflicts
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

        DebugLog("VideoManager: Preparing video...");
        videoPlayer.Prepare();

        // 🔧 IMPROVED: Better preparation waiting with more granular timeout
        float timeout = 20f; // Reduced timeout
        float checkInterval = 0.1f;
        float timer = 0f;

        while (!videoPlayer.isPrepared && timer < timeout)
        {
            timer += checkInterval;
            yield return new WaitForSeconds(checkInterval);

            // 🔧 FIX: Update UI during loading
            if (timer % 1f < checkInterval) // Update every second
            {
                UpdateStatus($"Loading: {videoInfo.originalName}... ({timer:F0}s)", ConnectionState.Connected);
                UpdateOverlayInfo();
            }
        }

        // Check if preparation was successful
        if (!videoPlayer.isPrepared)
        {
            DebugLogError("VideoManager: Video preparation timed out!");
            UpdateStatus($"Failed to load: {videoInfo.originalName} (timeout)", ConnectionState.Disconnected);
            yield return StartCoroutine(StopCurrentVideoCompletely());
            isLoadingVideo = false;
            currentVideoLoadCoroutine = null;
            yield break;
        }

        DebugLog("VideoManager: ✅ Video prepared successfully, starting playback");

        // 🔧 FIX: Small delay before playing to ensure everything is ready
        yield return new WaitForSeconds(0.1f);

        videoPlayer.Play();
        currentVideoId = videoInfo.id;

        UpdateStatus($"Playing: {videoInfo.originalName}", ConnectionState.Connected);
        UpdateOverlayInfo();

        DebugLog($"VideoManager: Successfully loaded and started video: {videoInfo.originalName}");

        // Clean up loading state
        isLoadingVideo = false;
        currentVideoLoadCoroutine = null;
    }

    // 🔧 NEW: Comprehensive video stopping
    private IEnumerator StopCurrentVideoCompletely()
    {
        if (videoPlayer == null) yield break;

        DebugLog("VideoManager: Stopping current video completely...");

        // Stop if playing
        if (videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
            yield return new WaitForSeconds(0.1f);
        }

        // Clear URL to release resources
        videoPlayer.url = "";
        yield return new WaitForEndOfFrame();

        DebugLog("VideoManager: Video stopped and resources cleared");
    }

    // 🔧 NEW: Safe video clearing
    private IEnumerator ClearCurrentVideoSafely()
    {
        DebugLog("VideoManager: ClearCurrentVideoSafely() called");

        yield return StartCoroutine(StopCurrentVideoCompletely());

        if (currentVideoName != null)
        {
            currentVideoName.text = "No video";
        }

        if (videoInfo != null)
        {
            videoInfo.text = "No video loaded";
        }

        currentVideoId = "";
        currentVideoUrl = "";
        lastRequestedVideoId = "";

        DebugLog("VideoManager: Current video cleared safely");
    }

    // 🔧 NEW: URL validation
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

    private void UpdateVideoInfoDisplay(VideoInfo videoInfo)
    {
        if (videoInfo != null && this.videoInfo != null)
        {
            string sizeText = FormatFileSize(videoInfo.fileSize);
            string durationText = !string.IsNullOrEmpty(videoInfo.duration) ? videoInfo.duration : "Unknown";
            this.videoInfo.text = $"Size: {sizeText} | Duration: {durationText} | Type: {videoInfo.mimeType}";
        }
    }

    private string FormatFileSize(int bytes)
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

    // Video Player Event Handlers
    private void OnVideoPrepared(VideoPlayer source)
    {
        DebugLog($"VideoManager: Video prepared successfully - Duration: {source.length:F1}s");
        UpdateOverlayInfo();
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        DebugLogError($"VideoManager: Video error - {message}");
        UpdateStatus($"Video error: {message}", ConnectionState.Disconnected);

        // 🔧 FIX: Clear loading state on error
        isLoadingVideo = false;
        currentVideoLoadCoroutine = null;
    }

    private void OnVideoStarted(VideoPlayer source)
    {
        DebugLog("VideoManager: Video playback started");
        UpdateOverlayInfo();
    }

    private void OnVideoLoopCompleted(VideoPlayer source)
    {
        DebugLog("VideoManager: Video loop completed");
    }

    void OnDestroy()
    {
        DebugLog("VideoManager: OnDestroy() called");
        StopVideoPolling();

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.started -= OnVideoStarted;
            videoPlayer.loopPointReached -= OnVideoLoopCompleted;

            videoPlayer.Stop();
        }

        // 🔧 FIX: Clean up coroutines
        if (currentVideoLoadCoroutine != null)
        {
            StopCoroutine(currentVideoLoadCoroutine);
        }
    }

    // UI Button Methods
    public void ManualPoll()
    {
        DebugLog("VideoManager: Manual poll triggered");
        if (isConnected && !isLoadingVideo)
        {
            StartCoroutine(CheckForNewVideo());
        }
        else if (isLoadingVideo)
        {
            DebugLogWarning("VideoManager: Cannot manual poll - video loading in progress");
        }
        else
        {
            DebugLogWarning("VideoManager: Cannot manual poll - not connected");
        }
    }

    public void TogglePolling()
    {
        if (isPolling)
        {
            StopVideoPolling();
        }
        else
        {
            StartVideoPolling();
        }
    }

    public void TogglePlayPause()
    {
        if (videoPlayer == null) return;

        if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
            DebugLog("VideoManager: Video paused");
        }
        else if (videoPlayer.isPrepared)
        {
            videoPlayer.Play();
            DebugLog("VideoManager: Video resumed");
        }
        else
        {
            DebugLogWarning("VideoManager: Video not prepared for playback");
        }

        UpdateOverlayInfo();
    }

    // Debug Methods
    [ContextMenu("Emergency Stop All Video Operations")]
    public void EmergencyStopAllVideoOperations()
    {
        DebugLog("🚨 EMERGENCY STOP: Stopping all video operations");

        isLoadingVideo = false;

        if (currentVideoLoadCoroutine != null)
        {
            StopCoroutine(currentVideoLoadCoroutine);
            currentVideoLoadCoroutine = null;
        }

        StartCoroutine(StopCurrentVideoCompletely());

        currentVideoId = "";
        currentVideoUrl = "";
        lastRequestedVideoId = "";

        UpdateStatus("Emergency stop completed", ConnectionState.Disconnected);
        DebugLog("🚨 Emergency stop completed");
    }

    [ContextMenu("Debug Token Sources")]
    public void DebugTokenSources()
    {
        DebugLog("=== VIDEO TOKEN SOURCE DEBUG ===");

        if (connectionManager != null && connectionManager.loginData != null)
        {
            DebugLog($"✅ ScriptableObject: {connectionManager.loginData.UserEmail} (ID: {connectionManager.loginData.UserId})");
            DebugLog($"   Token Valid: {connectionManager.loginData.IsTokenValid()}");
        }
        else
        {
            DebugLogError("❌ No ScriptableObject available");
        }

        string authToken1 = PlayerPrefs.GetString("auth_token", "");
        string authToken2 = PlayerPrefs.GetString("AuthToken", "");
        DebugLog($"PlayerPrefs 'auth_token': {(authToken1.Length > 0 ? "✅ Available" : "❌ Empty")}");
        DebugLog($"PlayerPrefs 'AuthToken': {(authToken2.Length > 0 ? "✅ Available" : "❌ Empty")}");

        DebugLog("=== VIDEO TOKEN SOURCE DEBUG END ===");
    }

    [ContextMenu("Clear All Tokens")]
    public void ClearAllTokens()
    {
        DebugLog("🧹 Clearing all authentication tokens...");

        if (connectionManager?.loginData != null)
        {
            connectionManager.loginData.Logout();
        }

        PlayerPrefs.DeleteKey("auth_token");
        PlayerPrefs.DeleteKey("AuthToken");
        PlayerPrefs.Save();

        UpdateStatus("Tokens cleared - please log in", ConnectionState.Disconnected);
        DebugLog("🧹 All tokens cleared");
    }

    [ContextMenu("Debug System Status")]
    public void DebugSystemStatus()
    {
        DebugLog("=== VIDEO SYSTEM STATUS DEBUG ===");
        DebugLog($"VideoPlayer: {(videoPlayer != null ? "✅ Assigned" : "❌ Missing")}");
        DebugLog($"Is Loading Video: {(isLoadingVideo ? "🔄 YES" : "✅ No")}");
        DebugLog($"Current Video ID: '{currentVideoId}'");
        DebugLog($"Last Requested ID: '{lastRequestedVideoId}'");
        DebugLog($"Current Video URL: {(string.IsNullOrEmpty(currentVideoUrl) ? "❌ None" : "✅ Set")}");
        DebugLog($"Video prepared: {(videoPlayer?.isPrepared == true ? "✅ Yes" : "❌ No")}");
        DebugLog($"Video playing: {(videoPlayer?.isPlaying == true ? "✅ Yes" : "❌ No")}");
        DebugLog($"Polling: {(isPolling ? "✅ Active" : "❌ Inactive")}");
        DebugLog($"Connected: {(isConnected ? "✅ Yes" : "❌ No")}");
        DebugLog($"Active Video Load Coroutine: {(currentVideoLoadCoroutine != null ? "🔄 Running" : "✅ None")}");
        DebugLog("=== VIDEO SYSTEM STATUS DEBUG END ===");
    }

    [ContextMenu("Toggle Debug Logs")]
    public void ToggleDebugLogs()
    {
        enableDebugLogs = !enableDebugLogs;
        DebugLog($"VideoManager: Debug logging {(enableDebugLogs ? "ENABLED" : "DISABLED")}");
    }
}