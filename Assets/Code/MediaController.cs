using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityAtoms.BaseAtoms;

public enum ManualOverrideState
{
    Automatic,
    DeviceImage,
    DeviceVideo,
    GlobalImage,
    GlobalVideo,
    None
}

public class MediaController : MonoBehaviour
{
    private const string LOG_TAG = "[MediaController]";

    [Header("RawImage Controls")]
    [Tooltip("UI RawImage component for displaying images")]
    public RawImage imageRawImage;

    [Tooltip("UI RawImage component for displaying videos")]
    public RawImage videoRawImage;

    [Header("RenderTexture Sources")]
    [Tooltip("RenderTexture output from device-specific image manager")]
    public RenderTexture deviceImageRenderTexture;

    [Tooltip("RenderTexture output from device-specific video manager")]
    public RenderTexture deviceVideoRenderTexture;

    [Tooltip("RenderTexture output from global image manager")]
    public RenderTexture globalImageRenderTexture;

    [Tooltip("RenderTexture output from global video manager")]
    public RenderTexture globalVideoRenderTexture;

    [Header("Canvas Fitting")]
    [Tooltip("Canvas containing the UI elements")]
    public Canvas displayCanvas;

    [Tooltip("Automatically fit RawImages to canvas height while maintaining aspect ratio")]
    public bool autoFitToCanvas = true;

    [Header("Media Managers")]
    [Tooltip("Device-specific image manager")]
    public DeviceSpecificImageManager deviceImageManager;

    [Tooltip("Device-specific video manager")]
    public DeviceSpecificVideoManager deviceVideoManager;

    [Tooltip("Global image manager")]
    public ImageManager globalImageManager;

    [Tooltip("Global video manager")]
    public VideoManager globalVideoManager;

    [Header("Device Configuration")]
    [Tooltip("Unity Atoms StringVariable for Device ID")]
    public StringVariable deviceIdVariable;

    [Header("Manual Override")]
    [Tooltip("Enable manual override to force specific manager (disables automatic coordination)")]
    public bool enableManualOverride = false;

    [Tooltip("Manually select which manager to use (when manual override is enabled)")]
    public ManualOverrideState manualOverrideState = ManualOverrideState.Automatic;

    [Header("Coordination Settings")]
    [Tooltip("How often to check which manager should be active (seconds) - only when manual override is disabled")]
    public float checkInterval = 3f;

    [Tooltip("Prefer device-specific content over global content")]
    public bool prioritizeDeviceContent = true;

    [Tooltip("Automatically connect to manager outputs")]
    public bool autoConnectToManagers = true;

    [Header("Connection Reference")]
    public ConnectionManager connectionManager;

    [Header("Visibility Controls (Inspector Only)")]
    [Tooltip("Show/hide the image RawImage component")]
    public bool showImageRawImage = true;

    [Tooltip("Show/hide the video RawImage component")]
    public bool showVideoRawImage = false;

    // State Management
    private MonoBehaviour currentActiveManager = null;
    private MediaType currentMediaType = MediaType.None;
    private bool isCoordinationActive = false;
    private Coroutine coordinationCoroutine;
    private ManualOverrideState lastManualState = ManualOverrideState.Automatic;

    // Auto-connection tracking
    private RenderTexture currentImageRenderTexture = null;
    private RenderTexture currentVideoRenderTexture = null;

    public enum MediaType
    {
        None,
        DeviceImage,
        DeviceVideo,
        GlobalImage,
        GlobalVideo
    }

    // Public Properties
    public MonoBehaviour CurrentActiveManager => currentActiveManager;
    public MediaType CurrentMediaType => currentMediaType;
    public bool HasActiveManager => currentActiveManager != null;
    public bool IsCoordinationActive => isCoordinationActive;
    public bool IsManualOverrideEnabled => enableManualOverride;
    public ManualOverrideState CurrentManualOverrideState => manualOverrideState;

