using UnityEngine;
using UnityAtoms.BaseAtoms;

/// <summary>
/// Controls render texture GameObjects based on Atom bool variables.
/// Automatically fits render textures to canvas size with no empty space.
/// Ensures mutual exclusion between image and video render textures.
/// Maintains stable state - once active, stays active until replaced by other media.
/// </summary>
public class RenderTextureController : MonoBehaviour
{
    [Header("Render Texture GameObjects")]
    [SerializeField] private GameObject videoRenderTextureObject;
    [SerializeField] private GameObject imageRenderTextureObject;

    [Header("Canvas Reference")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform canvasRectTransform;

    [Header("UI Fitting Options")]
    [SerializeField] private bool useAnchorStretching = false;
    [SerializeField] private bool forceOverrideLayout = true;

    [Header("State Management")]
    [SerializeField] private bool videoPriority = true; // If true, video takes priority over image when both try to activate
    [SerializeField] private bool enableDebugLogs = true;
    [SerializeField] private bool maintainLastActiveState = true; // Keep last active state when API reports no media

    [Header("API Status Variables (READ ONLY)")]
    [SerializeField] private BoolVariable apiHasImageVariable; // From API - don't modify
    [SerializeField] private BoolVariable apiHasVideoVariable; // From API - don't modify
    [SerializeField] private BoolVariable apiHasAnyMediaVariable; // From API - don't modify

    [Header("Display Control Variables (CONTROLLED BY THIS SCRIPT)")]
    [SerializeField] private BoolVariable displayImageActive; // Controls actual image display
    [SerializeField] private BoolVariable displayVideoActive; // Controls actual video display
    [SerializeField] private BoolVariable displayAnyActive;   // Controls any display

    // Internal state tracking
    private bool currentImageDisplayState = false;
    private bool currentVideoDisplayState = false;
    private bool hasInitialized = false;

    // Track what was last requested to be active (for maintaining state)
    private bool lastRequestedImageState = false;
    private bool lastRequestedVideoState = false;

    // Debouncing to prevent rapid state changes
    private float lastStateChangeTime = 0f;
    private const float STATE_CHANGE_DEBOUNCE = 0.1f;

    private void Start()
    {
        InitializeController();
    }

    private void InitializeController()
    {
        DebugLog("Initializing RenderTextureController...");

        // Subscribe to API status changes (READ ONLY - we only listen, never modify these)
        if (apiHasImageVariable != null)
        {
            apiHasImageVariable.Changed.Register(OnApiImageStatusChanged);
            DebugLog($"Subscribed to API Image Variable: {apiHasImageVariable.name}");
        }

        if (apiHasVideoVariable != null)
        {
            apiHasVideoVariable.Changed.Register(OnApiVideoStatusChanged);
            DebugLog($"Subscribed to API Video Variable: {apiHasVideoVariable.name}");
        }

        if (apiHasAnyMediaVariable != null)
        {
            apiHasAnyMediaVariable.Changed.Register(OnApiAnyMediaStatusChanged);
            DebugLog($"Subscribed to API Any Media Variable: {apiHasAnyMediaVariable.name}");
        }

        // Initialize display control variables
        InitializeDisplayVariables();

        // Set initial state from API (exclusive)
        UpdateDisplayState();
        hasInitialized = true;

        DebugLog("RenderTextureController initialization complete");
    }

    private void InitializeDisplayVariables()
    {
        // Initialize display control variables - start with image active by default
        if (displayImageActive != null)
        {
            displayImageActive.Value = true;
            currentImageDisplayState = true;
            lastRequestedImageState = true;
            DebugLog($"Initialized Display Image Variable: {displayImageActive.name} (DEFAULT ACTIVE)");
        }

        if (displayVideoActive != null)
        {
            displayVideoActive.Value = false;
            DebugLog($"Initialized Display Video Variable: {displayVideoActive.name}");
        }

        if (displayAnyActive != null)
        {
            displayAnyActive.Value = true; // Always true since we enforce at least one active
            DebugLog($"Initialized Display Any Variable: {displayAnyActive.name} (ALWAYS TRUE)");
        }

        // Make sure the default image GameObject is active
        SetGameObjectActive(imageRenderTextureObject, true);
        SetGameObjectActive(videoRenderTextureObject, false);
    }

    private void OnDestroy()
    {
        // Unsubscribe from API status changes
        if (apiHasImageVariable != null)
            apiHasImageVariable.Changed.Unregister(OnApiImageStatusChanged);

        if (apiHasVideoVariable != null)
            apiHasVideoVariable.Changed.Unregister(OnApiVideoStatusChanged);

        if (apiHasAnyMediaVariable != null)
            apiHasAnyMediaVariable.Changed.Unregister(OnApiAnyMediaStatusChanged);
    }

    // ===== Mutual-Exclusivity Core =====
    private void ApplyExclusiveDisplayFromApi()
    {
        bool apiImage = GetApiImageStatus();
        bool apiVideo = GetApiVideoStatus();

        // Only Video
        if (apiVideo && !apiImage)
        {
            lastRequestedVideoState = true;
            lastRequestedImageState = false;

            if (!currentVideoDisplayState) SetVideoDisplayState(true);
            if (currentImageDisplayState) SetImageDisplayState(false);
            return;
        }

        // Only Image
        if (apiImage && !apiVideo)
        {
            lastRequestedImageState = true;
            lastRequestedVideoState = false;

            if (!currentImageDisplayState) SetImageDisplayState(true);
            if (currentVideoDisplayState) SetVideoDisplayState(false);
            return;
        }

        // Both present → respect priority
        if (apiImage && apiVideo)
        {
            if (videoPriority)
            {
                lastRequestedVideoState = true;
                lastRequestedImageState = false;

                if (!currentVideoDisplayState) SetVideoDisplayState(true);
                if (currentImageDisplayState) SetImageDisplayState(false);
            }
            else
            {
                lastRequestedImageState = true;
                lastRequestedVideoState = false;

                if (!currentImageDisplayState) SetImageDisplayState(true);
                if (currentVideoDisplayState) SetVideoDisplayState(false);
            }
            return;
        }

        // None present
        if (!maintainLastActiveState)
        {
            RequestDeactivateAll(); // keeps exactly one active (defaults to image)
        }
        else
        {
            // Maintain whatever is currently active; still ensure only one is active
            if (currentImageDisplayState && currentVideoDisplayState)
            {
                if (videoPriority) SetImageDisplayState(false);
                else SetVideoDisplayState(false);
            }
            else if (!currentImageDisplayState && !currentVideoDisplayState)
            {
                // pick last requested or default to image
                if (lastRequestedVideoState) SetVideoDisplayState(true);
                else SetImageDisplayState(true);
            }
        }

        UpdateAnyActiveState();
    }

    // API Status Change Handlers → Always delegate to exclusivity rule
    private void OnApiImageStatusChanged(bool _)
    {
        DebugLog($"API Image Status Changed: {GetApiImageStatus()}");
        ApplyExclusiveDisplayFromApi();
    }

    private void OnApiVideoStatusChanged(bool _)
    {
        DebugLog($"API Video Status Changed: {GetApiVideoStatus()}");
        ApplyExclusiveDisplayFromApi();
    }

    private void OnApiAnyMediaStatusChanged(bool _)
    {
        DebugLog($"API Any Media Status Changed: {GetApiAnyStatus()}");
        ApplyExclusiveDisplayFromApi();
    }

    // Core State Management Methods
    private void SetImageDisplayState(bool active)
    {
        if (currentImageDisplayState == active)
            return; // No change needed

        // Debounce
        if (Time.time - lastStateChangeTime < STATE_CHANGE_DEBOUNCE)
        {
            DebugLog("SetImageDisplayState debounced");
            return;
        }

        currentImageDisplayState = active;
        lastStateChangeTime = Time.time;

        // Update the display control variable
        if (displayImageActive != null)
            displayImageActive.Value = active;

        // Update GameObjects
        SetGameObjectActive(imageRenderTextureObject, active);

        if (active)
        {
            FitRenderTextureToCanvas(imageRenderTextureObject);
        }

        UpdateAnyActiveState();
        DebugLog($"Image display state set to: {active}");
    }

    private void SetVideoDisplayState(bool active)
    {
        if (currentVideoDisplayState == active)
            return; // No change needed

        // Debounce
        if (Time.time - lastStateChangeTime < STATE_CHANGE_DEBOUNCE)
        {
            DebugLog("SetVideoDisplayState debounced");
            return;
        }

        currentVideoDisplayState = active;
        lastStateChangeTime = Time.time;

        // Update the display control variable
        if (displayVideoActive != null)
            displayVideoActive.Value = active;

        // Update GameObjects
        SetGameObjectActive(videoRenderTextureObject, active);

        if (active)
        {
            FitRenderTextureToCanvas(videoRenderTextureObject);
        }

        UpdateAnyActiveState();
        DebugLog($"Video display state set to: {active}");
    }

    private void UpdateAnyActiveState()
    {
        bool anyActive = currentImageDisplayState || currentVideoDisplayState;

        // ENFORCE: Always keep at least one active - if both are trying to be false, keep the last one that was true
        if (!anyActive)
        {
            if (lastRequestedImageState && !lastRequestedVideoState)
            {
                DebugLog("Enforcing: Keeping image active as it was the last requested");
                currentImageDisplayState = true;
                if (displayImageActive != null) displayImageActive.Value = true;
                SetGameObjectActive(imageRenderTextureObject, true);
                anyActive = true;
            }
            else if (lastRequestedVideoState && !lastRequestedImageState)
            {
                DebugLog("Enforcing: Keeping video active as it was the last requested");
                currentVideoDisplayState = true;
                if (displayVideoActive != null) displayVideoActive.Value = true;
                SetGameObjectActive(videoRenderTextureObject, true);
                anyActive = true;
            }
            else
            {
                // Default to image if no clear preference
                DebugLog("Enforcing: Defaulting to image active (no clear last state)");
                currentImageDisplayState = true;
                lastRequestedImageState = true;
                if (displayImageActive != null) displayImageActive.Value = true;
                SetGameObjectActive(imageRenderTextureObject, true);
                anyActive = true;
            }
        }

        if (displayAnyActive != null)
            displayAnyActive.Value = anyActive;

        DebugLog($"Any active state updated to: {anyActive} (Image: {currentImageDisplayState}, Video: {currentVideoDisplayState})");
    }

    private void UpdateDisplayState()
    {
        // Reconcile API status with display state using exclusive rule
        ApplyExclusiveDisplayFromApi();
    }

    // Request Methods (kept for external calls; exclusivity is enforced in ApplyExclusiveDisplayFromApi)
    private void RequestImageActivation()
    {
        if (Time.time - lastStateChangeTime < STATE_CHANGE_DEBOUNCE)
        {
            DebugLog("Image activation request debounced");
            return;
        }

        DebugLog("Requesting image activation");
        lastRequestedImageState = true;

        if (currentVideoDisplayState)
        {
            if (videoPriority)
            {
                DebugLog("Video has priority - ignoring image activation request");
                return;
            }
            else
            {
                DebugLog("Image has priority - deactivating video");
                SetVideoDisplayState(false);
            }
        }

        SetImageDisplayState(true);
    }

    private void RequestVideoActivation()
    {
        if (Time.time - lastStateChangeTime < STATE_CHANGE_DEBOUNCE)
        {
            DebugLog("Video activation request debounced");
            return;
        }

        DebugLog("Requesting video activation");
        lastRequestedVideoState = true;

        if (currentImageDisplayState)
        {
            if (videoPriority)
            {
                DebugLog("Video has priority - deactivating image");
                SetImageDisplayState(false);
            }
            else
            {
                DebugLog("Image has priority - ignoring video activation request");
                return;
            }
        }

        SetVideoDisplayState(true);
    }

    private void RequestImageDeactivation()
    {
        DebugLog("Requesting image deactivation");
        lastRequestedImageState = false;

        // Only deactivate if video will be active, otherwise keep image active
        if (currentVideoDisplayState || GetApiVideoStatus())
        {
            SetImageDisplayState(false);
        }
        else
        {
            DebugLog("Cannot deactivate image - no video active. Keeping image active.");
        }
    }

    private void RequestVideoDeactivation()
    {
        DebugLog("Requesting video deactivation");
        lastRequestedVideoState = false;

        // Only deactivate if image will be active, otherwise keep video active
        if (currentImageDisplayState || GetApiImageStatus())
        {
            SetVideoDisplayState(false);
        }
        else
        {
            DebugLog("Cannot deactivate video - no image active. Keeping video active.");
        }
    }

    private void RequestDeactivateAll()
    {
        DebugLog("Requesting deactivation of all displays - but enforcing at least one stays active");
        lastRequestedImageState = false;
        lastRequestedVideoState = false;

        // Don't actually deactivate both - the UpdateAnyActiveState will handle keeping one active
        if (videoPriority && currentVideoDisplayState)
        {
            // Keep video, deactivate image
            SetImageDisplayState(false);
        }
        else if (currentImageDisplayState)
        {
            // Keep image, deactivate video
            SetVideoDisplayState(false);
        }
        else
        {
            // Neither is currently active, activate default (image)
            SetImageDisplayState(true);
            lastRequestedImageState = true;
        }
    }

    // Helper Methods
    private bool GetApiImageStatus()
    {
        return apiHasImageVariable?.Value ?? false;
    }

    private bool GetApiVideoStatus()
    {
        return apiHasVideoVariable?.Value ?? false;
    }

    private bool GetApiAnyStatus()
    {
        return apiHasAnyMediaVariable?.Value ?? false;
    }

    private void SetGameObjectActive(GameObject obj, bool active)
    {
        if (obj != null && obj.activeSelf != active)
        {
            obj.SetActive(active);
            DebugLog($"GameObject {obj.name} set to: {active}");
        }
    }

    private void DebugLog(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[RenderTextureController] {message}");
        }
    }

