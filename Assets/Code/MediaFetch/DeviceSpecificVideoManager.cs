using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;
using UnityAtoms.BaseAtoms;
using System.IO;

public class DeviceSpecificVideoManager : MonoBehaviour
{
    private const string LOG_TAG = "[DeviceSpecificVideoManager]";

    [Header("Video Player")]
    public VideoPlayer videoPlayer;

    [Header("Device Configuration")]
    public StringVariable deviceIdVariable;

    public string DeviceId => deviceIdVariable?.Value ?? "MISSING_DEVICE_ID";

    [Header("Settings")]
    public float pollingInterval = 2f;
    public string serverURL = "https://unity-server-control-patrykbroda.replit.app";

    [Header("Cache Settings")]
    public bool enableCaching = true;
    public float maxCacheSizeMB = 5000f;
    public bool autoCleanCache = true;
    public float downloadTimeoutSeconds = 600f;

    [Header("References")]
    public ConnectionManager connectionManager;

    [Header("API Media Status")]
    public BoolVariable apiHasImageVariable;
    public BoolVariable apiHasVideoVariable;
    public BoolVariable apiHasAnyMediaVariable;

    // State Management
    private bool isPolling = false;
    private bool isConnected = false;
    private bool isDownloadingVideo = false;
    private int currentVideoId = 0;
    private int lastRequestedVideoId = 0;
    private MediaType currentMediaType = MediaType.None;
    private Coroutine currentDownloadCoroutine;
    private string videoCacheDir;
    private Dictionary<int, CachedVideoInfo> videoCache = new Dictionary<int, CachedVideoInfo>();

    public enum ConnectionState { Disconnected, Connecting, Connected }
    public enum MediaType { None, DeviceSpecific, GlobalActive }

    [System.Serializable]
    public class CachedVideoInfo
    {
        public int id;
        public string filename;
        public string localPath;
        public long fileSize;
        public System.DateTime lastAccessed;
        public string originalName;
    }

    [System.Serializable]
    public class DeviceMediaResponse
    {
        public bool success;
        public MediaContainer media;
        public string mediaType;
        public int userId;
        public string deviceId;
    }

    [System.Serializable]
    public class MediaContainer
    {
        public DeviceVideoInfo video;
    }

    [System.Serializable]
    public class DeviceVideoInfo
    {
        public int id;
        public string filename;
        public string originalName;
        public string url;
        public long fileSize;
        public bool isActive;
    }

    [System.Serializable]
    public class CacheManifest
    {
        public List<CachedVideoInfo> entries = new List<CachedVideoInfo>();
    }

    void Start()
    {
        Debug.Log($"{LOG_TAG} Starting Video Manager");

        InitializeCacheDirectory();
        LoadCacheManifest();
        InitializeAtomVariables();
        InitializeVideoPlayer();

        if (connectionManager == null)
            connectionManager = FindFirstObjectByType<ConnectionManager>();

        StartCoroutine(WaitForConnectionThenPoll());
    }

    void InitializeCacheDirectory()
    {
        videoCacheDir = Path.Combine(Application.persistentDataPath, "VideoCache");
        if (!Directory.Exists(videoCacheDir))
            Directory.CreateDirectory(videoCacheDir);
    }

