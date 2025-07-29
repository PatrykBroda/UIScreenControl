using UnityEngine;
using UnityEngine.UIElements;

public class ScreenManager : MonoBehaviour
{
    [Header("UI Document")]
    public UIDocument uiDocument;

    [Header("Development Settings")]
    public bool enableDebugTap = true;

    private VisualElement root;
    private Label statusText;
    private VisualElement[] screens;
    private VisualElement debugInfo;
    private int currentScreen = 0;

    public int CurrentScreen => currentScreen;
    public int TotalScreens => screens?.Length ?? 0;

    public enum ConnectionStatus
    {
        Connected,
        Disconnected,
        Connecting,
        Error
    }

    void Start()
    {
        if (uiDocument == null)
        {
            Debug.LogError("UIDocument is not assigned! Please assign the UIDocument component.");
            return;
        }

        InitializeUI();
        ShowScreen(0);
        UpdateConnectionStatus("Disconnected", ConnectionStatus.Disconnected);
    }

    void InitializeUI()
    {
        root = uiDocument.rootVisualElement;

        if (root == null)
        {
            Debug.LogError("Root visual element is null! Check your UXML file.");
            return;
        }

        statusText = root.Q<Label>("status-text");
        debugInfo = root.Q<VisualElement>("debug-info");

        screens = new VisualElement[4];
        screens[0] = root.Q<VisualElement>("screen-1");
        screens[1] = root.Q<VisualElement>("screen-2");
        screens[2] = root.Q<VisualElement>("screen-3");
        screens[3] = root.Q<VisualElement>("screen-4");

        for (int i = 0; i < screens.Length; i++)
        {
            if (screens[i] == null)
            {
                Debug.LogError($"Screen {i + 1} element not found! Check your UXML structure.");
            }
        }

        if (enableDebugTap)
        {
            foreach (var screen in screens)
            {
                if (screen != null)
                {
                    screen.RegisterCallback<ClickEvent>(OnScreenClick);
                }
            }

            if (debugInfo != null)
            {
                debugInfo.style.display = DisplayStyle.Flex;
            }
        }
        else
        {
            if (debugInfo != null)
            {
                debugInfo.style.display = DisplayStyle.None;
            }
        }

        Debug.Log("UI initialized successfully!");
    }

    public void ShowScreen(int screenIndex)
    {
        if (screens == null || screenIndex < 0 || screenIndex >= screens.Length)
        {
            Debug.LogWarning($"Invalid screen index: {screenIndex}. Valid range: 0-{screens?.Length - 1 ?? 0}");
            return;
        }

        for (int i = 0; i < screens.Length; i++)
        {
            if (screens[i] != null)
            {
                screens[i].RemoveFromClassList("screen--active");
            }
        }

        if (screens[screenIndex] != null)
        {
            screens[screenIndex].AddToClassList("screen--active");
            currentScreen = screenIndex;
            Debug.Log($"Switched to screen: {screenIndex + 1}");
        }
    }

    public void UpdateConnectionStatus(string status, ConnectionStatus connectionStatus)
    {
        if (statusText == null) return;

        statusText.text = $"Status: {status}";

        statusText.RemoveFromClassList("connection-status__text--connected");
        statusText.RemoveFromClassList("connection-status__text--disconnected");
        statusText.RemoveFromClassList("connection-status__text--connecting");

        string styleClass = connectionStatus switch
        {
            ConnectionStatus.Connected => "connection-status__text--connected",
            ConnectionStatus.Connecting => "connection-status__text--connecting",
            ConnectionStatus.Disconnected => "connection-status__text--disconnected",
            ConnectionStatus.Error => "connection-status__text--disconnected",
            _ => "connection-status__text--disconnected"
        };

        statusText.AddToClassList(styleClass);
    }

    void OnScreenClick(ClickEvent evt)
    {
        if (enableDebugTap)
        {
            NextScreen();
        }
    }

    public void NextScreen()
    {
        int nextScreen = (currentScreen + 1) % screens.Length;
        ShowScreen(nextScreen);
    }

    public void PreviousScreen()
    {
        int prevScreen = (currentScreen - 1 + screens.Length) % screens.Length;
        ShowScreen(prevScreen);
    }

    public void SetDebugMode(bool enabled)
    {
        enableDebugTap = enabled;

        if (debugInfo != null)
        {
            debugInfo.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    void OnValidate()
    {
        if (Application.isPlaying && debugInfo != null)
        {
            debugInfo.style.display = enableDebugTap ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}