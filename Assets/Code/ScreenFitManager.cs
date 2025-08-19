using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UIElements;

public class ScreenFitManager : MonoBehaviour
{
    private const string LOG_TAG = "[ScreenFitManager]";

    [Header("Manager References")]
    [SerializeField] private DeviceSpecificVideoManager videoManager;
    [SerializeField] private DeviceSpecificImageManager imageManager;
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private RenderTexture videoRenderTexture;
    [SerializeField] private RenderTexture imageRenderTexture;
    [SerializeField] private Camera targetCamera;

    [Header("Automatic Fitting Settings")]
    [SerializeField] private bool autoDetectDeviceType = true;
    [SerializeField] private bool autoAdjustForOrientation = true;
    [SerializeField] private bool dynamicRenderTextures = true;
    [SerializeField] private bool maintainAspectRatio = true;
    [SerializeField] private float screenChangeDelay = 0.5f;

    [Header("Fit Mode Overrides")]
    [SerializeField] private FitMode forcedVideoFitMode = FitMode.Auto;
    [SerializeField] private FitMode forcedImageFitMode = FitMode.Auto;

    [Header("Quality Settings")]
    [SerializeField] private bool useHighQualityScaling = true;
    [SerializeField] private FilterMode textureFilterMode = FilterMode.Bilinear;
    [SerializeField] private int maxRenderTextureSize = 2048;
    [SerializeField] private bool enableAntiAliasing = true;

