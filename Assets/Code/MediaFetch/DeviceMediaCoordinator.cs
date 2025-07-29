using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityAtoms.BaseAtoms;

/// <summary>
/// DeviceMediaCoordinator manages the coordination between image and video media managers
/// to prevent conflicts when both are polling the same endpoint. It also manages the 
/// visibility of render GameObjects to show/hide the appropriate render components.
/// 
/// Key Features:
/// - Single polling source to determine active media type
/// - Automatic activation/deactivation of image/video managers
/// - Control of render GameObjects for proper visual display
/// - Comprehensive validation and debugging capabilities
/// </summary>
public class DeviceMediaCoordinator : MonoBehaviour
{
    private const string LOG_TAG = "[DeviceMediaCoordinator]";

    [Header("Media Managers")]
    [Tooltip("Reference to the Image Manager")]
    public DeviceSpecificImageManager imageManager;

    [Tooltip("Reference to the Video Manager")]
    public DeviceSpecificVideoManager videoManager;

    [Header("Render GameObjects")]
    [Tooltip("GameObject containing the Image Render components (will be activated/deactivated)")]
    public GameObject imageRenderGameObject;

    [Tooltip("GameObject containing the Video Render components (will be activated/deactivated)")]
    public GameObject videoRenderGameObject;

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

    [Header("UI Toolkit Overlay")]
    public UIDocument overlayDocument;

    // UI Elements
    private Label coordinatorStatus;
    private Label activeMediaType;
    private Label lastCoordinatorUpdate;
    private Button manualCoordinatorPoll;
    private Button toggleCoordinatorPolling;

    // State Management
    private bool isPolling = false;
    private bool isConnected = false;
    private ActiveMediaType currentActiveType = ActiveMediaType.None;
    private int lastImageId = 0;
    private int lastVideoId = 0;

    public enum ActiveMediaType
    {
        None,
        Image,
        Video
    }

    [System.Serializable]
    public class DeviceMediaResponse
    {
        public bool success;
        public MediaContainer media;
        public string mediaType;
        public string timestamp;
        public int userId;
        public string deviceId;
    }

    [System.Serializable]
    public class MediaContainer
    {
        public MediaInfo image;
        public MediaInfo video;
    }

    [System.Serializable]
    public class MediaInfo
    {
        public int id;
        public string filename;
        public string originalName;
        public string url;
        public string mimeType;
        public long fileSize;
        public bool isActive;
        public string assignedAt;
        public string activatedAt;
    }

    void Start()
    {
        Debug.Log($"{LOG_TAG} DeviceMediaCoordinator starting...");

        InitializeOverlay();
        ValidateManagers();
        InitializeRenderGameObjects();

        if (string.IsNullOrEmpty(serverURL))
        {
            serverURL = "https://unity-server-control-patrykbroda.replit.app";
        }

        // Disable individual manager polling to prevent conflicts
        DisableIndividualManagerPolling();

        UpdateCoordinatorStatus("Initializing...");

        if (connectionManager == null)
        {
            connectionManager = FindFirstObjectByType<ConnectionManager>();
        }

        StartCoroutine(WaitForConnectionThenStartCoordination());
    }

    private void InitializeOverlay()
    {
        if (overlayDocument == null)
        {
            overlayDocument = GetComponent<UIDocument>();
        }

        if (overlayDocument != null)
        {
            VisualElement root = overlayDocument.rootVisualElement;

            coordinatorStatus = root.Q<Label>("coordinator-status");
            activeMediaType = root.Q<Label>("coordinator-active-type");
            lastCoordinatorUpdate = root.Q<Label>("coordinator-last-update");
            manualCoordinatorPoll = root.Q<Button>("coordinator-manual-poll");
            toggleCoordinatorPolling = root.Q<Button>("coordinator-toggle-polling");

            if (manualCoordinatorPoll != null)
                manualCoordinatorPoll.clicked += ManualPoll;

            if (toggleCoordinatorPolling != null)
                toggleCoordinatorPolling.clicked += TogglePolling;

            Debug.Log($"{LOG_TAG} UI overlay initialized");
        }
    }

