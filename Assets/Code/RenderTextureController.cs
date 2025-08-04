using UnityEngine;
using UnityAtoms.BaseAtoms;

/// <summary>
/// Controls render texture GameObjects based on Atom bool variables.
/// Automatically fits render textures to canvas size with no empty space.
/// Ensures mutual exclusion between image and video render textures.
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

    [Header("Mutual Exclusion Settings")]
    [SerializeField] private bool videoPriority = true; // If true, video takes priority over image when both try to activate
    [SerializeField] private bool enableMutualExclusionWarnings = true;

    [Header("Atom Bool Variables")]
    [SerializeField] private BoolVariable isAnyActive;
    [SerializeField] private BoolVariable isImageActive;
    [SerializeField] private BoolVariable isVideoActive;

    [Header("Optional: Atom Bool References")]
    [SerializeField] private BoolReference isAnyActiveRef;
    [SerializeField] private BoolReference isImageActiveRef;
    [SerializeField] private BoolReference isVideoActiveRef;

    // Track the last active state to help with conflict resolution
    private bool lastVideoState = false;
    private bool lastImageState = false;

    private void Start()
    {
        // Subscribe to atom bool variable changes
        if (isAnyActive != null)
            isAnyActive.Changed.Register(OnAnyActiveChanged);

        if (isImageActive != null)
            isImageActive.Changed.Register(OnImageActiveChanged);

        if (isVideoActive != null)
            isVideoActive.Changed.Register(OnVideoActiveChanged);

        // Initialize last states
        lastVideoState = GetBoolValue(isVideoActive, isVideoActiveRef);
        lastImageState = GetBoolValue(isImageActive, isImageActiveRef);

        // Initial setup
        UpdateRenderTextures();
    }

    private void OnDestroy()
    {
        // Unsubscribe from atom bool variable changes
        if (isAnyActive != null)
            isAnyActive.Changed.Unregister(OnAnyActiveChanged);

        if (isImageActive != null)
            isImageActive.Changed.Unregister(OnImageActiveChanged);

        if (isVideoActive != null)
            isVideoActive.Changed.Unregister(OnVideoActiveChanged);
    }

    private void OnAnyActiveChanged(bool value)
    {
        UpdateRenderTextures();
    }

    private void OnImageActiveChanged(bool value)
    {
        if (value && !lastImageState) // Image is being activated
        {
            HandleImageActivation();
        }
        lastImageState = value;
        UpdateRenderTextures();
    }

    private void OnVideoActiveChanged(bool value)
    {
        if (value && !lastVideoState) // Video is being activated
        {
            HandleVideoActivation();
        }
        lastVideoState = value;
        UpdateRenderTextures();
    }

    private void HandleImageActivation()
    {
        bool videoActive = GetBoolValue(isVideoActive, isVideoActiveRef);

        if (videoActive)
        {
            if (enableMutualExclusionWarnings)
            {
                Debug.LogWarning("[RenderTextureController] Conflict detected: Both image and video render textures are trying to be active simultaneously!");
            }

            if (videoPriority)
            {
                if (enableMutualExclusionWarnings)
                {
                    Debug.LogWarning("[RenderTextureController] Video has priority - deactivating image render texture.");
                }
                // Deactivate image
                SetImageActiveInternal(false);
            }
            else
            {
                if (enableMutualExclusionWarnings)
                {
                    Debug.LogWarning("[RenderTextureController] Image has priority - deactivating video render texture.");
                }
                // Deactivate video
                SetVideoActiveInternal(false);
            }
        }
    }

    private void HandleVideoActivation()
    {
        bool imageActive = GetBoolValue(isImageActive, isImageActiveRef);

        if (imageActive)
        {
            if (enableMutualExclusionWarnings)
            {
                Debug.LogWarning("[RenderTextureController] Conflict detected: Both image and video render textures are trying to be active simultaneously!");
            }

            if (videoPriority)
            {
                if (enableMutualExclusionWarnings)
                {
                    Debug.LogWarning("[RenderTextureController] Video has priority - deactivating image render texture.");
                }
                // Deactivate image
                SetImageActiveInternal(false);
            }
            else
            {
                if (enableMutualExclusionWarnings)
                {
                    Debug.LogWarning("[RenderTextureController] Image has priority - deactivating video render texture.");
                }
                // Deactivate video
                SetVideoActiveInternal(false);
            }
        }
    }

    private void UpdateRenderTextures()
    {
        bool anyActive = GetBoolValue(isAnyActive, isAnyActiveRef);
        bool imageActive = GetBoolValue(isImageActive, isImageActiveRef);
        bool videoActive = GetBoolValue(isVideoActive, isVideoActiveRef);

        // Check for mutual exclusion violation and resolve it
        if (imageActive && videoActive)
        {
            if (enableMutualExclusionWarnings)
            {
                Debug.LogWarning("[RenderTextureController] Mutual exclusion violation detected during update - resolving based on priority.");
            }

            if (videoPriority)
            {
                imageActive = false;
                SetImageActiveInternal(false);
            }
            else
            {
                videoActive = false;
                SetVideoActiveInternal(false);
            }
        }

        // Only activate render textures if something is active
        if (!anyActive)
        {
            SetGameObjectActive(videoRenderTextureObject, false);
            SetGameObjectActive(imageRenderTextureObject, false);
            return;
        }

        // Control video render texture
        SetGameObjectActive(videoRenderTextureObject, videoActive);

        // Control image render texture
        SetGameObjectActive(imageRenderTextureObject, imageActive);

        // Debug logging
        Debug.Log($"Render Textures Updated - Any: {anyActive}, Video: {videoActive}, Image: {imageActive}");
    }

    private bool GetBoolValue(BoolVariable variable, BoolReference reference)
    {
        // Try to get value from BoolVariable first, then BoolReference as fallback
        if (variable != null)
            return variable.Value;

        if (reference != null)
            return reference.Value;

        return false;
    }

    private void SetGameObjectActive(GameObject obj, bool active)
    {
        if (obj != null && obj.activeSelf != active)
        {
            obj.SetActive(active);
        }
    }

    // Internal methods to set values without triggering mutual exclusion checks
    private void SetVideoActiveInternal(bool active)
    {
        if (isVideoActive != null)
        {
            lastVideoState = active;
            isVideoActive.Value = active;
        }
        else if (isVideoActiveRef != null)
        {
            lastVideoState = active;
            isVideoActiveRef.Value = active;
        }
    }

    private void SetImageActiveInternal(bool active)
    {
        if (isImageActive != null)
        {
            lastImageState = active;
            isImageActive.Value = active;
        }
        else if (isImageActiveRef != null)
        {
            lastImageState = active;
            isImageActiveRef.Value = active;
        }
    }

    private void FitRenderTextureToCanvas(GameObject renderTextureObject)
    {
        if (renderTextureObject == null || targetCanvas == null)
            return;

        // Get the canvas dimensions
        Vector2 canvasSize = GetCanvasSize();
        if (canvasSize == Vector2.zero)
            return;

        // Try to get RectTransform first (for UI objects)
        RectTransform rectTransform = renderTextureObject.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            FitUIRenderTexture(rectTransform, canvasSize);
        }
        else
        {
            // Fallback to regular Transform (for 3D objects)
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
            {
                sizeFitter.enabled = false;
            }
            if (aspectFitter != null)
            {
                aspectFitter.enabled = false;
            }
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
            // Fallback to current rect size or canvas size
            textureSize = rectTransform.sizeDelta != Vector2.zero ? rectTransform.sizeDelta : canvasSize;
        }

        // Calculate scale to FILL canvas completely (crop if necessary - no empty space)
        float canvasAspect = canvasSize.x / canvasSize.y;
        float textureAspect = textureSize.x / textureSize.y;

        Vector2 newSize;
        if (textureAspect > canvasAspect)
        {
            // Texture is wider than canvas - scale to fill height, width will overflow
            newSize.y = canvasSize.y;
            newSize.x = canvasSize.y * textureAspect;
        }
        else
        {
            // Texture is taller than canvas - scale to fill width, height will overflow  
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

        // Apply the new size using sizeDelta
        rectTransform.sizeDelta = newSize;

        // Center the render texture
        rectTransform.anchoredPosition = Vector2.zero;

        // If there's a layout element, update it too
        if (layoutElement != null && forceOverrideLayout)
        {
            layoutElement.preferredWidth = newSize.x;
            layoutElement.preferredHeight = newSize.y;
        }

        // Force layout rebuild if in a layout group
        UnityEngine.UI.LayoutGroup parentLayout = rectTransform.GetComponentInParent<UnityEngine.UI.LayoutGroup>();
        if (parentLayout != null)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parentLayout.transform as RectTransform);
        }

        Debug.Log($"Fitted UI RenderTexture (SizeDelta) - Canvas: {canvasSize}, Texture: {textureSize}, New Size: {newSize}, Final SizeDelta: {rectTransform.sizeDelta}");
    }

    private void FitUIRenderTextureWithAnchors(RectTransform rectTransform, Vector2 canvasSize)
    {
        // Use anchor stretching to fill the entire canvas
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        // Get the texture for UV rect adjustment
        UnityEngine.UI.RawImage rawImage = rectTransform.GetComponent<UnityEngine.UI.RawImage>();

        if (rawImage != null && rawImage.texture != null)
        {
            Vector2 textureSize = new Vector2(rawImage.texture.width, rawImage.texture.height);

            // Calculate aspect ratios
            float canvasAspect = canvasSize.x / canvasSize.y;
            float textureAspect = textureSize.x / textureSize.y;

            // Always fill the entire canvas - crop the texture if needed
            Rect uvRect = new Rect(0, 0, 1, 1);

            if (textureAspect > canvasAspect)
            {
                // Texture is wider than canvas - crop left and right sides
                float scale = canvasAspect / textureAspect;
                float offset = (1f - scale) * 0.5f;
                uvRect = new Rect(offset, 0, scale, 1);
            }
            else if (textureAspect < canvasAspect)
            {
                // Texture is taller than canvas - crop top and bottom
                float scale = textureAspect / canvasAspect;
                float offset = (1f - scale) * 0.5f;
                uvRect = new Rect(0, offset, 1, scale);
            }
            // If aspects match exactly, uvRect stays (0,0,1,1)

            rawImage.uvRect = uvRect;

            Debug.Log($"Fitted UI RenderTexture (Full Canvas) - Canvas: {canvasSize}, Texture: {textureSize}, Canvas Aspect: {canvasAspect:F2}, Texture Aspect: {textureAspect:F2}, UV Rect: {uvRect}");
        }
        else
        {
            Debug.Log($"Fitted UI RenderTexture (Full Canvas) - Stretched to fill entire canvas: {canvasSize}");
        }
    }

    private void FitWorldSpaceRenderTexture(Transform transform, Vector2 canvasSize)
    {
        // For world space objects, we need to convert canvas size to world units
        // This is a simplified approach - you may need to adjust based on your camera setup

        // Get the original scale
        Vector3 originalScale = transform.localScale;

        // Calculate the scale factor based on canvas size
        // Assuming the object should fill the screen
        float canvasAspect = canvasSize.x / canvasSize.y;

        // Simple scaling approach - you may want to customize this based on your needs
        float scaleFactor = Mathf.Max(canvasSize.x / 1920f, canvasSize.y / 1080f); // Normalize to common resolution

        Vector3 newScale = originalScale * scaleFactor;
        transform.localScale = newScale;

        Debug.Log($"Fitted World Space RenderTexture - Canvas: {canvasSize}, Scale Factor: {scaleFactor}");
    }

    private Vector2 GetCanvasSize()
    {
        if (targetCanvas == null)
            return Vector2.zero;

        // Try to get size from assigned RectTransform first
        if (canvasRectTransform != null)
        {
            return canvasRectTransform.sizeDelta;
        }

        // Fallback to canvas RectTransform
        RectTransform canvasRect = targetCanvas.GetComponent<RectTransform>();
        if (canvasRect != null)
        {
            return canvasRect.sizeDelta;
        }

        // Last resort - use screen dimensions
        return new Vector2(Screen.width, Screen.height);
    }

    // Public methods for manual control with mutual exclusion
    public void SetVideoActive(bool active)
    {
        if (active)
        {
            // Check if image is currently active and handle conflict
            bool imageCurrentlyActive = GetBoolValue(isImageActive, isImageActiveRef);
            if (imageCurrentlyActive)
            {
                if (enableMutualExclusionWarnings)
                {
                    Debug.LogWarning("[RenderTextureController] SetVideoActive(true) called while image is active - deactivating image due to mutual exclusion.");
                }
                SetImageActiveInternal(false);
            }
        }

        SetVideoActiveInternal(active);
    }

    public void SetImageActive(bool active)
    {
        if (active)
        {
            // Check if video is currently active and handle conflict
            bool videoCurrentlyActive = GetBoolValue(isVideoActive, isVideoActiveRef);
            if (videoCurrentlyActive)
            {
                if (enableMutualExclusionWarnings)
                {
                    Debug.LogWarning("[RenderTextureController] SetImageActive(true) called while video is active - deactivating video due to mutual exclusion.");
                }
                SetVideoActiveInternal(false);
            }
        }

        SetImageActiveInternal(active);
    }

    public void SetAnyActive(bool active)
    {
        if (isAnyActive != null)
            isAnyActive.Value = active;
        else if (isAnyActiveRef != null)
            isAnyActiveRef.Value = active;
    }

    // Method to toggle between video and image (safe way to switch)
    public void SwitchToVideo()
    {
        if (enableMutualExclusionWarnings)
        {
            Debug.Log("[RenderTextureController] Switching to video render texture.");
        }
        SetImageActiveInternal(false);
        SetVideoActiveInternal(true);
    }

    public void SwitchToImage()
    {
        if (enableMutualExclusionWarnings)
        {
            Debug.Log("[RenderTextureController] Switching to image render texture.");
        }
        SetVideoActiveInternal(false);
        SetImageActiveInternal(true);
    }

    // Method to deactivate both
    public void DeactivateBoth()
    {
        SetVideoActiveInternal(false);
        SetImageActiveInternal(false);
    }

    // Method to update render textures manually if needed
    [ContextMenu("Update Render Textures")]
    public void ManualUpdateRenderTextures()
    {
        UpdateRenderTextures();
    }

    // Method to manually fit render textures to canvas
    [ContextMenu("Fit Render Textures to Canvas")]
    public void FitRenderTexturesToCanvas()
    {
        if (videoRenderTextureObject != null && videoRenderTextureObject.activeSelf)
            FitRenderTextureToCanvas(videoRenderTextureObject);

        if (imageRenderTextureObject != null && imageRenderTextureObject.activeSelf)
            FitRenderTextureToCanvas(imageRenderTextureObject);
    }

    // Public method to update canvas reference and refit
    public void SetCanvas(Canvas newCanvas)
    {
        targetCanvas = newCanvas;
        if (newCanvas != null)
        {
            canvasRectTransform = newCanvas.GetComponent<RectTransform>();
            FitRenderTexturesToCanvas();
        }
    }

    // Force the render textures to fill the entire canvas
    [ContextMenu("Force Fill Entire Canvas")]
    public void ForceFillEntireCanvas()
    {
        useAnchorStretching = true;
        forceOverrideLayout = true;
        FitRenderTexturesToCanvas();
    }

    // Toggle priority system
    public void SetVideoPriority(bool priority)
    {
        videoPriority = priority;
        if (enableMutualExclusionWarnings)
        {
            Debug.Log($"[RenderTextureController] Video priority set to: {priority}");
        }
    }

    // Enable/disable warnings
    public void SetWarningsEnabled(bool enabled)
    {
        enableMutualExclusionWarnings = enabled;
    }

    // Debug method to check current state
    [ContextMenu("Debug Current State")]
    public void DebugCurrentState()
    {
        Vector2 canvasSize = GetCanvasSize();
        bool videoActive = GetBoolValue(isVideoActive, isVideoActiveRef);
        bool imageActive = GetBoolValue(isImageActive, isImageActiveRef);

        Debug.Log($"Canvas Size: {canvasSize}");
        Debug.Log($"Use Anchor Stretching: {useAnchorStretching}");
        Debug.Log($"Force Override Layout: {forceOverrideLayout}");
        Debug.Log($"Video Priority: {videoPriority}");
        Debug.Log($"Warnings Enabled: {enableMutualExclusionWarnings}");
        Debug.Log($"Current States - Video: {videoActive}, Image: {imageActive}");

        if (videoActive && imageActive)
        {
            Debug.LogError("[RenderTextureController] CONFLICT: Both video and image are currently active!");
        }

        if (videoRenderTextureObject != null)
        {
            RectTransform videoRect = videoRenderTextureObject.GetComponent<RectTransform>();
            if (videoRect != null)
            {
                Debug.Log($"Video RenderTexture - Size: {videoRect.sizeDelta}, Anchors: Min{videoRect.anchorMin} Max{videoRect.anchorMax}");
            }
        }

        if (imageRenderTextureObject != null)
        {
            RectTransform imageRect = imageRenderTextureObject.GetComponent<RectTransform>();
            if (imageRect != null)
            {
                Debug.Log($"Image RenderTexture - Size: {imageRect.sizeDelta}, Anchors: Min{imageRect.anchorMin} Max{imageRect.anchorMax}");
            }
        }
    }
}