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
    public bool clearCacheOnStart = false; // Set to true temporarily to clear bad cache

    [Header("Enhanced Download Settings")]
    public int downloadChunkSizeMB = 5; // Download in 5MB chunks
    public int maxRetryAttempts = 3;
    public float retryDelay = 2f;
    public bool enableChunkedDownload = true;
    public int maxConcurrentChunks = 2; // For parallel downloading

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

    // Progress tracking
    private float currentDownloadProgress = 0f;
    private long totalBytesDownloaded = 0;
    private long totalFileSize = 0;

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

    [System.Serializable]
    public class DownloadState
    {
        public int videoId;
        public string url;
        public long totalSize;
        public long downloadedBytes;
        public string tempFilePath;
    }

    // Public property to expose download progress
    public float DownloadProgress => currentDownloadProgress;

    void Start()
    {
        Debug.Log($"{LOG_TAG} Starting Video Manager");

        InitializeCacheDirectory();

        // Clean up any bad cache from previous versions
        if (clearCacheOnStart)
        {
            Debug.Log($"{LOG_TAG} Clearing cache on start (clearCacheOnStart = true)");
            ClearVideoCache();
        }
        else
        {
            CleanupBadCache();
        }

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

    void CleanupBadCache()
    {
        // Clean up any subdirectories (like "uploads") that shouldn't exist
        if (Directory.Exists(videoCacheDir))
        {
            string[] subdirs = Directory.GetDirectories(videoCacheDir);
            foreach (string subdir in subdirs)
            {
                Debug.Log($"{LOG_TAG} Removing invalid subdirectory: {subdir}");
                Directory.Delete(subdir, true);
            }

            // Also clean up any files without proper extensions
            string[] files = Directory.GetFiles(videoCacheDir);
            foreach (string file in files)
            {
                if (!file.EndsWith(".mp4") && !file.EndsWith(".webm") && !file.EndsWith(".mov") &&
                    !file.EndsWith(".json") && !file.EndsWith(".tmp"))
                {
                    Debug.Log($"{LOG_TAG} Removing invalid file: {file}");
                    File.Delete(file);
                }
            }
        }
    }

    void LoadCacheManifest()
    {
        string manifestPath = Path.Combine(videoCacheDir, "cache_manifest.json");
        if (!File.Exists(manifestPath)) return;

        try
        {
            string json = File.ReadAllText(manifestPath);
            var manifest = JsonUtility.FromJson<CacheManifest>(json);

            videoCache.Clear();
            foreach (var entry in manifest.entries)
            {
                // Only load entries with valid file extensions
                if (File.Exists(entry.localPath) &&
                    (entry.localPath.EndsWith(".mp4") || entry.localPath.EndsWith(".webm") || entry.localPath.EndsWith(".mov")))
                {
                    videoCache[entry.id] = entry;
                }
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

                    // Use enhanced download method
                    currentDownloadCoroutine = StartCoroutine(EnhancedDownloadAndCacheVideo(videoInfo));
                }
            }
        }
        else if (currentVideoId > 0)
        {
            yield return StartCoroutine(ClearCurrentVideo());
        }
    }

    private IEnumerator EnhancedDownloadAndCacheVideo(DeviceVideoInfo videoInfo)
    {
        if (isDownloadingVideo) yield break;

        isDownloadingVideo = true;
        currentDownloadProgress = 0f;
        totalBytesDownloaded = 0;
        totalFileSize = videoInfo.fileSize;

        Debug.Log($"{LOG_TAG} Starting enhanced download: {videoInfo.originalName} ({FormatFileSize(videoInfo.fileSize)})");

        // Check cache space
        if (maxCacheSizeMB > 0 && autoCleanCache)
        {
            long currentSize = GetCacheSize();
            float maxBytes = maxCacheSizeMB * 1024f * 1024f;

            if (currentSize + videoInfo.fileSize > maxBytes)
                CleanOldVideos(videoInfo.fileSize);
        }

        string videoUrl = $"{serverURL}{videoInfo.url}";

        // Fix the filename handling - extract just the filename, no paths
        string cleanFilename = Path.GetFileName(videoInfo.filename);
        if (string.IsNullOrEmpty(cleanFilename))
        {
            cleanFilename = Path.GetFileName(videoInfo.url);
        }

        // Remove any directory separators that might still be there
        cleanFilename = cleanFilename.Replace("/", "").Replace("\\", "");

        // Ensure the file has a video extension
        if (!Path.HasExtension(cleanFilename) ||
            (!cleanFilename.EndsWith(".mp4", System.StringComparison.OrdinalIgnoreCase) &&
             !cleanFilename.EndsWith(".webm", System.StringComparison.OrdinalIgnoreCase) &&
             !cleanFilename.EndsWith(".mov", System.StringComparison.OrdinalIgnoreCase)))
        {
            // Add .mp4 extension if missing or unknown
            cleanFilename = Path.GetFileNameWithoutExtension(cleanFilename) + ".mp4";
        }

        string localFileName = $"video_{videoInfo.id}_{cleanFilename}";
        string localPath = Path.Combine(videoCacheDir, localFileName);
        string tempPath = localPath + ".tmp";

        Debug.Log($"{LOG_TAG} Downloading to: {localFileName}");

        // Check if we can resume a partial download
        long existingBytes = 0;
        if (File.Exists(tempPath))
        {
            existingBytes = new FileInfo(tempPath).Length;
            Debug.Log($"{LOG_TAG} Resuming download from {FormatFileSize(existingBytes)}");
        }

        // Use chunked download for large files
        if (enableChunkedDownload && videoInfo.fileSize > 10 * 1024 * 1024) // > 10MB
        {
            yield return StartCoroutine(ChunkedDownload(videoUrl, tempPath, videoInfo.fileSize, existingBytes));
        }
        else
        {
            // Fall back to standard download for small files
            yield return StartCoroutine(StandardDownload(videoUrl, tempPath));
        }

        // Verify download completed
        if (!File.Exists(tempPath))
        {
            Debug.LogError($"{LOG_TAG} Download failed - file not found");
            isDownloadingVideo = false;
            currentDownloadCoroutine = null;
            yield break;
        }

        // Verify file size
        long downloadedSize = new FileInfo(tempPath).Length;
        if (System.Math.Abs(downloadedSize - videoInfo.fileSize) > 1024) // Allow 1KB tolerance
        {
            Debug.LogWarning($"{LOG_TAG} File size mismatch. Expected: {videoInfo.fileSize}, Got: {downloadedSize}");
        }

        // Move temp file to final location
        if (File.Exists(localPath))
            File.Delete(localPath);
        File.Move(tempPath, localPath);

        Debug.Log($"{LOG_TAG} Download complete: {videoInfo.originalName} saved as {localFileName}");

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

    private IEnumerator ChunkedDownload(string url, string filePath, long totalSize, long startByte = 0)
    {
        int chunkSize = downloadChunkSizeMB * 1024 * 1024;
        long currentByte = startByte;
        totalBytesDownloaded = startByte;

        // Open or create file for writing
        using (FileStream fileStream = new FileStream(filePath, startByte > 0 ? FileMode.Append : FileMode.Create))
        {
            while (currentByte < totalSize)
            {
                long endByte = System.Math.Min(currentByte + chunkSize - 1, totalSize - 1);
                bool chunkSuccess = false;
                int retryCount = 0;

                while (!chunkSuccess && retryCount < maxRetryAttempts)
                {
                    using (UnityWebRequest request = UnityWebRequest.Get(url))
                    {
                        // Set range header for partial download
                        request.SetRequestHeader("Range", $"bytes={currentByte}-{endByte}");

                        string authToken = GetAuthToken();
                        if (!string.IsNullOrEmpty(authToken))
                            request.SetRequestHeader("Authorization", "Bearer " + authToken);

                        request.timeout = 60; // 60 seconds per chunk

                        yield return request.SendWebRequest();

                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            // Write chunk to file
                            byte[] data = request.downloadHandler.data;
                            fileStream.Write(data, 0, data.Length);
                            fileStream.Flush(); // Ensure data is written to disk

                            currentByte = endByte + 1;
                            totalBytesDownloaded += data.Length;
                            currentDownloadProgress = (float)totalBytesDownloaded / totalSize;

                            Debug.Log($"{LOG_TAG} Progress: {(currentDownloadProgress * 100):F1}% ({FormatFileSize(totalBytesDownloaded)}/{FormatFileSize(totalSize)})");

                            chunkSuccess = true;
                        }
                        else
                        {
                            retryCount++;
                            Debug.LogWarning($"{LOG_TAG} Chunk failed (attempt {retryCount}/{maxRetryAttempts}): {request.error}");

                            if (retryCount < maxRetryAttempts)
                            {
                                yield return new WaitForSeconds(retryDelay * retryCount); // Exponential backoff
                            }
                            else
                            {
                                Debug.LogError($"{LOG_TAG} Failed to download chunk after {maxRetryAttempts} attempts");
                                yield break;
                            }
                        }
                    }
                }

                // Optional: Add delay between chunks to avoid overwhelming the server
                yield return null;
            }
        }

        currentDownloadProgress = 1f;
        Debug.Log($"{LOG_TAG} Chunked download completed successfully");
    }

    private IEnumerator StandardDownload(string url, string filePath)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.downloadHandler = new DownloadHandlerFile(filePath);
            request.timeout = (int)downloadTimeoutSeconds;

            string authToken = GetAuthToken();
            if (!string.IsNullOrEmpty(authToken))
                request.SetRequestHeader("Authorization", "Bearer " + authToken);

            // Track progress
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                currentDownloadProgress = request.downloadProgress;
                Debug.Log($"{LOG_TAG} Download progress: {(currentDownloadProgress * 100):F1}%");
                yield return new WaitForSeconds(0.5f);
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{LOG_TAG} Download failed: {request.error}");

                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            else
            {
                currentDownloadProgress = 1f;
                Debug.Log($"{LOG_TAG} Standard download completed");
            }
        }
    }

    private IEnumerator PlayCachedVideo(CachedVideoInfo cachedVideo)
    {
        if (!File.Exists(cachedVideo.localPath))
        {
            Debug.LogError($"{LOG_TAG} Cached video file not found: {cachedVideo.localPath}");
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
        Debug.Log($"{LOG_TAG} Attempting to play video from: {fileUrl}");
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

    [ContextMenu("Clear Video Cache")]
    public void ClearVideoCache()
    {
        if (videoPlayer?.isPlaying == true)
            videoPlayer.Stop();

        if (Directory.Exists(videoCacheDir))
        {
            // Delete all files and subdirectories
            Directory.Delete(videoCacheDir, true);
            // Recreate the directory
            Directory.CreateDirectory(videoCacheDir);
        }

        videoCache.Clear();
        SaveCacheManifest();
        currentVideoId = 0;
        lastRequestedVideoId = 0;

        Debug.Log($"{LOG_TAG} Cache cleared completely");
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

    // Helper method to format file sizes
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:F2} {sizes[order]}";
    }

    // Method to cancel current download
    public void CancelDownload()
    {
        if (currentDownloadCoroutine != null)
        {
            StopCoroutine(currentDownloadCoroutine);
            currentDownloadCoroutine = null;
        }

        isDownloadingVideo = false;
        currentDownloadProgress = 0f;

        Debug.Log($"{LOG_TAG} Download cancelled");
    }

    // Optional: Save download progress for resume capability
    private void SaveDownloadState(DownloadState state)
    {
        string statePath = Path.Combine(videoCacheDir, "download_state.json");
        string json = JsonUtility.ToJson(state, true);
        File.WriteAllText(statePath, json);
    }

    private DownloadState LoadDownloadState()
    {
        string statePath = Path.Combine(videoCacheDir, "download_state.json");
        if (File.Exists(statePath))
        {
            string json = File.ReadAllText(statePath);
            return JsonUtility.FromJson<DownloadState>(json);
        }
        return null;
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