    private void ValidateManagers()
    {
        if (imageManager == null)
        {
            imageManager = FindFirstObjectByType<DeviceSpecificImageManager>();
            if (imageManager == null)
            {
                Debug.LogWarning($"{LOG_TAG} No DeviceSpecificImageManager found!");
            }
        }

        if (videoManager == null)
        {
            videoManager = FindFirstObjectByType<DeviceSpecificVideoManager>();
            if (videoManager == null)
            {
                Debug.LogWarning($"{LOG_TAG} No DeviceSpecificVideoManager found!");
            }
        }

        // Validate render GameObjects
        if (imageRenderGameObject == null)
        {
            Debug.LogWarning($"{LOG_TAG} No Image Render GameObject assigned! Please assign it for proper show/hide functionality.");
        }

        if (videoRenderGameObject == null)
        {
            Debug.LogWarning($"{LOG_TAG} No Video Render GameObject assigned! Please assign it for proper show/hide functionality.");
        }

        Debug.Log($"{LOG_TAG} Managers - Image: {(imageManager != null ? "✅" : "❌")}, Video: {(videoManager != null ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG} Render Objects - Image: {(imageRenderGameObject != null ? "✅" : "❌")}, Video: {(videoRenderGameObject != null ? "✅" : "❌")}");
    }

    private void InitializeRenderGameObjects()
    {
        Debug.Log($"{LOG_TAG} Initializing render GameObjects...");

        // Start with both render objects disabled until we determine what should be active
        if (imageRenderGameObject != null)
        {
            imageRenderGameObject.SetActive(false);
            Debug.Log($"{LOG_TAG} Image Render GameObject set to inactive (initial state)");
        }

        if (videoRenderGameObject != null)
        {
            videoRenderGameObject.SetActive(false);
            Debug.Log($"{LOG_TAG} Video Render GameObject set to inactive (initial state)");
        }

        Debug.Log($"{LOG_TAG} Render GameObjects initialization completed");
    }

    private void DisableIndividualManagerPolling()
    {
        // Stop any existing polling from individual managers
        if (imageManager != null)
        {
            imageManager.StopDeviceMediaPolling();
            Debug.Log($"{LOG_TAG} Stopped ImageManager polling");
        }

        if (videoManager != null)
        {
            videoManager.StopDeviceVideoPolling();
            Debug.Log($"{LOG_TAG} Stopped VideoManager polling");
        }
    }

    private IEnumerator WaitForConnectionThenStartCoordination()
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