    // Canvas Fitting Methods (keeping your existing logic)
    private void FitRenderTextureToCanvas(GameObject renderTextureObject)
    {
        if (renderTextureObject == null || targetCanvas == null)
            return;

        Vector2 canvasSize = GetCanvasSize();
        if (canvasSize == Vector2.zero)
            return;

        RectTransform rectTransform = renderTextureObject.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            FitUIRenderTexture(rectTransform, canvasSize);
        }
        else
        {
            FitWorldSpaceRenderTexture(renderTextureObject.transform, canvasSize);
        }
    }

    private void FitUIRenderTexture(RectTransform rectTransform, Vector2 canvasSize)
    {
        if (useAnchorStretching)
        {
            FitUIRenderTextureWithAnchors(rectTransform, canvasSize);
            return;
        }

        // Check for layout components that might interfere
        UnityEngine.UI.LayoutElement layoutElement = rectTransform.GetComponent<UnityEngine.UI.LayoutElement>();
        UnityEngine.UI.ContentSizeFitter sizeFitter = rectTransform.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        UnityEngine.UI.AspectRatioFitter aspectFitter = rectTransform.GetComponent<UnityEngine.UI.AspectRatioFitter>();

        // Disable components that might interfere with manual sizing
        if (forceOverrideLayout)
        {
            if (sizeFitter != null)
                sizeFitter.enabled = false;
            if (aspectFitter != null)
                aspectFitter.enabled = false;
        }

        // Set anchors to center-center so sizeDelta works properly
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        // Get the texture size for aspect ratio calculation
        Vector2 textureSize = Vector2.zero;
        UnityEngine.UI.RawImage rawImage = rectTransform.GetComponent<UnityEngine.UI.RawImage>();

        if (rawImage != null && rawImage.texture != null)
        {
            textureSize = new Vector2(rawImage.texture.width, rawImage.texture.height);
        }
        else
        {
            textureSize = rectTransform.sizeDelta != Vector2.zero ? rectTransform.sizeDelta : canvasSize;
        }

        // Calculate scale to FILL canvas completely (crop if necessary - no empty space)
        float canvasAspect = canvasSize.x / canvasSize.y;
        float textureAspect = textureSize.x / textureSize.y;

        Vector2 newSize;
        if (textureAspect > canvasAspect)
        {
            newSize.y = canvasSize.y;
            newSize.x = canvasSize.y * textureAspect;
        }
        else
        {
            newSize.x = canvasSize.x;
            newSize.y = canvasSize.x / textureAspect;
        }

        // Ensure we never have a size smaller than canvas
        if (newSize.x < canvasSize.x)
        {
            float scale = canvasSize.x / newSize.x;
            newSize.x *= scale;
            newSize.y *= scale;
        }
        if (newSize.y < canvasSize.y)
        {
            float scale = canvasSize.y / newSize.y;
            newSize.x *= scale;
            newSize.y *= scale;
        }

        rectTransform.sizeDelta = newSize;
        rectTransform.anchoredPosition = Vector2.zero;

        if (layoutElement != null && forceOverrideLayout)
        {
            layoutElement.preferredWidth = newSize.x;
            layoutElement.preferredHeight = newSize.y;
        }

        UnityEngine.UI.LayoutGroup parentLayout = rectTransform.GetComponentInParent<UnityEngine.UI.LayoutGroup>();
        if (parentLayout != null)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parentLayout.transform as RectTransform);
        }

        DebugLog($"Fitted UI RenderTexture - Canvas: {canvasSize}, New Size: {newSize}");
    }

    private void FitUIRenderTextureWithAnchors(RectTransform rectTransform, Vector2 canvasSize)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        UnityEngine.UI.RawImage rawImage = rectTransform.GetComponent<UnityEngine.UI.RawImage>();

        if (rawImage != null && rawImage.texture != null)
        {
            Vector2 textureSize = new Vector2(rawImage.texture.width, rawImage.texture.height);
            float canvasAspect = canvasSize.x / canvasSize.y;
            float textureAspect = textureSize.x / textureSize.y;

            Rect uvRect = new Rect(0, 0, 1, 1);

            if (textureAspect > canvasAspect)
            {
                float scale = canvasAspect / textureAspect;
                float offset = (1f - scale) * 0.5f;
                uvRect = new Rect(offset, 0, scale, 1);
            }
            else if (textureAspect < canvasAspect)
            {
                float scale = textureAspect / canvasAspect;
                float offset = (1f - scale) * 0.5f;
                uvRect = new Rect(0, offset, 1, scale);
            }

            rawImage.uvRect = uvRect;
            DebugLog($"Fitted with anchors - UV Rect: {uvRect}");
        }
    }

    private void FitWorldSpaceRenderTexture(Transform transform, Vector2 canvasSize)
    {
        Vector3 originalScale = transform.localScale;
        float scaleFactor = Mathf.Max(canvasSize.x / 1920f, canvasSize.y / 1080f);
        Vector3 newScale = originalScale * scaleFactor;
        transform.localScale = newScale;
        DebugLog($"Fitted World Space - Scale Factor: {scaleFactor}");
    }

    private Vector2 GetCanvasSize()
    {
        if (targetCanvas == null)
            return Vector2.zero;

        if (canvasRectTransform != null)
            return canvasRectTransform.sizeDelta;

        RectTransform canvasRect = targetCanvas.GetComponent<RectTransform>();
        if (canvasRect != null)
            return canvasRect.sizeDelta;

        return new Vector2(Screen.width, Screen.height);
    }

    // Public API Methods
    public void ForceActivateImage()
    {
        DebugLog("Force activating image");
        // enforce exclusivity
        SetVideoDisplayState(false);
        SetImageDisplayState(true);
        lastRequestedImageState = true;
        lastRequestedVideoState = false;
    }

    public void ForceActivateVideo()
    {
        DebugLog("Force activating video");
        // enforce exclusivity
        SetImageDisplayState(false);
        SetVideoDisplayState(true);
        lastRequestedVideoState = true;
        lastRequestedImageState = false;
    }

    public void ForceDeactivateAll()
    {
        DebugLog("Force deactivate requested - but maintaining at least one active");
        // Don't actually deactivate all - this would violate our "always one active" rule
        // Instead, reset to default state (image active)
        lastRequestedImageState = true;
        lastRequestedVideoState = false;
        SetVideoDisplayState(false);
        SetImageDisplayState(true);
    }

    public void SetVideoPriority(bool priority)
    {
        videoPriority = priority;
        DebugLog($"Video priority set to: {priority}");
        UpdateDisplayState(); // Recheck current state with new priority
    }

    public void SetMaintainLastState(bool maintain)
    {
        maintainLastActiveState = maintain;
        DebugLog($"Maintain last state set to: {maintain}");
    }

    // Context Menu Methods for Testing
    [ContextMenu("Debug Current State")]
    public void DebugCurrentState()
    {
        DebugLog("=== CURRENT STATE DEBUG ===");
        DebugLog($"API Status - Image: {GetApiImageStatus()}, Video: {GetApiVideoStatus()}, Any: {GetApiAnyStatus()}");
        DebugLog($"Display State - Image: {currentImageDisplayState}, Video: {currentVideoDisplayState}");
        DebugLog($"Last Requested - Image: {lastRequestedImageState}, Video: {lastRequestedVideoState}");
        DebugLog($"Settings - Video Priority: {videoPriority}, Maintain State: {maintainLastActiveState}");
        DebugLog($"GameObjects - Image Active: {imageRenderTextureObject?.activeSelf}, Video Active: {videoRenderTextureObject?.activeSelf}");
        DebugLog("=== END STATE DEBUG ===");
    }

    [ContextMenu("Force Update Display State")]
    public void ForceUpdateDisplayState()
    {
        UpdateDisplayState();
    }

    [ContextMenu("Test Image Activation")]
    public void TestImageActivation()
    {
        ForceActivateImage();
    }

    [ContextMenu("Test Video Activation")]
    public void TestVideoActivation()
    {
        ForceActivateVideo();
    }
}