    [Header("Debug Options")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private UIDocument debugUI;

    public enum FitMode
    {
        Auto,           // Automatically choose best fit
        FitToScreen,    // Fit entire content on screen
        FillScreen,     // Fill screen, crop if needed
        StretchToFit,   // Stretch to fill screen
        OriginalSize,   // Keep original size
        SmartFit        // Intelligent fitting based on content
    }

    public enum DeviceType
    {
        Mobile,
        Tablet,
        Desktop,
        Unknown
    }

    // Screen tracking
    private Vector2 currentScreenSize;
    private Vector2 lastScreenSize;
    private ScreenOrientation lastOrientation;
    private DeviceType currentDeviceType;
    private bool isLandscape;

    // Fit states
    private FitMode currentVideoFitMode;
    private FitMode currentImageFitMode;
    private bool isInitialized = false;

    // Debug UI elements
    private Label debugScreenInfo;
    private Label debugDeviceInfo;
    private Label debugFitInfo;
    private VisualElement debugPanel;

    // Events
    public System.Action<Vector2> OnScreenSizeChanged;
    public System.Action<ScreenOrientation> OnOrientationChanged;
    public System.Action<DeviceType> OnDeviceTypeChanged;

    void Start()
    {
        Debug.Log($"{LOG_TAG} Initializing Screen Fit Manager...");

        InitializeComponents();
        DetectCurrentScreen();
        SetupDebugUI();

        StartCoroutine(InitializeWithDelay());
    }

    void Update()
    {
        CheckForScreenChanges();

        if (showDebugInfo && Time.frameCount % 30 == 0) // Update debug info twice per second
        {
            UpdateDebugInfo();
        }
    }

    private void InitializeComponents()
    {
        // Auto-find components if not assigned
        if (videoManager == null)
            videoManager = FindFirstObjectByType<DeviceSpecificVideoManager>();

        if (imageManager == null)
            imageManager = FindFirstObjectByType<DeviceSpecificImageManager>();

        if (videoPlayer == null && videoManager != null)
            videoPlayer = videoManager.videoPlayer;

        if (targetCamera == null)
            targetCamera = Camera.main;

        // Get render textures from managers
        if (videoRenderTexture == null && videoPlayer != null)
            videoRenderTexture = videoPlayer.targetTexture;

        if (imageRenderTexture == null && imageManager != null)
            imageRenderTexture = imageManager.outputRenderTexture;

        Debug.Log($"{LOG_TAG} Components initialized - Video: {videoManager != null}, Image: {imageManager != null}, Camera: {targetCamera != null}");
    }

    private IEnumerator InitializeWithDelay()
    {
        yield return new WaitForSeconds(0.5f); // Wait for other components to initialize

        ApplyOptimalFitSettings();
        isInitialized = true;

        Debug.Log($"{LOG_TAG} ✅ Screen Fit Manager fully initialized");
    }

    private void DetectCurrentScreen()
    {
        currentScreenSize = new Vector2(Screen.width, Screen.height);
        lastScreenSize = currentScreenSize;
        lastOrientation = Screen.orientation;
        isLandscape = currentScreenSize.x > currentScreenSize.y;

        if (autoDetectDeviceType)
        {
            currentDeviceType = DetectDeviceType();
        }

        Debug.Log($"{LOG_TAG} Screen detected: {currentScreenSize.x}x{currentScreenSize.y} ({currentDeviceType}) {(isLandscape ? "Landscape" : "Portrait")}");
    }

    private DeviceType DetectDeviceType()
    {
        // Use Unity's device detection combined with screen size
        RuntimePlatform platform = Application.platform;
        float screenDiagonal = GetScreenDiagonalInches();
        int minDimension = Mathf.Min(Screen.width, Screen.height);
        int maxDimension = Mathf.Max(Screen.width, Screen.height);

        // Mobile platforms
        if (platform == RuntimePlatform.Android || platform == RuntimePlatform.IPhonePlayer)
        {
            if (screenDiagonal < 7.0f || minDimension < 600)
            {
                return DeviceType.Mobile;
            }
            else
            {
                return DeviceType.Tablet;
            }
        }

        // Desktop/Editor
        if (platform == RuntimePlatform.WindowsPlayer ||
            platform == RuntimePlatform.OSXPlayer ||
            platform == RuntimePlatform.LinuxPlayer ||
            platform == RuntimePlatform.WindowsEditor ||
            platform == RuntimePlatform.OSXEditor ||
            platform == RuntimePlatform.LinuxEditor)
        {
            if (minDimension < 800)
            {
                return DeviceType.Mobile; // Small window
            }
            else if (minDimension < 1200)
            {
                return DeviceType.Tablet; // Medium window
            }
            else
            {
                return DeviceType.Desktop;
            }
        }

        return DeviceType.Unknown;
    }

    private float GetScreenDiagonalInches()
    {
        float dpi = Screen.dpi > 0 ? Screen.dpi : 160f; // Fallback to standard Android DPI
        float widthInches = Screen.width / dpi;
        float heightInches = Screen.height / dpi;
        return Mathf.Sqrt(widthInches * widthInches + heightInches * heightInches);
    }

    private void CheckForScreenChanges()
    {
        currentScreenSize = new Vector2(Screen.width, Screen.height);
        bool orientationChanged = autoAdjustForOrientation && Screen.orientation != lastOrientation;
        bool sizeChanged = currentScreenSize != lastScreenSize;

        if (sizeChanged || orientationChanged)
        {
            Debug.Log($"{LOG_TAG} Screen change detected: {lastScreenSize} -> {currentScreenSize}, Orientation: {lastOrientation} -> {Screen.orientation}");

            StartCoroutine(HandleScreenChangeWithDelay());

            lastScreenSize = currentScreenSize;
            lastOrientation = Screen.orientation;
        }
    }

    private IEnumerator HandleScreenChangeWithDelay()
    {
        yield return new WaitForSeconds(screenChangeDelay);

        DetectCurrentScreen();

        if (isInitialized)
        {
            ApplyOptimalFitSettings();
        }

        // Trigger events
        OnScreenSizeChanged?.Invoke(currentScreenSize);
        OnOrientationChanged?.Invoke(Screen.orientation);
        OnDeviceTypeChanged?.Invoke(currentDeviceType);
    }

    public void ApplyOptimalFitSettings()
    {
        Debug.Log($"{LOG_TAG} Applying optimal fit settings for {currentDeviceType} {currentScreenSize.x}x{currentScreenSize.y}");

        // Determine optimal fit modes
        currentVideoFitMode = DetermineOptimalVideoFitMode();
        currentImageFitMode = DetermineOptimalImageFitMode();

        // Apply settings
        ApplyVideoFitMode(currentVideoFitMode);
        ApplyImageFitMode(currentImageFitMode);

        if (dynamicRenderTextures)
        {
            UpdateRenderTextureSizes();
        }

        ConfigureCameraSettings();

        Debug.Log($"{LOG_TAG} ✅ Applied fit settings - Video: {currentVideoFitMode}, Image: {currentImageFitMode}");
    }

    private FitMode DetermineOptimalVideoFitMode()
    {
        if (forcedVideoFitMode != FitMode.Auto)
            return forcedVideoFitMode;

        switch (currentDeviceType)
        {
            case DeviceType.Mobile:
                return isLandscape ? FitMode.FillScreen : FitMode.FitToScreen;

            case DeviceType.Tablet:
                return FitMode.SmartFit;

            case DeviceType.Desktop:
                return FitMode.FitToScreen;

            default:
                return FitMode.SmartFit;
        }
    }

    private FitMode DetermineOptimalImageFitMode()
    {
        if (forcedImageFitMode != FitMode.Auto)
            return forcedImageFitMode;

        switch (currentDeviceType)
        {
            case DeviceType.Mobile:
                return isLandscape ? FitMode.FillScreen : FitMode.FitToScreen;

            case DeviceType.Tablet:
                return FitMode.FitToScreen;

            case DeviceType.Desktop:
                return FitMode.FitToScreen;

            default:
                return FitMode.FitToScreen;
        }
    }

    private void ApplyVideoFitMode(FitMode fitMode)
    {
        if (videoPlayer == null) return;

        switch (videoPlayer.renderMode)
        {
            case VideoRenderMode.RenderTexture:
                ConfigureVideoRenderTexture(fitMode);
                break;

            case VideoRenderMode.CameraFarPlane:
            case VideoRenderMode.CameraNearPlane:
                ConfigureVideoCamera(fitMode);
                break;

            case VideoRenderMode.MaterialOverride:
                ConfigureVideoMaterialOverride(fitMode);
                break;

            case VideoRenderMode.APIOnly:
                // No visual rendering, just texture access
                break;
        }

        Debug.Log($"{LOG_TAG} Video fit mode applied: {fitMode} (Render Mode: {videoPlayer.renderMode})");

        // Additional debug info for render mode specific settings
        if (videoPlayer.renderMode == VideoRenderMode.RenderTexture && videoRenderTexture != null)
        {
            Debug.Log($"{LOG_TAG} Video RenderTexture: {videoRenderTexture.width}x{videoRenderTexture.height}");
        }
        else if (videoPlayer.renderMode == VideoRenderMode.CameraFarPlane ||
                 videoPlayer.renderMode == VideoRenderMode.CameraNearPlane)
        {
            Debug.Log($"{LOG_TAG} Video Aspect Ratio: {videoPlayer.aspectRatio}");
        }
    }

    private void ConfigureVideoRenderTexture(FitMode fitMode)
    {
        if (videoRenderTexture == null) return;

        // The actual texture fitting will be handled by UpdateRenderTextureSizes()
        // Here we just ensure the video player is configured correctly
        videoPlayer.targetTexture = videoRenderTexture;
    }

    private void ConfigureVideoCamera(FitMode fitMode)
    {
        VideoAspectRatio aspectRatio = fitMode switch
        {
            FitMode.FitToScreen => VideoAspectRatio.FitVertically,
            FitMode.FillScreen => VideoAspectRatio.FitHorizontally,
            FitMode.StretchToFit => VideoAspectRatio.Stretch,
            FitMode.OriginalSize => VideoAspectRatio.NoScaling,
            FitMode.SmartFit => DetermineSmartVideoAspectRatio(),
            _ => VideoAspectRatio.FitVertically
        };

        videoPlayer.aspectRatio = aspectRatio;
    }

    private void ConfigureVideoMaterialOverride(FitMode fitMode)
    {
        // For MaterialOverride mode, the fitting is handled by the material/shader
        // We can set aspect ratio if needed
        switch (fitMode)
        {
            case FitMode.FitToScreen:
                videoPlayer.aspectRatio = VideoAspectRatio.FitVertically;
                break;

            case FitMode.FillScreen:
                videoPlayer.aspectRatio = VideoAspectRatio.FitHorizontally;
                break;

            case FitMode.StretchToFit:
                videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
                break;

            case FitMode.OriginalSize:
                videoPlayer.aspectRatio = VideoAspectRatio.NoScaling;
                break;

            case FitMode.SmartFit:
                videoPlayer.aspectRatio = DetermineSmartVideoAspectRatio();
                break;
        }
    }

    private VideoAspectRatio DetermineSmartVideoAspectRatio()
    {
        return isLandscape ? VideoAspectRatio.FitHorizontally : VideoAspectRatio.FitVertically;
    }

    private void ApplyCameraLetterboxing()
    {
        if (targetCamera == null) return;

        Vector2 videoSize = GetVideoNativeSize();
        if (videoSize == Vector2.zero)
        {
            targetCamera.rect = new Rect(0, 0, 1, 1);
            return;
        }

        float videoAspect = videoSize.x / videoSize.y;
        float screenAspect = currentScreenSize.x / currentScreenSize.y;

        if (screenAspect >= videoAspect)
        {
            // Screen is wider - add pillarbox
            float width = videoAspect / screenAspect;
            float offsetX = (1f - width) / 2f;
            targetCamera.rect = new Rect(offsetX, 0, width, 1);
        }
        else
        {
            // Screen is taller - add letterbox
            float height = screenAspect / videoAspect;
            float offsetY = (1f - height) / 2f;
            targetCamera.rect = new Rect(0, offsetY, 1, height);
        }
    }

    private Vector2 GetVideoNativeSize()
    {
        if (videoPlayer != null && videoPlayer.texture != null)
        {
            return new Vector2(videoPlayer.texture.width, videoPlayer.texture.height);
        }
        return Vector2.zero;
    }

    private void ApplyImageFitMode(FitMode fitMode)
    {
        if (imageManager == null) return;

        // Store fit mode for when images are loaded
        var method = imageManager.GetType().GetMethod("SetImageFitMode");
        if (method != null)
        {
            var imageFitMode = fitMode switch
            {
                FitMode.FitToScreen => 0, // Assuming enum values
                FitMode.FillScreen => 1,
                FitMode.StretchToFit => 2,
                FitMode.OriginalSize => 3,
                FitMode.SmartFit => 4,
                _ => 0
            };

            try
            {
                method.Invoke(imageManager, new object[] { imageFitMode });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{LOG_TAG} Could not set image fit mode: {e.Message}");
            }
        }

        Debug.Log($"{LOG_TAG} Image fit mode applied: {fitMode}");
    }

    private void UpdateRenderTextureSizes()
    {
        if (videoRenderTexture != null)
        {
            Vector2 optimalVideoSize = CalculateOptimalRenderTextureSize(currentVideoFitMode, GetVideoNativeSize());
            ResizeRenderTexture(videoRenderTexture, optimalVideoSize, "Video");
        }

        if (imageRenderTexture != null)
        {
            Vector2 optimalImageSize = CalculateOptimalRenderTextureSize(currentImageFitMode, Vector2.zero);
            ResizeRenderTexture(imageRenderTexture, optimalImageSize, "Image");
        }
    }

    private Vector2 CalculateOptimalRenderTextureSize(FitMode fitMode, Vector2 contentSize)
    {
        Vector2 targetSize = currentScreenSize;

        // Limit to max size for performance
        if (targetSize.x > maxRenderTextureSize || targetSize.y > maxRenderTextureSize)
        {
            float scale = maxRenderTextureSize / Mathf.Max(targetSize.x, targetSize.y);
            targetSize *= scale;
        }

        switch (fitMode)
        {
            case FitMode.OriginalSize:
                if (contentSize != Vector2.zero)
                    return contentSize;
                break;

            case FitMode.FitToScreen:
                if (contentSize != Vector2.zero)
                    return CalculateFitToScreenSize(contentSize, targetSize);
                break;
        }

        return targetSize;
    }

    private Vector2 CalculateFitToScreenSize(Vector2 contentSize, Vector2 screenSize)
    {
        float contentAspect = contentSize.x / contentSize.y;
        float screenAspect = screenSize.x / screenSize.y;

        if (contentAspect > screenAspect)
        {
            // Content is wider - fit to width
            float height = screenSize.x / contentAspect;
            return new Vector2(screenSize.x, height);
        }
        else
        {
            // Content is taller - fit to height
            float width = screenSize.y * contentAspect;
            return new Vector2(width, screenSize.y);
        }
    }

    private void ResizeRenderTexture(RenderTexture renderTexture, Vector2 targetSize, string name)
    {
        int width = Mathf.RoundToInt(targetSize.x);
        int height = Mathf.RoundToInt(targetSize.y);

        // Ensure minimum size
        width = Mathf.Max(width, 64);
        height = Mathf.Max(height, 64);

        if (renderTexture.width != width || renderTexture.height != height)
        {
            renderTexture.Release();
            renderTexture.width = width;
            renderTexture.height = height;

            // Apply quality settings
            renderTexture.filterMode = textureFilterMode;
            if (enableAntiAliasing && useHighQualityScaling)
            {
                renderTexture.antiAliasing = 4;
            }

            renderTexture.Create();

            Debug.Log($"{LOG_TAG} {name} RenderTexture resized to: {width}x{height}");
        }
    }

    private void ConfigureCameraSettings()
    {
        if (targetCamera == null) return;

        // Reset camera rect unless we're doing letterboxing with camera render modes
        if (currentVideoFitMode != FitMode.FitToScreen ||
            (videoPlayer?.renderMode != VideoRenderMode.CameraFarPlane &&
             videoPlayer?.renderMode != VideoRenderMode.CameraNearPlane))
        {
            targetCamera.rect = new Rect(0, 0, 1, 1);
        }
    }

    #region Debug UI

    private void SetupDebugUI()
    {
        if (!showDebugInfo || debugUI == null) return;

        VisualElement root = debugUI.rootVisualElement;

        debugPanel = new VisualElement();
        debugPanel.name = "debug-panel";
        debugPanel.style.position = Position.Absolute;
        debugPanel.style.top = 10;
        debugPanel.style.left = 10;
        debugPanel.style.backgroundColor = new Color(0, 0, 0, 0.8f);
        debugPanel.style.borderTopLeftRadius = 10;
        debugPanel.style.borderTopRightRadius = 10;
        debugPanel.style.borderBottomLeftRadius = 10;
        debugPanel.style.borderBottomRightRadius = 10;
        debugPanel.style.paddingTop = 10;
        debugPanel.style.paddingBottom = 10;
        debugPanel.style.paddingLeft = 15;
        debugPanel.style.paddingRight = 15;
        debugPanel.style.minWidth = 300;

        var title = new Label("Screen Fit Manager Debug");
        title.style.color = Color.white;
        title.style.fontSize = 16;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 10;
        debugPanel.Add(title);

        debugScreenInfo = new Label();
        debugScreenInfo.style.color = Color.yellow;
        debugScreenInfo.style.fontSize = 12;
        debugScreenInfo.style.marginBottom = 5;
        debugPanel.Add(debugScreenInfo);

        debugDeviceInfo = new Label();
        debugDeviceInfo.style.color = Color.cyan;
        debugDeviceInfo.style.fontSize = 12;
        debugDeviceInfo.style.marginBottom = 5;
        debugPanel.Add(debugDeviceInfo);

        debugFitInfo = new Label();
        debugFitInfo.style.color = Color.green;
        debugFitInfo.style.fontSize = 12;
        debugPanel.Add(debugFitInfo);

        root.Add(debugPanel);

        UpdateDebugInfo();
    }

    private void UpdateDebugInfo()
    {
        if (!showDebugInfo) return;

        if (debugScreenInfo != null)
        {
            float aspect = currentScreenSize.x / currentScreenSize.y;
            string orientation = isLandscape ? "Landscape" : "Portrait";
            debugScreenInfo.text = $"Screen: {currentScreenSize.x}x{currentScreenSize.y} ({aspect:F2}) {orientation}";
        }

        if (debugDeviceInfo != null)
        {
            float diagonal = GetScreenDiagonalInches();
            debugDeviceInfo.text = $"Device: {currentDeviceType} ({diagonal:F1}\" diagonal)";
        }

        if (debugFitInfo != null)
        {
            debugFitInfo.text = $"Fit Modes - Video: {currentVideoFitMode}, Image: {currentImageFitMode}";
        }
    }

    #endregion

    #region Public Methods

    public void SetFitMode(FitMode videoMode, FitMode imageMode)
    {
        forcedVideoFitMode = videoMode;
        forcedImageFitMode = imageMode;
        ApplyOptimalFitSettings();
    }

    public void SetAutoDetection(bool enableAutoDetect, bool enableAutoOrientation)
    {
        autoDetectDeviceType = enableAutoDetect;
        autoAdjustForOrientation = enableAutoOrientation;

        if (enableAutoDetect)
        {
            DetectCurrentScreen();
            ApplyOptimalFitSettings();
        }
    }

    public void ForceRefresh()
    {
        DetectCurrentScreen();
        ApplyOptimalFitSettings();
    }

    public void ApplyPreset(string presetName)
    {
        switch (presetName.ToLower())
        {
            case "mobile":
                ApplyMobilePreset();
                break;
            case "tablet":
                ApplyTabletPreset();
                break;
            case "desktop":
                ApplyDesktopPreset();
                break;
            default:
                Debug.LogWarning($"{LOG_TAG} Unknown preset: {presetName}");
                break;
        }
    }

    private void ApplyMobilePreset()
    {
        forcedVideoFitMode = isLandscape ? FitMode.FillScreen : FitMode.FitToScreen;
        forcedImageFitMode = isLandscape ? FitMode.FillScreen : FitMode.FitToScreen;
        autoDetectDeviceType = false;
        autoAdjustForOrientation = true;
        dynamicRenderTextures = true;
        ApplyOptimalFitSettings();
        Debug.Log($"{LOG_TAG} Mobile preset applied");
    }

    private void ApplyTabletPreset()
    {
        forcedVideoFitMode = FitMode.SmartFit;
        forcedImageFitMode = FitMode.FitToScreen;
        autoDetectDeviceType = false;
        autoAdjustForOrientation = true;
        dynamicRenderTextures = true;
        ApplyOptimalFitSettings();
        Debug.Log($"{LOG_TAG} Tablet preset applied");
    }

    private void ApplyDesktopPreset()
    {
        forcedVideoFitMode = FitMode.FitToScreen;
        forcedImageFitMode = FitMode.FitToScreen;
        autoDetectDeviceType = false;
        autoAdjustForOrientation = false;
        dynamicRenderTextures = false;
        ApplyOptimalFitSettings();
        Debug.Log($"{LOG_TAG} Desktop preset applied");
    }

    public void ToggleDebugUI()
    {
        showDebugInfo = !showDebugInfo;

        if (debugPanel != null)
        {
            debugPanel.style.display = showDebugInfo ? DisplayStyle.Flex : DisplayStyle.None;
        }
        else if (showDebugInfo)
        {
            SetupDebugUI();
        }
    }

    // Getters
    public Vector2 GetCurrentScreenSize() => currentScreenSize;
    public DeviceType GetCurrentDeviceType() => currentDeviceType;
    public bool IsLandscape() => isLandscape;
    public FitMode GetCurrentVideoFitMode() => currentVideoFitMode;
    public FitMode GetCurrentImageFitMode() => currentImageFitMode;

    #endregion

    #region Context Menu Debug Methods

    [ContextMenu("Force Mobile Preset")]
    public void DebugApplyMobilePreset() => ApplyMobilePreset();

    [ContextMenu("Force Tablet Preset")]
    public void DebugApplyTabletPreset() => ApplyTabletPreset();

    [ContextMenu("Force Desktop Preset")]
    public void DebugApplyDesktopPreset() => ApplyDesktopPreset();

    [ContextMenu("Debug Current State")]
    public void DebugCurrentState()
    {
        Debug.Log($"{LOG_TAG} === SCREEN FIT MANAGER DEBUG ===");
        Debug.Log($"{LOG_TAG} Screen: {currentScreenSize.x}x{currentScreenSize.y} ({GetScreenDiagonalInches():F1}\")");
        Debug.Log($"{LOG_TAG} Device Type: {currentDeviceType}");
        Debug.Log($"{LOG_TAG} Orientation: {(isLandscape ? "Landscape" : "Portrait")}");
        Debug.Log($"{LOG_TAG} Video Fit Mode: {currentVideoFitMode}");
        Debug.Log($"{LOG_TAG} Image Fit Mode: {currentImageFitMode}");
        Debug.Log($"{LOG_TAG} Auto Detect: {autoDetectDeviceType}");
        Debug.Log($"{LOG_TAG} Auto Orientation: {autoAdjustForOrientation}");
        Debug.Log($"{LOG_TAG} Dynamic RT: {dynamicRenderTextures}");
        Debug.Log($"{LOG_TAG} Video RT: {(videoRenderTexture != null ? $"{videoRenderTexture.width}x{videoRenderTexture.height}" : "None")}");
        Debug.Log($"{LOG_TAG} Image RT: {(imageRenderTexture != null ? $"{imageRenderTexture.width}x{imageRenderTexture.height}" : "None")}");
        Debug.Log($"{LOG_TAG} === END DEBUG ===");
    }

    #endregion
}