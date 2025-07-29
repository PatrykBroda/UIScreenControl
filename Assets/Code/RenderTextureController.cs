using UnityEngine;
using UnityAtoms.BaseAtoms;

/// <summary>
/// Controls render texture GameObjects based on Atom bool variables.
/// Automatically fits render textures to canvas size with no empty space.
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

    [Header("Atom Bool Variables")]
    [SerializeField] private BoolVariable isAnyActive;
    [SerializeField] private BoolVariable isImageActive;
    [SerializeField] private BoolVariable isVideoActive;

    [Header("Optional: Atom Bool References")]
    [SerializeField] private BoolReference isAnyActiveRef;
    [SerializeField] private BoolReference isImageActiveRef;
    [SerializeField] private BoolReference isVideoActiveRef;

    private void Start()
    {
        // Subscribe to atom bool variable changes
        if (isAnyActive != null)
            isAnyActive.Changed.Register(OnAnyActiveChanged);

        if (isImageActive != null)
            isImageActive.Changed.Register(OnImageActiveChanged);

        if (isVideoActive != null)
            isVideoActive.Changed.Register(OnVideoActiveChanged);

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
        UpdateRenderTextures();
    }

    private void OnVideoActiveChanged(bool value)
    {
        UpdateRenderTextures();
    }

    private void UpdateRenderTextures()
    {
        bool anyActive = GetBoolValue(isAnyActive, isAnyActiveRef);
        bool imageActive = GetBoolValue(isImageActive, isImageActiveRef);
        bool videoActive = GetBoolValue(isVideoActive, isVideoActiveRef);

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

    // Public methods for manual control if needed
    public void SetVideoActive(bool active)
    {
        if (isVideoActive != null)
            isVideoActive.Value = active;
        else if (isVideoActiveRef != null)
            isVideoActiveRef.Value = active;
    }

    public void SetImageActive(bool active)
    {
        if (isImageActive != null)
            isImageActive.Value = active;
        else if (isImageActiveRef != null)
            isImageActiveRef.Value = active;
    }

    public void SetAnyActive(bool active)
    {
        if (isAnyActive != null)
            isAnyActive.Value = active;
        else if (isAnyActiveRef != null)
            isAnyActiveRef.Value = active;
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

    // Debug method to check current state
    [ContextMenu("Debug Current State")]
    public void DebugCurrentState()
    {
        Vector2 canvasSize = GetCanvasSize();
        Debug.Log($"Canvas Size: {canvasSize}");
        Debug.Log($"Use Anchor Stretching: {useAnchorStretching}");
        Debug.Log($"Force Override Layout: {forceOverrideLayout}");

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