        Debug.Log($"{LOG_TAG} Connection established! Starting media coordination...");
        isConnected = true;
        StartMediaCoordination();
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
            Debug.Log($"{LOG_TAG} Could not check connection status: {e.Message}");
        }

        return true;
    }

    public void StartMediaCoordination()
    {
        if (isPolling)
        {
            Debug.Log($"{LOG_TAG} Already coordinating, skipping start");
            return;
        }

        Debug.Log($"{LOG_TAG} Starting media coordination every {pollingInterval} seconds");
        isPolling = true;
        UpdateCoordinatorStatus("Coordinating media...");
        UpdateOverlayInfo();

        StartCoroutine(MediaCoordinationLoop());
    }

    public void StopMediaCoordination()
    {
        Debug.Log($"{LOG_TAG} Stopping media coordination");
        isPolling = false;

        // Also stop individual managers
        DisableIndividualManagerPolling();

        UpdateCoordinatorStatus("Coordination stopped");
        UpdateOverlayInfo();
    }

    private IEnumerator MediaCoordinationLoop()
    {
        Debug.Log($"{LOG_TAG} Media coordination loop started");

        while (isPolling && isConnected)
        {
            yield return StartCoroutine(CheckActiveMediaType());
            yield return new WaitForSeconds(pollingInterval);
        }

        Debug.Log($"{LOG_TAG} Media coordination loop ended");
    }

    private IEnumerator CheckActiveMediaType()
    {
        string url = $"{serverURL}/api/device/{DeviceId}/media";
        Debug.Log($"{LOG_TAG} Checking active media type: {url}");

        string authToken = GetAuthToken();
        if (string.IsNullOrEmpty(authToken))
        {
            Debug.LogError($"{LOG_TAG} ❌ No auth token found!");
            UpdateCoordinatorStatus("No authentication token");
            yield break;
        }

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("Authorization", "Bearer " + authToken);
            www.SetRequestHeader("Cache-Control", "no-cache");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{LOG_TAG} Request failed - {www.error}");

                if (www.responseCode == 401)
                {
                    UpdateCoordinatorStatus("Authentication expired");
                    HandleTokenExpired();
                    yield break;
                }

                UpdateCoordinatorStatus($"Request failed: {www.error}");
            }
            else
            {
                yield return StartCoroutine(ProcessMediaResponse(www.downloadHandler.text));
            }
        }

        UpdateOverlayInfo();
    }

    private IEnumerator ProcessMediaResponse(string rawResponse)
    {
        Debug.Log($"{LOG_TAG} Processing media response...");

        DeviceMediaResponse response = null;
        try
        {
            response = JsonUtility.FromJson<DeviceMediaResponse>(rawResponse);
            Debug.Log($"{LOG_TAG} ✅ Response parsed successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LOG_TAG} JSON parsing failed: {e.Message}");
            UpdateCoordinatorStatus("JSON parsing failed");
            yield break;
        }

        if (response == null)
        {
            Debug.LogError($"{LOG_TAG} Could not parse response");
            yield break;
        }

        // Validate user and device IDs
        if (!ValidateResponse(response))
        {
            yield break;
        }

        // Determine which media type is active
        ActiveMediaType detectedType = DetermineActiveMediaType(response);

        Debug.Log($"{LOG_TAG} Detected active media type: {detectedType}");
        Debug.Log($"{LOG_TAG} Current active type: {currentActiveType}");

        // Only coordinate if there's a change or if we need to ensure proper state
        if (detectedType != currentActiveType || ShouldForceCoordination(response))
        {
            Debug.Log($"{LOG_TAG} Coordinating media managers - switching to: {detectedType}");
            yield return StartCoroutine(CoordinateManagers(detectedType, response));
            currentActiveType = detectedType;
        }
        else
        {
            Debug.Log($"{LOG_TAG} No coordination needed - media type unchanged");
        }

        UpdateCoordinatorStatus($"Active: {GetActiveTypeDisplayName(currentActiveType)}");
    }

    private bool ValidateResponse(DeviceMediaResponse response)
    {
        // Validate user ID
        if (connectionManager?.loginData != null)
        {
            int expectedUserId = connectionManager.loginData.UserId;
            if (response.userId != expectedUserId)
            {
                Debug.LogError($"{LOG_TAG} 🚨 USER ID MISMATCH! Expected: {expectedUserId}, Got: {response.userId}");
                UpdateCoordinatorStatus("🚨 SECURITY ERROR: User ID mismatch!");
                return false;
            }
        }

        // Validate device ID
        if (!string.IsNullOrEmpty(response.deviceId) && response.deviceId != DeviceId)
        {
            Debug.LogError($"{LOG_TAG} 🚨 DEVICE ID MISMATCH! Expected: {DeviceId}, Got: {response.deviceId}");
            UpdateCoordinatorStatus("🚨 DEVICE ID MISMATCH!");
            return false;
        }

        return true;
    }

    private ActiveMediaType DetermineActiveMediaType(DeviceMediaResponse response)
    {
        bool hasActiveImage = response.media?.image != null && response.media.image.id > 0;
        bool hasActiveVideo = response.media?.video != null && response.media.video.id > 0;

        Debug.Log($"{LOG_TAG} Media analysis - Image: {(hasActiveImage ? $"✅ ID:{response.media.image.id}" : "❌")}, Video: {(hasActiveVideo ? $"✅ ID:{response.media.video.id}" : "❌")}");

        // If both are present, prioritize based on which is marked as active or use video as default
        if (hasActiveImage && hasActiveVideo)
        {
            Debug.LogWarning($"{LOG_TAG} Both image and video present! This shouldn't happen according to backend logic.");

            // Check if either is explicitly marked as active
            bool imageActive = response.media.image.isActive;
            bool videoActive = response.media.video.isActive;

            if (videoActive && !imageActive)
            {
                return ActiveMediaType.Video;
            }
            else if (imageActive && !videoActive)
            {
                return ActiveMediaType.Image;
            }
            else
            {
                // Fallback: prefer video if both or neither are marked active
                Debug.LogWarning($"{LOG_TAG} Ambiguous active state, defaulting to Video");
                return ActiveMediaType.Video;
            }
        }
        else if (hasActiveVideo)
        {
            return ActiveMediaType.Video;
        }
        else if (hasActiveImage)
        {
            return ActiveMediaType.Image;
        }
        else
        {
            return ActiveMediaType.None;
        }
    }

    private bool ShouldForceCoordination(DeviceMediaResponse response)
    {
        // Force coordination if media IDs have changed even if type is the same
        int currentImageId = response.media?.image?.id ?? 0;
        int currentVideoId = response.media?.video?.id ?? 0;

        bool imageIdChanged = currentImageId != lastImageId;
        bool videoIdChanged = currentVideoId != lastVideoId;

        if (imageIdChanged || videoIdChanged)
        {
            Debug.Log($"{LOG_TAG} Media IDs changed - Image: {lastImageId} -> {currentImageId}, Video: {lastVideoId} -> {currentVideoId}");
            lastImageId = currentImageId;
            lastVideoId = currentVideoId;
            return true;
        }

        return false;
    }

    private IEnumerator CoordinateManagers(ActiveMediaType activeType, DeviceMediaResponse response)
    {
        Debug.Log($"{LOG_TAG} Coordinating managers for type: {activeType}");

        switch (activeType)
        {
            case ActiveMediaType.Image:
                yield return StartCoroutine(ActivateImageManager(response));
                yield return StartCoroutine(DeactivateVideoManager());
                break;

            case ActiveMediaType.Video:
                yield return StartCoroutine(DeactivateImageManager());
                yield return StartCoroutine(ActivateVideoManager(response));
                break;

            case ActiveMediaType.None:
                yield return StartCoroutine(DeactivateImageManager());
                yield return StartCoroutine(DeactivateVideoManager());
                break;
        }

        Debug.Log($"{LOG_TAG} Coordination completed for type: {activeType}");
    }

    private IEnumerator ActivateImageManager(DeviceMediaResponse response)
    {
        if (imageManager == null)
        {
            Debug.LogWarning($"{LOG_TAG} Cannot activate ImageManager - not assigned");
            yield break;
        }

        Debug.Log($"{LOG_TAG} Activating ImageManager...");

        // Stop any existing polling
        imageManager.StopDeviceMediaPolling();

        // Give it a moment to stop
        yield return new WaitForSeconds(0.1f);

        // Start fresh polling
        imageManager.StartDeviceMediaPolling();

        Debug.Log($"{LOG_TAG} ✅ ImageManager activated");
    }

    private IEnumerator ActivateVideoManager(DeviceMediaResponse response)
    {
        if (videoManager == null)
        {
            Debug.LogWarning($"{LOG_TAG} Cannot activate VideoManager - not assigned");
            yield break;
        }

        Debug.Log($"{LOG_TAG} Activating VideoManager...");

        // Stop any existing polling
        videoManager.StopDeviceVideoPolling();

        // Give it a moment to stop
        yield return new WaitForSeconds(0.1f);

        // Start fresh polling
        videoManager.StartDeviceVideoPolling();

        Debug.Log($"{LOG_TAG} ✅ VideoManager activated");
    }

    private IEnumerator DeactivateImageManager()
    {
        if (imageManager == null) yield break;

        Debug.Log($"{LOG_TAG} Deactivating ImageManager...");

        // Disable image render GameObject
        if (imageRenderGameObject != null)
        {
            imageRenderGameObject.SetActive(false);
            Debug.Log($"{LOG_TAG} ✅ Image Render GameObject disabled");
        }

        imageManager.StopDeviceMediaPolling();
        yield return new WaitForSeconds(0.1f);
        Debug.Log($"{LOG_TAG} ✅ ImageManager deactivated");
    }

    private IEnumerator DeactivateVideoManager()
    {
        if (videoManager == null) yield break;

        Debug.Log($"{LOG_TAG} Deactivating VideoManager...");

        // Disable video render GameObject
        if (videoRenderGameObject != null)
        {
            videoRenderGameObject.SetActive(false);
            Debug.Log($"{LOG_TAG} ✅ Video Render GameObject disabled");
        }

        videoManager.StopDeviceVideoPolling();
        yield return new WaitForSeconds(0.1f);
        Debug.Log($"{LOG_TAG} ✅ VideoManager deactivated");
    }

    private string GetAuthToken()
    {
        if (connectionManager != null && connectionManager.loginData != null && connectionManager.loginData.IsTokenValid())
        {
            return connectionManager.loginData.AuthToken;
        }
        else if (!string.IsNullOrEmpty(PlayerPrefs.GetString("auth_token", "")))
        {
            return PlayerPrefs.GetString("auth_token", "");
        }
        else if (!string.IsNullOrEmpty(PlayerPrefs.GetString("AuthToken", "")))
        {
            return PlayerPrefs.GetString("AuthToken", "");
        }

        return "";
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

        StopMediaCoordination();
    }

    private void UpdateCoordinatorStatus(string message)
    {
        if (coordinatorStatus != null)
        {
            coordinatorStatus.text = $"🎯 Coordinator: {message}";
        }

        Debug.Log($"{LOG_TAG} Status: {message}");
    }

    private void UpdateOverlayInfo()
    {
        if (activeMediaType != null)
        {
            activeMediaType.text = $"Active Type: {GetActiveTypeDisplayName(currentActiveType)}";
        }

        if (lastCoordinatorUpdate != null)
        {
            lastCoordinatorUpdate.text = $"Last Update: {System.DateTime.Now.ToString("HH:mm:ss")}";
        }

        if (toggleCoordinatorPolling != null)
        {
            toggleCoordinatorPolling.text = isPolling ? "Stop Coordination" : "Start Coordination";
        }
    }

    private string GetActiveTypeDisplayName(ActiveMediaType type)
    {
        return type switch
        {
            ActiveMediaType.Image => "🖼️ Image",
            ActiveMediaType.Video => "🎥 Video",
            ActiveMediaType.None => "❌ None",
            _ => "❓ Unknown"
        };
    }

    // Public Methods
    public void ManualPoll()
    {
        Debug.Log($"{LOG_TAG} Manual coordination poll triggered");
        if (isConnected)
        {
            StartCoroutine(CheckActiveMediaType());
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
            StopMediaCoordination();
        }
        else
        {
            StartMediaCoordination();
        }
    }

    public void SetDeviceId(string newDeviceId)
    {
        if (deviceIdVariable != null)
        {
            deviceIdVariable.Value = newDeviceId;
            Debug.Log($"{LOG_TAG} Device ID changed to: {newDeviceId}");

            // Update managers as well
            imageManager?.SetDeviceId(newDeviceId);
            videoManager?.SetDeviceId(newDeviceId);
        }
    }

    // Debug Methods
    [ContextMenu("Force Emergency Stop All Managers")]
    public void ForceEmergencyStopAllManagers()
    {
        Debug.Log($"{LOG_TAG} 🚨 EMERGENCY STOP: Stopping all managers");

        StopMediaCoordination();
        DisableIndividualManagerPolling();

        // Emergency stop video operations
        if (videoManager != null)
        {
            videoManager.EmergencyStopAllVideoOperations();
        }

        currentActiveType = ActiveMediaType.None;
        UpdateCoordinatorStatus("Emergency stop completed");
        UpdateOverlayInfo();
    }

    [ContextMenu("Debug Coordination Status")]
    public void DebugCoordinationStatus()
    {
        Debug.Log($"{LOG_TAG} === COORDINATION STATUS DEBUG ===");
        Debug.Log($"{LOG_TAG} Device ID: '{DeviceId}'");
        Debug.Log($"{LOG_TAG} Is Polling: {(isPolling ? "✅ Yes" : "❌ No")}");
        Debug.Log($"{LOG_TAG} Is Connected: {(isConnected ? "✅ Yes" : "❌ No")}");
        Debug.Log($"{LOG_TAG} Current Active Type: {currentActiveType}");
        Debug.Log($"{LOG_TAG} Last Image ID: {lastImageId}");
        Debug.Log($"{LOG_TAG} Last Video ID: {lastVideoId}");
        Debug.Log($"{LOG_TAG} ImageManager: {(imageManager != null ? "✅ Assigned" : "❌ Missing")}");
        Debug.Log($"{LOG_TAG} VideoManager: {(videoManager != null ? "✅ Assigned" : "❌ Missing")}");
        Debug.Log($"{LOG_TAG} Image Render GameObject: {(imageRenderGameObject != null ? $"✅ Assigned ({(imageRenderGameObject.activeInHierarchy ? "Active" : "Inactive")})" : "❌ Missing")}");
        Debug.Log($"{LOG_TAG} Video Render GameObject: {(videoRenderGameObject != null ? $"✅ Assigned ({(videoRenderGameObject.activeInHierarchy ? "Active" : "Inactive")})" : "❌ Missing")}");
        Debug.Log($"{LOG_TAG} ConnectionManager: {(connectionManager != null ? "✅ Assigned" : "❌ Missing")}");
        Debug.Log($"{LOG_TAG} === COORDINATION STATUS DEBUG END ===");
    }

    void OnDestroy()
    {
        Debug.Log($"{LOG_TAG} OnDestroy() called");
        StopMediaCoordination();
    }
}