    void LoadCacheManifest()
    {
        string manifestPath = Path.Combine(videoCacheDir, "cache_manifest.json");
        if (!File.Exists(manifestPath)) return;

        try
        {
            string json = File.ReadAllText(manifestPath);
            var manifest = JsonUtility.FromJson<CacheManifest>(json);

            foreach (var entry in manifest.entries)
            {
                if (File.Exists(entry.localPath))
                    videoCache[entry.id] = entry;
            }

            Debug.Log($"{LOG_TAG} Loaded {videoCache.Count} cached videos");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LOG_TAG} Failed to load cache: {e.Message}");
        }
    }

    void SaveCacheManifest()
    {
        try
        {
            var manifest = new CacheManifest { entries = new List<CachedVideoInfo>(videoCache.Values) };
            string json = JsonUtility.ToJson(manifest, true);
            string manifestPath = Path.Combine(videoCacheDir, "cache_manifest.json");
            File.WriteAllText(manifestPath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LOG_TAG} Failed to save cache: {e.Message}");
        }
    }

    void InitializeAtomVariables()
    {
        if (apiHasImageVariable != null) apiHasImageVariable.Value = false;
        if (apiHasVideoVariable != null) apiHasVideoVariable.Value = false;
        if (apiHasAnyMediaVariable != null) apiHasAnyMediaVariable.Value = false;
    }

    private void InitializeVideoPlayer()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();

        if (videoPlayer == null)
        {
            Debug.LogError($"{LOG_TAG} No VideoPlayer found!");
            return;
        }

        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.source = VideoSource.Url;

        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.errorReceived += OnVideoError;
    }

    private IEnumerator WaitForConnectionThenPoll()
    {
        if (connectionManager != null)
        {
            while (!IsConnectionReady())
                yield return new WaitForSeconds(0.5f);
        }
        else
        {
            yield return new WaitForSeconds(2f);
        }

        isConnected = true;
        StartPolling();
    }

    private bool IsConnectionReady()
    {
        if (connectionManager == null) return true;

        try
        {
            var connectedProp = connectionManager.GetType().GetProperty("IsConnected");
            var authenticatedProp = connectionManager.GetType().GetProperty("IsAuthenticated");

            if (connectedProp != null && authenticatedProp != null)
            {
                bool connected = (bool)connectedProp.GetValue(connectionManager);
                bool authenticated = (bool)authenticatedProp.GetValue(connectionManager);
                return connected && authenticated;
            }
        }
        catch (System.Exception e)
        {
            Debug.Log($"{LOG_TAG} Connection check failed: {e.Message}");
        }

        return true;
    }

    public void StartPolling()
    {
        if (isPolling) return;

        isPolling = true;
        Debug.Log($"{LOG_TAG} Starting polling");
        StartCoroutine(PollingLoop());
    }

    public void StopPolling()
    {
        isPolling = false;
        Debug.Log($"{LOG_TAG} Stopping polling");
    }

    private IEnumerator PollingLoop()
    {
        while (isPolling && isConnected)
        {
            if (!isDownloadingVideo)
                yield return StartCoroutine(CheckForDeviceVideo());

            yield return new WaitForSeconds(pollingInterval);
        }
    }

    private IEnumerator CheckForDeviceVideo()
    {
        string url = $"{serverURL}/api/device/{DeviceId}/media";
        string authToken = GetAuthToken();

        if (string.IsNullOrEmpty(authToken))
        {
            Debug.LogError($"{LOG_TAG} No auth token!");
            yield break;
        }

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("Authorization", "Bearer " + authToken);
            www.timeout = 30;

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                if (www.responseCode == 401)
                    HandleTokenExpired();
                else
                    Debug.LogError($"{LOG_TAG} Request failed: {www.error}");
            }
            else
            {
                yield return StartCoroutine(ProcessResponse(www.downloadHandler.text));
            }
        }
    }

    private string GetAuthToken()
    {
        if (connectionManager?.loginData != null && connectionManager.loginData.IsTokenValid())
            return connectionManager.loginData.AuthToken;

        string token = PlayerPrefs.GetString("auth_token", "");
        return !string.IsNullOrEmpty(token) ? token : PlayerPrefs.GetString("AuthToken", "");
    }

    private IEnumerator ProcessResponse(string rawResponse)
    {
        DeviceMediaResponse response;
        try
        {
            response = JsonUtility.FromJson<DeviceMediaResponse>(rawResponse);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LOG_TAG} JSON error: {e.Message}");
            yield break;
        }

        // Update API status variables
        bool hasVideo = response.media?.video != null && response.media.video.id > 0;

        if (apiHasVideoVariable != null) apiHasVideoVariable.Value = hasVideo;
        if (apiHasAnyMediaVariable != null) apiHasAnyMediaVariable.Value = hasVideo;

        // Validate response
        if (connectionManager?.loginData != null && response.userId != connectionManager.loginData.UserId)
        {
            Debug.LogError($"{LOG_TAG} User ID mismatch!");
            yield break;
        }

        if (!string.IsNullOrEmpty(response.deviceId) && response.deviceId != DeviceId)
        {
            Debug.LogError($"{LOG_TAG} Device ID mismatch!");
            yield break;
        }

        if (hasVideo)
        {
            DeviceVideoInfo videoInfo = response.media.video;

            if (videoInfo.id != currentVideoId && videoInfo.id != lastRequestedVideoId && !isDownloadingVideo)
            {
                lastRequestedVideoId = videoInfo.id;

                // Check cache first
                if (videoCache.ContainsKey(videoInfo.id) && File.Exists(videoCache[videoInfo.id].localPath))
                {
                    yield return StartCoroutine(PlayCachedVideo(videoCache[videoInfo.id]));
                }
                else
                {
                    if (currentDownloadCoroutine != null)
                        StopCoroutine(currentDownloadCoroutine);

                    currentDownloadCoroutine = StartCoroutine(DownloadAndCacheVideo(videoInfo));
                }
            }
        }
        else if (currentVideoId > 0)
        {
            yield return StartCoroutine(ClearCurrentVideo());
        }
    }

    private IEnumerator DownloadAndCacheVideo(DeviceVideoInfo videoInfo)
    {
        if (isDownloadingVideo) yield break;

        isDownloadingVideo = true;
        Debug.Log($"{LOG_TAG} Downloading: {videoInfo.originalName}");

        // Check and clean cache if needed
        if (maxCacheSizeMB > 0 && autoCleanCache)
        {
            long currentSize = GetCacheSize();
            float maxBytes = maxCacheSizeMB * 1024f * 1024f;

            if (currentSize + videoInfo.fileSize > maxBytes)
                CleanOldVideos(videoInfo.fileSize);
        }

        string videoUrl = $"{serverURL}{videoInfo.url}";
        string localFileName = $"video_{videoInfo.id}_{videoInfo.filename}";
        string localPath = Path.Combine(videoCacheDir, localFileName);

        using (UnityWebRequest www = UnityWebRequest.Get(videoUrl))
        {
            www.downloadHandler = new DownloadHandlerFile(localPath);
            www.timeout = (int)downloadTimeoutSeconds;

            string authToken = GetAuthToken();
            if (!string.IsNullOrEmpty(authToken))
                www.SetRequestHeader("Authorization", "Bearer " + authToken);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{LOG_TAG} Download failed: {www.error}");

                if (File.Exists(localPath))
                    File.Delete(localPath);

                isDownloadingVideo = false;
                currentDownloadCoroutine = null;
                yield break;
            }
        }

        Debug.Log($"{LOG_TAG} Download complete!");

        // Add to cache
        var cachedInfo = new CachedVideoInfo
        {
            id = videoInfo.id,
            filename = localFileName,
            localPath = localPath,
            fileSize = videoInfo.fileSize,
            lastAccessed = System.DateTime.Now,
            originalName = videoInfo.originalName
        };

        videoCache[videoInfo.id] = cachedInfo;
        SaveCacheManifest();

        yield return StartCoroutine(PlayCachedVideo(cachedInfo));

        isDownloadingVideo = false;
        currentDownloadCoroutine = null;
    }

    private IEnumerator PlayCachedVideo(CachedVideoInfo cachedVideo)
    {
        if (!File.Exists(cachedVideo.localPath))
        {
            videoCache.Remove(cachedVideo.id);
            SaveCacheManifest();
            yield break;
        }

        if (videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
            yield return new WaitForSeconds(0.1f);
        }

        string fileUrl = "file:///" + cachedVideo.localPath.Replace('\\', '/');
        videoPlayer.url = fileUrl;

        // Update access time
        cachedVideo.lastAccessed = System.DateTime.Now;
        videoCache[cachedVideo.id] = cachedVideo;

        videoPlayer.Prepare();

        float timeout = 30f;
        float timer = 0f;

        while (!videoPlayer.isPrepared && timer < timeout)
        {
            timer += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        if (!videoPlayer.isPrepared)
        {
            Debug.LogError($"{LOG_TAG} Failed to prepare video!");
            yield break;
        }

        videoPlayer.Play();
        currentVideoId = cachedVideo.id;
        Debug.Log($"{LOG_TAG} Playing: {cachedVideo.originalName}");
    }

    private long GetCacheSize()
    {
        long totalSize = 0;
        if (Directory.Exists(videoCacheDir))
        {
            foreach (FileInfo file in new DirectoryInfo(videoCacheDir).GetFiles())
                totalSize += file.Length;
        }
        return totalSize;
    }

    private void CleanOldVideos(long spaceNeeded)
    {
        var sortedVideos = new List<CachedVideoInfo>(videoCache.Values);
        sortedVideos.Sort((a, b) => a.lastAccessed.CompareTo(b.lastAccessed));

        long freedSpace = 0;
        foreach (var video in sortedVideos)
        {
            if (video.id == currentVideoId) continue;

            if (File.Exists(video.localPath))
            {
                long fileSize = new FileInfo(video.localPath).Length;
                File.Delete(video.localPath);
                videoCache.Remove(video.id);
                freedSpace += fileSize;

                if (freedSpace >= spaceNeeded) break;
            }
        }

        SaveCacheManifest();
        Debug.Log($"{LOG_TAG} Freed cache space");
    }

    public void ClearVideoCache()
    {
        if (videoPlayer?.isPlaying == true)
            videoPlayer.Stop();

        if (Directory.Exists(videoCacheDir))
        {
            foreach (FileInfo file in new DirectoryInfo(videoCacheDir).GetFiles())
                file.Delete();
        }

        videoCache.Clear();
        SaveCacheManifest();
        currentVideoId = 0;

        Debug.Log($"{LOG_TAG} Cache cleared");
    }

    private IEnumerator ClearCurrentVideo()
    {
        if (videoPlayer != null)
        {
            if (videoPlayer.isPlaying)
            {
                videoPlayer.Stop();
                yield return new WaitForSeconds(0.1f);
            }
            videoPlayer.url = "";
        }

        currentVideoId = 0;
        lastRequestedVideoId = 0;
        currentMediaType = MediaType.None;

        if (apiHasVideoVariable != null) apiHasVideoVariable.Value = false;
        if (apiHasAnyMediaVariable != null) apiHasAnyMediaVariable.Value = false;
    }

    private void HandleTokenExpired()
    {
        Debug.Log($"{LOG_TAG} Token expired");

        connectionManager?.loginData?.Logout();
        PlayerPrefs.DeleteKey("auth_token");
        PlayerPrefs.DeleteKey("AuthToken");
        PlayerPrefs.Save();

        StopPolling();
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        Debug.Log($"{LOG_TAG} Video prepared");
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"{LOG_TAG} Video error: {message}");
        isDownloadingVideo = false;
        currentDownloadCoroutine = null;
    }

    // Public API
    public bool GetApiHasVideo() => apiHasVideoVariable?.Value ?? false;
    public bool GetApiHasAnyMedia() => apiHasAnyMediaVariable?.Value ?? false;

    public void TogglePlayPause()
    {
        if (videoPlayer == null) return;

        if (videoPlayer.isPlaying)
            videoPlayer.Pause();
        else if (videoPlayer.isPrepared)
            videoPlayer.Play();
    }

    [ContextMenu("Emergency Stop")]
    public void EmergencyStop()
    {
        Debug.Log($"{LOG_TAG} Emergency stop");

        isDownloadingVideo = false;

        if (currentDownloadCoroutine != null)
        {
            StopCoroutine(currentDownloadCoroutine);
            currentDownloadCoroutine = null;
        }

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.url = "";
        }

        currentVideoId = 0;
        lastRequestedVideoId = 0;
    }

    void OnDestroy()
    {
        StopPolling();

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.Stop();
        }

        if (currentDownloadCoroutine != null)
            StopCoroutine(currentDownloadCoroutine);

        SaveCacheManifest();
    }
}