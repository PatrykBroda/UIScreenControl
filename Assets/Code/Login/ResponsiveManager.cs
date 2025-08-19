using UnityEngine;
using UnityEngine.UIElements;

public class ResponsiveManager : MonoBehaviour
{
    private UIDocument uiDocument;
    private VisualElement loginPanel;
    private Vector2 lastScreenSize;
    private ScreenOrientation lastOrientation;

    // Screen size breakpoints
    private const int MOBILE_SMALL = 480;
    private const int MOBILE_MEDIUM = 768;
    private const int TABLET = 1024;
    private const int DESKTOP_SMALL = 1366;
    private const int ULTRA_WIDE = 1920;

    void Start()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("UIDocument not found!");
            return;
        }

        loginPanel = uiDocument.rootVisualElement.Q<VisualElement>("login-panel");
        if (loginPanel == null)
        {
            Debug.LogError("login-panel not found!");
            return;
        }

        UpdateResponsiveClasses();
        lastScreenSize = new Vector2(Screen.width, Screen.height);
        lastOrientation = Screen.orientation;
    }

    void Update()
    {
        Vector2 currentScreenSize = new Vector2(Screen.width, Screen.height);
        ScreenOrientation currentOrientation = Screen.orientation;

        if (lastScreenSize != currentScreenSize || lastOrientation != currentOrientation)
        {
            UpdateResponsiveClasses();
            lastScreenSize = currentScreenSize;
            lastOrientation = currentOrientation;
        }
    }

    void UpdateResponsiveClasses()
    {
        if (loginPanel == null) return;

        // Clear all responsive classes
        ClearResponsiveClasses();

        // Determine screen size category
        int screenWidth = Screen.width;
        string sizeClass = GetSizeClass(screenWidth);

        // Apply size class
        loginPanel.AddToClassList(sizeClass);

        // Apply orientation class
        bool isLandscape = Screen.width > Screen.height;
        string orientationClass = isLandscape ? "landscape" : "portrait";
        loginPanel.AddToClassList(orientationClass);

        Debug.Log($"Applied classes: {sizeClass}, {orientationClass} (Screen: {Screen.width}x{Screen.height})");
    }

    string GetSizeClass(int width)
    {
        if (width <= MOBILE_SMALL)
            return "mobile-small";
        else if (width <= MOBILE_MEDIUM)
            return "mobile-medium";
        else if (width <= TABLET)
            return "tablet";
        else if (width <= DESKTOP_SMALL)
            return "desktop-small";
        else if (width <= ULTRA_WIDE)
            return "desktop-large";
        else
            return "ultra-wide";
    }

    void ClearResponsiveClasses()
    {
        // Remove all responsive classes
        string[] responsiveClasses = {
            "mobile-small", "mobile-medium", "tablet",
            "desktop-small", "desktop-large", "ultra-wide",
            "landscape", "portrait"
        };

        foreach (string className in responsiveClasses)
        {
            loginPanel.RemoveFromClassList(className);
        }
    }
}