    // Events for external integration
    public System.Action<MediaType> OnMediaTypeChanged;
    public System.Action<MonoBehaviour> OnActiveManagerChanged;

    void Start()
    {
        Debug.Log($"{LOG_TAG} Starting Media Controller...");

        SetupRawImagesForCanvasFitting();
        UpdateRawImageVisibility();

        if (autoConnectToManagers)
        {
            AutoConnectToManagerOutputs();
        }

        StartCoroutine(WaitForConnectionThenStartCoordination());
    }

    void OnValidate()
    {
        if (Application.isPlaying)
        {
            UpdateRawImageVisibility();

            // Handle manual override changes made in inspector during play mode
            if (enableManualOverride && manualOverrideState != lastManualState)
            {
                CheckManualOverrideChanges();
            }
        }
    }

    void Update()
    {
        // Auto-update textures from connected sources
        if (autoConnectToManagers)
        {
            UpdateTexturesFromManagers();
        }

        // Check for manual override changes
        CheckManualOverrideChanges();
    }

    private void CheckManualOverrideChanges()
    {
        // Check if manual override state changed
        if (enableManualOverride && manualOverrideState != lastManualState)
        {
            Debug.Log($"{LOG_TAG} Manual override changed to: {manualOverrideState}");
            ApplyManualOverride();
            lastManualState = manualOverrideState;
        }
        // Check if manual override was just enabled/disabled
        else if (enableManualOverride && !isCoordinationActive)
        {
            // Manual override just enabled - stop coordination
            StopCoordination();
            ApplyManualOverride();
        }
        else if (!enableManualOverride && !isCoordinationActive)
        {
            // Manual override just disabled - restart coordination
            StartCoordination();
        }
    }

    private void ApplyManualOverride()
    {
        MonoBehaviour targetManager = null;
        MediaType targetType = MediaType.None;

        switch (manualOverrideState)
        {
            case ManualOverrideState.DeviceImage:
                if (deviceImageManager != null)
                {
                    targetManager = deviceImageManager;
                    targetType = MediaType.DeviceImage;
                }
                else
                {
                    Debug.LogWarning($"{LOG_TAG} Device Image Manager not assigned for manual override");
                }
                break;

            case ManualOverrideState.DeviceVideo:
                if (deviceVideoManager != null)
                {
                    targetManager = deviceVideoManager;
                    targetType = MediaType.DeviceVideo;
                }
                else
                {
                    Debug.LogWarning($"{LOG_TAG} Device Video Manager not assigned for manual override");
                }
                break;

            case ManualOverrideState.GlobalImage:
                if (globalImageManager != null)
                {
                    targetManager = globalImageManager;
                    targetType = MediaType.GlobalImage;
                }
                else
                {
                    Debug.LogWarning($"{LOG_TAG} Global Image Manager not assigned for manual override");
                }
                break;

            case ManualOverrideState.GlobalVideo:
                if (globalVideoManager != null)
                {
                    targetManager = globalVideoManager;
                    targetType = MediaType.GlobalVideo;
                }
                else
                {
                    Debug.LogWarning($"{LOG_TAG} Global Video Manager not assigned for manual override");
                }
                break;

            case ManualOverrideState.None:
                targetManager = null;
                targetType = MediaType.None;
                break;

            case ManualOverrideState.Automatic:
                // This shouldn't happen when manual override is enabled
                Debug.LogWarning($"{LOG_TAG} Manual override is enabled but state is set to Automatic");
                return;
        }

        SwitchToManager(targetManager, targetType);
        Debug.Log($"{LOG_TAG} Manual override applied: {manualOverrideState}");
    }

    private IEnumerator WaitForConnectionThenStartCoordination()
    {
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

        Debug.Log($"{LOG_TAG} Connection ready! Starting coordination...");

        if (enableManualOverride)
        {
            Debug.Log($"{LOG_TAG} Manual override enabled at startup - applying: {manualOverrideState}");
            ApplyManualOverride();
            lastManualState = manualOverrideState;
        }
        else
        {
            StartCoordination();
        }
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
                return connected && authenticated;
            }
        }
        catch (System.Exception e)
        {
            Debug.Log($"{LOG_TAG} Could not check connection status: {e.Message}");
        }

        return true;
    }

    private void StartCoordination()
    {
        if (enableManualOverride)
        {
            Debug.Log($"{LOG_TAG} Manual override enabled - skipping automatic coordination");
            return;
        }

        StopAllManagers();
        isCoordinationActive = true;
        coordinationCoroutine = StartCoroutine(CoordinationLoop());
        Debug.Log($"{LOG_TAG} Automatic coordination started");
    }

    private IEnumerator CoordinationLoop()
    {
        while (isCoordinationActive && !enableManualOverride)
        {
            CheckAndSwitchOptimalManager();
            yield return new WaitForSeconds(checkInterval);
        }

        if (enableManualOverride)
        {
            Debug.Log($"{LOG_TAG} Coordination loop stopped - manual override enabled");
        }
    }

    private void CheckAndSwitchOptimalManager()
    {
        MonoBehaviour optimalManager = DetermineOptimalManager();
        MediaType optimalType = GetManagerType(optimalManager);

        if (optimalManager != currentActiveManager)
        {
            SwitchToManager(optimalManager, optimalType);
        }
    }

    private MonoBehaviour DetermineOptimalManager()
    {
        bool hasDeviceId = HasValidDeviceId();

        // Priority order based on prioritizeDeviceContent setting
        if (prioritizeDeviceContent && hasDeviceId)
        {
            // Device content first
            if (deviceImageManager != null && ManagerHasContent(deviceImageManager))
                return deviceImageManager;
            if (deviceVideoManager != null && ManagerHasContent(deviceVideoManager))
                return deviceVideoManager;
        }

        // Global content
        if (globalImageManager != null && ManagerHasContent(globalImageManager))
            return globalImageManager;
        if (globalVideoManager != null && ManagerHasContent(globalVideoManager))
            return globalVideoManager;

        // Device content as fallback (if not prioritized)
        if (!prioritizeDeviceContent && hasDeviceId)
        {
            if (deviceImageManager != null && ManagerHasContent(deviceImageManager))
                return deviceImageManager;
            if (deviceVideoManager != null && ManagerHasContent(deviceVideoManager))
                return deviceVideoManager;
        }

        return null;
    }

    private bool ManagerHasContent(MonoBehaviour manager)
    {
        if (manager == null) return false;

        try
        {
            if (manager is DeviceSpecificImageManager dim)
            {
                var field = typeof(DeviceSpecificImageManager).GetField("currentMediaId",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return field != null && (int)field.GetValue(dim) > 0;
            }
            else if (manager is DeviceSpecificVideoManager dvm)
            {
                var field = typeof(DeviceSpecificVideoManager).GetField("currentVideoId",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return field != null && (int)field.GetValue(dvm) > 0;
            }
            else if (manager is ImageManager im)
            {
                var field = typeof(ImageManager).GetField("currentImageId",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return field != null && !string.IsNullOrEmpty((string)field.GetValue(im));
            }
            else if (manager is VideoManager vm)
            {
                var field = typeof(VideoManager).GetField("currentVideoId",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return field != null && !string.IsNullOrEmpty((string)field.GetValue(vm));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"{LOG_TAG} Could not check content for {manager.GetType().Name}: {e.Message}");
        }

        return false;
    }

    private MediaType GetManagerType(MonoBehaviour manager)
    {
        if (manager == null) return MediaType.None;

        if (manager == deviceImageManager) return MediaType.DeviceImage;
        if (manager == deviceVideoManager) return MediaType.DeviceVideo;
        if (manager == globalImageManager) return MediaType.GlobalImage;
        if (manager == globalVideoManager) return MediaType.GlobalVideo;

        return MediaType.None;
    }

    private void SwitchToManager(MonoBehaviour newManager, MediaType newType)
    {
        Debug.Log($"{LOG_TAG} Switching from {currentMediaType} to {newType}");

        // Stop current manager
        if (currentActiveManager != null)
        {
            StopManager(currentActiveManager);
        }

        // Update display based on new type
        UpdateDisplayForMediaType(newType);

        // Start new manager
        if (newManager != null)
        {
            StartManager(newManager);
        }

        // Update state
        currentActiveManager = newManager;
        currentMediaType = newType;

        // Auto-connect to new manager's outputs
        if (autoConnectToManagers)
        {
            ConnectToManagerOutput(newManager);
        }

        // Fire events
        OnActiveManagerChanged?.Invoke(newManager);
        OnMediaTypeChanged?.Invoke(newType);

        string managerName = newManager?.GetType().Name ?? "None";
        Debug.Log($"{LOG_TAG} Switched to {managerName} ({newType})");
    }

    private void UpdateDisplayForMediaType(MediaType mediaType)
    {
        switch (mediaType)
        {
            case MediaType.DeviceImage:
            case MediaType.GlobalImage:
                ShowOnlyImage();
                break;

            case MediaType.DeviceVideo:
            case MediaType.GlobalVideo:
                ShowOnlyVideo();
                break;

            case MediaType.None:
            default:
                HideBoth();
                break;
        }
    }

    private void StartManager(MonoBehaviour manager)
    {
        if (manager == null) return;

        manager.enabled = true;

        if (manager is DeviceSpecificImageManager dim)
        {
            dim.StartDeviceMediaPolling();
        }
        else if (manager is DeviceSpecificVideoManager dvm)
        {
            dvm.StartDeviceVideoPolling();
        }
        else if (manager is ImageManager im)
        {
            im.StartImagePolling();
        }
        else if (manager is VideoManager vm)
        {
            vm.StartVideoPolling();
        }

        Debug.Log($"{LOG_TAG} Started {manager.GetType().Name}");
    }

    private void StopManager(MonoBehaviour manager)
    {
        if (manager == null) return;

        if (manager is DeviceSpecificImageManager dim)
        {
            dim.StopDeviceMediaPolling();
        }
        else if (manager is DeviceSpecificVideoManager dvm)
        {
            dvm.StopDeviceVideoPolling();
        }
        else if (manager is ImageManager im)
        {
            im.StopImagePolling();
        }
        else if (manager is VideoManager vm)
        {
            vm.StopVideoPolling();
        }

        manager.enabled = false;
    }

    private void StopAllManagers()
    {
        StopManager(deviceImageManager);
        StopManager(deviceVideoManager);
        StopManager(globalImageManager);
        StopManager(globalVideoManager);

        currentActiveManager = null;
        currentMediaType = MediaType.None;
    }

    private bool HasValidDeviceId()
    {
        return deviceIdVariable != null && !string.IsNullOrEmpty(deviceIdVariable.Value);
    }

    // Auto-connection methods
    private void AutoConnectToManagerOutputs()
    {
        // Try to auto-connect to manager RenderTextures if not manually assigned
        if (deviceImageRenderTexture == null && deviceImageManager != null)
        {
            var rtField = typeof(DeviceSpecificImageManager).GetField("outputRenderTexture");
            if (rtField != null)
            {
                deviceImageRenderTexture = (RenderTexture)rtField.GetValue(deviceImageManager);
                Debug.Log($"{LOG_TAG} Auto-connected to device image RenderTexture");
            }
        }

        if (deviceVideoRenderTexture == null && deviceVideoManager != null)
        {
            // Try to find RenderTexture field in DeviceSpecificVideoManager
            var rtField = typeof(DeviceSpecificVideoManager).GetField("outputRenderTexture");
            if (rtField == null)
            {
                // Try alternative field names
                rtField = typeof(DeviceSpecificVideoManager).GetField("videoRenderTexture");
            }
            if (rtField == null)
            {
                rtField = typeof(DeviceSpecificVideoManager).GetField("renderTexture");
            }

            if (rtField != null)
            {
                deviceVideoRenderTexture = (RenderTexture)rtField.GetValue(deviceVideoManager);
                Debug.Log($"{LOG_TAG} Auto-connected to device video RenderTexture");
            }
            else
            {
                Debug.LogWarning($"{LOG_TAG} Could not find RenderTexture field in DeviceSpecificVideoManager - please assign manually");
            }
        }

        if (globalImageRenderTexture == null && globalImageManager != null)
        {
            var rtField = typeof(ImageManager).GetField("outputRenderTexture");
            if (rtField != null)
            {
                globalImageRenderTexture = (RenderTexture)rtField.GetValue(globalImageManager);
                Debug.Log($"{LOG_TAG} Auto-connected to global image RenderTexture");
            }
        }

        if (globalVideoRenderTexture == null && globalVideoManager != null)
        {
            // Try to find RenderTexture field in VideoManager (might be named differently)
            var rtField = typeof(VideoManager).GetField("outputRenderTexture");
            if (rtField == null)
            {
                // Try alternative field names
                rtField = typeof(VideoManager).GetField("videoRenderTexture");
            }
            if (rtField == null)
            {
                rtField = typeof(VideoManager).GetField("renderTexture");
            }

            if (rtField != null)
            {
                globalVideoRenderTexture = (RenderTexture)rtField.GetValue(globalVideoManager);
                Debug.Log($"{LOG_TAG} Auto-connected to global video RenderTexture");
            }
            else
            {
                Debug.LogWarning($"{LOG_TAG} Could not find RenderTexture field in VideoManager - please assign manually");
            }
        }
    }

    private void ConnectToManagerOutput(MonoBehaviour manager)
    {
        if (manager == null)
        {
            // Clear current connections when no manager is active
            currentImageRenderTexture = null;
            currentVideoRenderTexture = null;
            return;
        }

        // Connect to the appropriate RenderTexture based on active manager
        if (manager == deviceImageManager)
        {
            currentImageRenderTexture = deviceImageRenderTexture;
            currentVideoRenderTexture = null;
            Debug.Log($"{LOG_TAG} Connected to device image RenderTexture");
        }
        else if (manager == deviceVideoManager)
        {
            currentImageRenderTexture = null;
            currentVideoRenderTexture = deviceVideoRenderTexture;
            Debug.Log($"{LOG_TAG} Connected to device video RenderTexture");
        }
        else if (manager == globalImageManager)
        {
            currentImageRenderTexture = globalImageRenderTexture;
            currentVideoRenderTexture = null;
            Debug.Log($"{LOG_TAG} Connected to global image RenderTexture");
        }
        else if (manager == globalVideoManager)
        {
            currentImageRenderTexture = null;
            currentVideoRenderTexture = globalVideoRenderTexture;
            Debug.Log($"{LOG_TAG} Connected to global video RenderTexture");
        }
    }

    private void UpdateTexturesFromManagers()
    {
        // Update image RawImage from current image RenderTexture
        if (imageRawImage != null && currentImageRenderTexture != null)
        {
            if (imageRawImage.texture != currentImageRenderTexture)
            {
                imageRawImage.texture = currentImageRenderTexture;
                if (autoFitToCanvas && imageRawImage.gameObject.activeInHierarchy)
                {
                    FitRawImageToCanvas(imageRawImage);
                }
            }
        }

        // Update video RawImage from current video RenderTexture
        if (videoRawImage != null && currentVideoRenderTexture != null)
        {
            if (videoRawImage.texture != currentVideoRenderTexture)
            {
                videoRawImage.texture = currentVideoRenderTexture;
                if (autoFitToCanvas && videoRawImage.gameObject.activeInHierarchy)
                {
                    FitRawImageToCanvas(videoRawImage);
                }
            }
        }
    }

    // Original MediaController methods (preserved for compatibility)
    private void SetupRawImagesForCanvasFitting()
    {
        SetupRawImageForCanvasFitting(imageRawImage);
        SetupRawImageForCanvasFitting(videoRawImage);
    }

    private void SetupRawImageForCanvasFitting(RawImage rawImage)
    {
        if (rawImage == null) return;

        RectTransform rawImageRect = rawImage.GetComponent<RectTransform>();
        if (rawImageRect != null)
        {
            rawImageRect.anchorMin = new Vector2(0.5f, 0.5f);
            rawImageRect.anchorMax = new Vector2(0.5f, 0.5f);
            rawImageRect.pivot = new Vector2(0.5f, 0.5f);
            rawImageRect.anchoredPosition = Vector2.zero;
        }
    }

    private void UpdateRawImageVisibility()
    {
        if (imageRawImage != null)
        {
            imageRawImage.gameObject.SetActive(showImageRawImage);
            if (showImageRawImage && autoFitToCanvas)
            {
                FitRawImageToCanvas(imageRawImage);
            }
        }

        if (videoRawImage != null)
        {
            videoRawImage.gameObject.SetActive(showVideoRawImage);
            if (showVideoRawImage && autoFitToCanvas)
            {
                FitRawImageToCanvas(videoRawImage);
            }
        }
    }

    private void FitRawImageToCanvas(RawImage rawImage)
    {
        if (rawImage == null || displayCanvas == null || rawImage.texture == null) return;

        RectTransform canvasRect = displayCanvas.GetComponent<RectTransform>();
        if (canvasRect == null) return;

        float canvasWidth = canvasRect.rect.width;
        float canvasHeight = canvasRect.rect.height;
        float textureWidth = rawImage.texture.width;
        float textureHeight = rawImage.texture.height;

        if (textureWidth <= 0 || textureHeight <= 0) return;

        float textureAspect = textureWidth / textureHeight;
        float canvasAspect = canvasWidth / canvasHeight;

        Vector2 targetSize;

        // Improved fitting: fit to canvas while maintaining aspect ratio
        if (textureAspect > canvasAspect)
        {
            // Texture is wider - fit to width
            targetSize = new Vector2(canvasWidth, canvasWidth / textureAspect);
        }
        else
        {
            // Texture is taller - fit to height  
            targetSize = new Vector2(canvasHeight * textureAspect, canvasHeight);
        }

        RectTransform rawImageRect = rawImage.GetComponent<RectTransform>();
        if (rawImageRect != null)
        {
            rawImageRect.sizeDelta = targetSize;
            rawImageRect.anchoredPosition = Vector2.zero;
        }
    }

    // Public methods to control visibility (preserved for compatibility)
    public void ShowImageRawImage(bool show)
    {
        showImageRawImage = show;
        if (imageRawImage != null)
        {
            imageRawImage.gameObject.SetActive(show);
            if (show && autoFitToCanvas)
            {
                FitRawImageToCanvas(imageRawImage);
            }
        }
    }

    public void ShowVideoRawImage(bool show)
    {
        showVideoRawImage = show;
        if (videoRawImage != null)
        {
            videoRawImage.gameObject.SetActive(show);
            if (show && autoFitToCanvas)
            {
                FitRawImageToCanvas(videoRawImage);
            }
        }
    }

    public void ShowOnlyImage()
    {
        ShowImageRawImage(true);
        ShowVideoRawImage(false);
    }

    public void ShowOnlyVideo()
    {
        ShowImageRawImage(false);
        ShowVideoRawImage(true);
    }

    public void ShowBoth()
    {
        ShowImageRawImage(true);
        ShowVideoRawImage(true);
    }

    public void HideBoth()
    {
        ShowImageRawImage(false);
        ShowVideoRawImage(false);
    }

    public void FitToCanvas()
    {
        if (autoFitToCanvas)
        {
            if (imageRawImage != null && imageRawImage.gameObject.activeInHierarchy)
            {
                FitRawImageToCanvas(imageRawImage);
            }
            if (videoRawImage != null && videoRawImage.gameObject.activeInHierarchy)
            {
                FitRawImageToCanvas(videoRawImage);
            }
        }
    }

    public void SetCanvas(Canvas canvas)
    {
        displayCanvas = canvas;
        if (autoFitToCanvas)
        {
            FitToCanvas();
        }
    }

    // New coordination methods
    public void ForceRefresh()
    {
        if (enableManualOverride)
        {
            Debug.Log($"{LOG_TAG} Force refresh requested but manual override is enabled - applying manual state: {manualOverrideState}");
            ApplyManualOverride();
        }
        else
        {
            Debug.Log($"{LOG_TAG} Force refresh requested");
            CheckAndSwitchOptimalManager();
        }
    }

    public void SetDeviceId(string deviceId)
    {
        if (deviceIdVariable != null)
        {
            deviceIdVariable.Value = deviceId;
            Debug.Log($"{LOG_TAG} Device ID set to: {deviceId}");
            ForceRefresh();
        }
    }

    public void EmergencyStop()
    {
        Debug.Log($"{LOG_TAG} 🚨 Emergency stop requested");

        // Disable manual override and stop coordination
        enableManualOverride = false;
        manualOverrideState = ManualOverrideState.Automatic;
        isCoordinationActive = false;

        if (coordinationCoroutine != null)
        {
            StopCoroutine(coordinationCoroutine);
        }

        StopAllManagers();
        HideBoth();

        // Emergency stop video operations
        deviceVideoManager?.EmergencyStopAllVideoOperations();
        globalVideoManager?.EmergencyStopAllVideoOperations();
    }

    public void StartCoordinationManually()
    {
        if (!isCoordinationActive)
        {
            StartCoordination();
        }
    }

    public void StopCoordination()
    {
        isCoordinationActive = false;
        if (coordinationCoroutine != null)
        {
            StopCoroutine(coordinationCoroutine);
        }
        Debug.Log($"{LOG_TAG} Coordination stopped");
    }

    // Manual override methods
    public void SetManualOverride(ManualOverrideState state)
    {
        enableManualOverride = true;
        manualOverrideState = state;
        Debug.Log($"{LOG_TAG} Manual override set to: {state}");
    }

    public void DisableManualOverride()
    {
        enableManualOverride = false;
        manualOverrideState = ManualOverrideState.Automatic;
        Debug.Log($"{LOG_TAG} Manual override disabled - returning to automatic coordination");
    }

    // Manual manager switching
    public void ForceDeviceImage()
    {
        if (deviceImageManager != null && HasValidDeviceId())
        {
            SwitchToManager(deviceImageManager, MediaType.DeviceImage);
        }
    }

    public void ForceDeviceVideo()
    {
        if (deviceVideoManager != null && HasValidDeviceId())
        {
            SwitchToManager(deviceVideoManager, MediaType.DeviceVideo);
        }
    }

    public void ForceGlobalImage()
    {
        if (globalImageManager != null)
        {
            SwitchToManager(globalImageManager, MediaType.GlobalImage);
        }
    }

    public void ForceGlobalVideo()
    {
        if (globalVideoManager != null)
        {
            SwitchToManager(globalVideoManager, MediaType.GlobalVideo);
        }
    }

    // Debug methods
    [ContextMenu("Debug Status")]
    public void DebugStatus()
    {
        Debug.Log($"{LOG_TAG} === MEDIA CONTROLLER STATUS ===");
        Debug.Log($"{LOG_TAG} Manual Override: {(enableManualOverride ? $"ENABLED ({manualOverrideState})" : "DISABLED")}");
        Debug.Log($"{LOG_TAG} Coordination Active: {isCoordinationActive}");
        Debug.Log($"{LOG_TAG} Current Manager: {currentActiveManager?.GetType().Name ?? "None"}");
        Debug.Log($"{LOG_TAG} Current Type: {currentMediaType}");
        Debug.Log($"{LOG_TAG} Device ID: {(HasValidDeviceId() ? deviceIdVariable.Value : "Not Set")}");
        Debug.Log($"{LOG_TAG} Auto Connect: {autoConnectToManagers}");
        Debug.Log($"{LOG_TAG} RenderTextures Assigned:");
        Debug.Log($"{LOG_TAG}   Device Image RT: {(deviceImageRenderTexture != null ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG}   Device Video RT: {(deviceVideoRenderTexture != null ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG}   Global Image RT: {(globalImageRenderTexture != null ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG}   Global Video RT: {(globalVideoRenderTexture != null ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG} Current Active RenderTextures:");
        Debug.Log($"{LOG_TAG}   Image RT: {(currentImageRenderTexture != null ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG}   Video RT: {(currentVideoRenderTexture != null ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG} Image RawImage Active: {(imageRawImage != null && imageRawImage.gameObject.activeInHierarchy)}");
        Debug.Log($"{LOG_TAG} Video RawImage Active: {(videoRawImage != null && videoRawImage.gameObject.activeInHierarchy)}");
        Debug.Log($"{LOG_TAG} Available Managers:");
        Debug.Log($"{LOG_TAG}   Device Image: {(deviceImageManager != null ? "✅" : "❌")} - Content: {(deviceImageManager != null && ManagerHasContent(deviceImageManager) ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG}   Device Video: {(deviceVideoManager != null ? "✅" : "❌")} - Content: {(deviceVideoManager != null && ManagerHasContent(deviceVideoManager) ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG}   Global Image: {(globalImageManager != null ? "✅" : "❌")} - Content: {(globalImageManager != null && ManagerHasContent(globalImageManager) ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG}   Global Video: {(globalVideoManager != null ? "✅" : "❌")} - Content: {(globalVideoManager != null && ManagerHasContent(globalVideoManager) ? "✅" : "❌")}");
        Debug.Log($"{LOG_TAG} === END STATUS ===");
    }

    [ContextMenu("Force Refresh")]
    public void TestForceRefresh()
    {
        ForceRefresh();
    }

    [ContextMenu("Emergency Stop")]
    public void TestEmergencyStop()
    {
        EmergencyStop();
    }

    [ContextMenu("Reconnect RenderTextures")]
    public void ReconnectRenderTextures()
    {
        Debug.Log($"{LOG_TAG} Reconnecting to manager RenderTextures...");
        AutoConnectToManagerOutputs();
    }

    // Context menu items for manual override testing
    [ContextMenu("Manual Override - Device Image")]
    public void TestManualDeviceImage()
    {
        SetManualOverride(ManualOverrideState.DeviceImage);
    }

    [ContextMenu("Manual Override - Device Video")]
    public void TestManualDeviceVideo()
    {
        SetManualOverride(ManualOverrideState.DeviceVideo);
    }

    [ContextMenu("Manual Override - Global Image")]
    public void TestManualGlobalImage()
    {
        SetManualOverride(ManualOverrideState.GlobalImage);
    }

    [ContextMenu("Manual Override - Global Video")]
    public void TestManualGlobalVideo()
    {
        SetManualOverride(ManualOverrideState.GlobalVideo);
    }

    [ContextMenu("Manual Override - None")]
    public void TestManualNone()
    {
        SetManualOverride(ManualOverrideState.None);
    }

    [ContextMenu("Disable Manual Override")]
    public void TestDisableManualOverride()
    {
        DisableManualOverride();
    }

    void OnDestroy()
    {
        Debug.Log($"{LOG_TAG} OnDestroy() called");
        EmergencyStop();
    }
}