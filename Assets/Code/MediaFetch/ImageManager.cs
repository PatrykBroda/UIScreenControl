using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.UI;
using UIButton = UnityEngine.UIElements.Button;

public class ImageManager : MonoBehaviour
{
    // Log tag for easy filtering
    private const string LOG_TAG = "[ImageManager]";

    [Header("UI Toolkit Overlay (Simple Text Only)")]
    public UIDocument overlayDocument;

    [Header("Render Texture Output")]
    public RenderTexture outputRenderTexture;

    [Header("Settings")]
    public float pollingInterval = 2f;
    public string serverURL = "https://unity-server-control-patrykbroda.replit.app";

    [Header("Connection Reference")]
    public ConnectionManager connectionManager;

    private Label statusText;
    private Label serverUrl;
    private Label userInfo;
    private Label pollingStatus;
    private Label lastUpdateTime;
    private Label currentImageName;

    private UIButton manualPollBtn;
    private UIButton togglePollingBtn;
    private UIButton debugTokensBtn;
    private UIButton clearTokensBtn;

    private bool isPolling = false;
    private bool isConnected = false;
    private string currentImageId = "";
    private Texture2D currentTexture;

    [System.Serializable]
    public class ImageCurrentResponse
    {
        public bool success;
        public ImageInfo image;
        public string timestamp;
        public int userId;

        public override string ToString()
        {
            return $"ImageCurrentResponse(success:{success}, image:{(image?.id ?? "null")}, timestamp:{timestamp}, userId:{userId})";
        }
    }

    [System.Serializable]
    public class ImageInfo
    {
        public string id;
        public string filename;
        public string originalName;
        public string url;

        public override string ToString()
        {
            return $"ImageInfo(id:{id}, filename:{filename}, originalName:{originalName})";
        }
    }

    void Start()
    {
        Debug.Log($"{LOG_TAG} Start() called");

        InitializeOverlay();

        if (string.IsNullOrEmpty(serverURL))
        {
            serverURL = "https://unity-server-control-patrykbroda.replit.app";
            Debug.Log($"{LOG_TAG} Set default serverURL to: {serverURL}");
        }
        else
        {
            Debug.Log($"{LOG_TAG} Using configured serverURL: {serverURL}");
        }

        UpdateStatus("Initializing...", ConnectionState.Connecting);

        if (connectionManager == null)
        {
            connectionManager = FindFirstObjectByType<ConnectionManager>();
            if (connectionManager == null)
            {
                Debug.LogWarning($"{LOG_TAG} No ConnectionManager found! Will try to start polling anyway.");
            }
        }

        StartCoroutine(WaitForConnectionThenPoll());
    }

    private void InitializeOverlay()
    {
        Debug.Log($"{LOG_TAG} Initializing simple UI overlay...");

        if (overlayDocument == null)
        {
            overlayDocument = GetComponent<UIDocument>();
        }

        if (overlayDocument != null)
        {
            VisualElement root = overlayDocument.rootVisualElement;

            statusText = root.Q<Label>("status-text");
            serverUrl = root.Q<Label>("server-url");
            userInfo = root.Q<Label>("user-info");
            pollingStatus = root.Q<Label>("polling-status");
            lastUpdateTime = root.Q<Label>("last-update-time");
            currentImageName = root.Q<Label>("current-image-name");

            manualPollBtn = root.Q<UIButton>("manual-poll-btn");
            togglePollingBtn = root.Q<UIButton>("toggle-polling-btn");
            debugTokensBtn = root.Q<UIButton>("debug-tokens-btn");
            clearTokensBtn = root.Q<UIButton>("clear-tokens-btn");

            if (manualPollBtn != null)
                manualPollBtn.clicked += ManualPoll;

            if (togglePollingBtn != null)
                togglePollingBtn.clicked += TogglePolling;

            if (debugTokensBtn != null)
                debugTokensBtn.clicked += DebugTokenSources;

            if (clearTokensBtn != null)
                clearTokensBtn.clicked += ClearAllTokens;

            if (serverUrl != null)
            {
                serverUrl.text = $"Server: {serverURL}";
            }

            Debug.Log($"{LOG_TAG} Simple overlay initialized successfully");
            Debug.Log($"{LOG_TAG}   - Status text: {(statusText != null ? "✅" : "❌")}");
            Debug.Log($"{LOG_TAG}   - Server URL: {(serverUrl != null ? "✅" : "❌")}");
            Debug.Log($"{LOG_TAG}   - User info: {(userInfo != null ? "✅" : "❌")}");
            Debug.Log($"{LOG_TAG}   - Debug buttons: {(manualPollBtn != null ? "✅" : "❌")}");
        }
        else
        {
            Debug.Log($"{LOG_TAG} No UI overlay document assigned - running without overlay");
        }

        if (outputRenderTexture != null)
        {
            Debug.Log($"{LOG_TAG} RenderTexture assigned - {outputRenderTexture.width}x{outputRenderTexture.height} format:{outputRenderTexture.format}");
        }
        else
        {
            Debug.LogWarning($"{LOG_TAG} No RenderTexture assigned - images will not be displayed!");
        }
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    private void UpdateStatus(string message, ConnectionState state)
    {
        if (statusText != null)
        {
            string stateText = state switch
            {
                ConnectionState.Connected => "🟢",
                ConnectionState.Connecting => "🟡",
                ConnectionState.Disconnected => "🔴",
                _ => "⚪"
            };
            statusText.text = $"{stateText} {message}";
        }

        Debug.Log($"{LOG_TAG} Status: {message} (State: {state}, isPolling: {isPolling}, isConnected: {isConnected})");
    }

    private void UpdateOverlayInfo()
    {
        if (userInfo != null && connectionManager?.loginData != null)
        {
            userInfo.text = $"User: {connectionManager.loginData.UserEmail} (ID: {connectionManager.loginData.UserId})";
        }
        else if (userInfo != null)
        {
            userInfo.text = "User: Not authenticated";
        }

        if (pollingStatus != null)
        {
            pollingStatus.text = $"Polling: {(isPolling ? "Active" : "Inactive")}";
        }

        if (lastUpdateTime != null)
        {
            lastUpdateTime.text = $"Last Update: {System.DateTime.Now.ToString("HH:mm:ss")}";
        }
    }

    private IEnumerator WaitForConnectionThenPoll()
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

        Debug.Log($"{LOG_TAG} Connection established! Starting image polling...");
        UpdateStatus("Connected! Starting image polling...", ConnectionState.Connected);

        if (string.IsNullOrEmpty(serverURL))
        {
            serverURL = "https://unity-server-control-patrykbroda.replit.app";
            Debug.Log($"{LOG_TAG} Re-set serverURL to: {serverURL}");
        }

        isConnected = true;
        StartImagePolling();
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

                Debug.Log($"{LOG_TAG} Connection status - Connected: {isConnected}, Authenticated: {isAuthenticated}");
                return isConnected && isAuthenticated;
            }
        }
        catch (System.Exception e)
        {
            Debug.Log($"{LOG_TAG} Could not check connection status: {e.Message}");
        }

        try
        {
            var property = connectionManager.GetType().GetProperty("IsConnected");
            if (property != null)
            {
                return (bool)property.GetValue(connectionManager);
            }
        }
        catch (System.Exception e)
        {
            Debug.Log($"{LOG_TAG} Could not check IsConnected property: {e.Message}");
        }

        Debug.Log($"{LOG_TAG} Cannot determine connection status, assuming ready");
        return true;
    }

    public void StartImagePolling()
    {
        if (isPolling)
        {
            Debug.Log($"{LOG_TAG} Already polling, skipping StartImagePolling");
            return;
        }

        Debug.Log($"{LOG_TAG} StartImagePolling() called");
        Debug.Log($"{LOG_TAG} serverURL = '{serverURL}'");
        Debug.Log($"{LOG_TAG} connectionManager = {(connectionManager != null ? "Found" : "NULL")}");

        if (string.IsNullOrEmpty(serverURL))
        {
            serverURL = "https://unity-server-control-patrykbroda.replit.app";
            Debug.LogWarning($"{LOG_TAG} serverURL was empty! Set to default: {serverURL}");
        }

        Debug.Log($"{LOG_TAG} Setting isPolling = true, final serverURL = '{serverURL}'");
        isPolling = true;
        Debug.Log($"{LOG_TAG} Started image polling every 2 seconds");

        UpdateStatus("Polling for images...", ConnectionState.Connected);
        UpdateOverlayInfo();

        StartCoroutine(ImagePollingLoop());
    }

    public void StopImagePolling()
    {
        Debug.Log($"{LOG_TAG} Stopping image polling");
        isPolling = false;
        UpdateStatus("Polling stopped", ConnectionState.Disconnected);
        UpdateOverlayInfo();
    }

    private IEnumerator ImagePollingLoop()
    {
        Debug.Log($"{LOG_TAG} ImagePollingLoop started");

        while (isPolling && isConnected)
        {
            yield return StartCoroutine(CheckForNewImage());
            yield return new WaitForSeconds(pollingInterval);
        }

        Debug.Log($"{LOG_TAG} ImagePollingLoop ended");
    }

    private IEnumerator CheckForNewImage()
    {
        string url = $"{serverURL}/api/image/current";
        Debug.Log($"{LOG_TAG} Polling URL: {url}");

        string authToken = "";

        if (connectionManager != null && connectionManager.loginData != null && connectionManager.loginData.IsTokenValid())
        {
            authToken = connectionManager.loginData.AuthToken;
            Debug.Log($"{LOG_TAG} ✅ Using auth token from UserLoginData ScriptableObject (primary source)");
        }
        else if (!string.IsNullOrEmpty(PlayerPrefs.GetString("auth_token", "")))
        {
            authToken = PlayerPrefs.GetString("auth_token", "");
            Debug.LogWarning($"{LOG_TAG} ⚠️ Using auth token from PlayerPrefs 'auth_token' (fallback)");

            if (connectionManager?.loginData != null)
            {
                connectionManager.loginData.SetAuthToken(authToken);
                Debug.Log($"{LOG_TAG} 🔄 Restored token to ScriptableObject from PlayerPrefs");
            }
        }
        else if (!string.IsNullOrEmpty(PlayerPrefs.GetString("AuthToken", "")))
        {
            authToken = PlayerPrefs.GetString("AuthToken", "");
            Debug.LogWarning($"{LOG_TAG} ⚠️ Using auth token from PlayerPrefs 'AuthToken' (legacy fallback)");
        }
        else
        {
            Debug.LogError($"{LOG_TAG} ❌ No auth token found in any source!");
            UpdateStatus("No authentication token found", ConnectionState.Disconnected);
            yield break;
        }

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("Authorization", "Bearer " + authToken);

            yield return www.SendWebRequest();

            Debug.Log($"{LOG_TAG} Response Code: {www.responseCode}");

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{LOG_TAG} Request failed - {www.error}");

                if (www.responseCode == 401)
                {
                    Debug.LogError($"{LOG_TAG} Authentication failed - token may be expired");
                    UpdateStatus("Authentication expired", ConnectionState.Disconnected);

                    PlayerPrefs.DeleteKey("auth_token");
                    PlayerPrefs.DeleteKey("AuthToken");
                    PlayerPrefs.Save();

                    if (connectionManager?.loginData != null)
                    {
                        connectionManager.loginData.Logout();
                    }

                    StopImagePolling();
                    yield break;
                }

                // Handle 502 Bad Gateway specifically
                if (www.responseCode == 502)
                {
                    Debug.LogWarning($"{LOG_TAG} 502 Server error - will retry next poll cycle");
                    UpdateStatus("Server temporarily unavailable (502)", ConnectionState.Connecting);
                    yield break; // Skip this cycle, will retry in 2 seconds
                }

                UpdateStatus($"Request failed: {www.error}", ConnectionState.Disconnected);
            }
            else
            {
                StartCoroutine(ProcessImageResponse(www.downloadHandler.text));
            }
        }

        UpdateOverlayInfo();
    }

    private IEnumerator ProcessImageResponse(string rawResponse)
    {
        Debug.Log($"{LOG_TAG} RAW SERVER RESPONSE: {rawResponse}");

        ImageCurrentResponse response = null;
        bool parseSuccess = false;

        try
        {
            response = JsonUtility.FromJson<ImageCurrentResponse>(rawResponse);
            parseSuccess = true;
            Debug.Log($"{LOG_TAG} ✅ JSON parsing successful");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LOG_TAG} JSON parsing failed: {e.Message}");
            UpdateStatus($"JSON parsing failed", ConnectionState.Disconnected);
            parseSuccess = false;
        }

        if (!parseSuccess || response == null)
        {
            Debug.LogError($"{LOG_TAG} Could not parse server response");
            yield break;
        }

        int expectedUserId = 0;
        if (connectionManager?.loginData != null)
        {
            expectedUserId = connectionManager.loginData.UserId;

            if (response.userId != expectedUserId)
            {
                Debug.LogError($"{LOG_TAG} 🚨 CRITICAL USER ID MISMATCH! Expected: {expectedUserId}, Got: {response.userId}");
                UpdateStatus("🚨 SECURITY ERROR: User ID mismatch!", ConnectionState.Disconnected);
                yield break;
            }
            else
            {
                Debug.Log($"{LOG_TAG} ✅ User ID matches correctly: {expectedUserId}");
            }
        }

        bool hasCurrentImage = response.image != null && !string.IsNullOrEmpty(response.image.id);

        if (hasCurrentImage)
        {
            if (response.image.id != currentImageId)
            {
                Debug.Log($"{LOG_TAG} New image detected! Old: '{currentImageId}' -> New: '{response.image.id}'");
                yield return StartCoroutine(DownloadAndDisplayImage(response.image));
            }
            else
            {
                Debug.Log($"{LOG_TAG} Same image as before (ID: {currentImageId}), no download needed");
                UpdateStatus($"Current: {response.image.originalName}", ConnectionState.Connected);
            }
        }
        else
        {
            Debug.Log($"{LOG_TAG} ❌ No current image detected");
            UpdateStatus("No image set on server", ConnectionState.Connected);

            if (currentImageName != null)
            {
                currentImageName.text = "No image";
            }

            if (!string.IsNullOrEmpty(currentImageId))
            {
                Debug.Log($"{LOG_TAG} Clearing previous image");
                ClearCurrentImage();
            }
        }
    }

    private IEnumerator DownloadAndDisplayImage(ImageInfo imageInfo)
    {
        Debug.Log($"{LOG_TAG} Starting download of image: {imageInfo}");

        string imageUrl = $"{serverURL}{imageInfo.url}";
        Debug.Log($"{LOG_TAG} Full image URL: {imageUrl}");

        UpdateStatus($"Downloading: {imageInfo.originalName}...", ConnectionState.Connected);

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return www.SendWebRequest();

            Debug.Log($"{LOG_TAG} Download response code: {www.responseCode}");

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{LOG_TAG} Image download failed: {www.error}");
                UpdateStatus($"Download failed: {www.error}", ConnectionState.Disconnected);
            }
            else
            {
                Debug.Log($"{LOG_TAG} ✅ Image download successful!");

                Texture2D downloadedTexture = ((DownloadHandlerTexture)www.downloadHandler).texture;
                Debug.Log($"{LOG_TAG} Downloaded texture size: {downloadedTexture.width}x{downloadedTexture.height}");

                if (currentTexture != null)
                {
                    Destroy(currentTexture);
                }

                currentTexture = downloadedTexture;
                currentImageId = imageInfo.id;

                if (outputRenderTexture != null)
                {
                    CopyTextureToRenderTexture(currentTexture, outputRenderTexture);
                    Debug.Log($"{LOG_TAG} ✅ RenderTexture updated successfully");
                }
                else
                {
                    Debug.LogWarning($"{LOG_TAG} ⚠️ No RenderTexture assigned");
                }

                if (currentImageName != null)
                {
                    currentImageName.text = imageInfo.originalName;
                }

                UpdateStatus($"Displaying: {imageInfo.originalName}", ConnectionState.Connected);
                Debug.Log($"{LOG_TAG} Successfully loaded and displayed image: {imageInfo.originalName}");
            }
        }
    }

    private void ClearCurrentImage()
    {
        Debug.Log($"{LOG_TAG} ClearCurrentImage() called");

        if (currentTexture != null)
        {
            Destroy(currentTexture);
            currentTexture = null;
        }

        if (outputRenderTexture != null)
        {
            ClearRenderTexture(outputRenderTexture);
            Debug.Log($"{LOG_TAG} ✅ RenderTexture cleared");
        }

        if (currentImageName != null)
        {
            currentImageName.text = "No image";
        }

        currentImageId = "";
        Debug.Log($"{LOG_TAG} Current image cleared");
    }

    private void CopyTextureToRenderTexture(Texture2D sourceTexture, RenderTexture targetRenderTexture)
    {
        if (sourceTexture == null || targetRenderTexture == null)
        {
            Debug.LogWarning($"{LOG_TAG} Cannot copy to render texture - source or target is null");
            return;
        }

        try
        {
            RenderTexture previousActive = RenderTexture.active;

            if (!targetRenderTexture.IsCreated())
            {
                targetRenderTexture.Create();
                Debug.Log($"{LOG_TAG} Created RenderTexture");
            }

            RenderTexture.active = targetRenderTexture;
            GL.Clear(true, true, Color.black);

            Graphics.Blit(sourceTexture, targetRenderTexture);

            RenderTexture.active = previousActive;

            Debug.Log($"{LOG_TAG} Successfully copied {sourceTexture.width}x{sourceTexture.height} texture to {targetRenderTexture.width}x{targetRenderTexture.height} RenderTexture using Blit");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LOG_TAG} Failed to copy texture to render texture: {e.Message}");
        }
    }

    private void ClearRenderTexture(RenderTexture renderTexture)
    {
        if (renderTexture == null) return;

        try
        {
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previousActive;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LOG_TAG} Failed to clear render texture: {e.Message}");
        }
    }

    void OnDestroy()
    {
        Debug.Log($"{LOG_TAG} OnDestroy() called");
        StopImagePolling();

        if (currentTexture != null)
        {
            Destroy(currentTexture);
        }

        if (outputRenderTexture != null)
        {
            ClearRenderTexture(outputRenderTexture);
        }
    }

    public void ManualPoll()
    {
        Debug.Log($"{LOG_TAG} Manual poll triggered");
        if (isConnected)
        {
            StartCoroutine(CheckForNewImage());
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
            StopImagePolling();
        }
        else
        {
            StartImagePolling();
        }
    }

    public void OnAuthenticationRestored()
    {
        Debug.Log($"{LOG_TAG} Authentication restored, restarting polling");
        if (!isPolling && isConnected)
        {
            StartImagePolling();
        }
    }

    [ContextMenu("Debug Token Sources")]
    public void DebugTokenSources()
    {
        Debug.Log($"{LOG_TAG} === TOKEN SOURCE DEBUG ===");

        if (connectionManager != null && connectionManager.loginData != null)
        {
            Debug.Log($"{LOG_TAG} ✅ ScriptableObject: {connectionManager.loginData.UserEmail} (ID: {connectionManager.loginData.UserId})");
            Debug.Log($"{LOG_TAG}    Token Valid: {connectionManager.loginData.IsTokenValid()}");
        }
        else
        {
            Debug.LogError($"{LOG_TAG} ❌ No ScriptableObject available");
        }

        string authToken1 = PlayerPrefs.GetString("auth_token", "");
        string authToken2 = PlayerPrefs.GetString("AuthToken", "");
        Debug.Log($"{LOG_TAG} PlayerPrefs 'auth_token': {(authToken1.Length > 0 ? "✅ Available" : "❌ Empty")}");
        Debug.Log($"{LOG_TAG} PlayerPrefs 'AuthToken': {(authToken2.Length > 0 ? "✅ Available" : "❌ Empty")}");

        Debug.Log($"{LOG_TAG} === TOKEN SOURCE DEBUG END ===");
    }

    [ContextMenu("Clear All Tokens")]
    public void ClearAllTokens()
    {
        Debug.Log($"{LOG_TAG} 🧹 Clearing all authentication tokens...");

        if (connectionManager?.loginData != null)
        {
            connectionManager.loginData.Logout();
        }

        PlayerPrefs.DeleteKey("auth_token");
        PlayerPrefs.DeleteKey("AuthToken");
        PlayerPrefs.Save();

        UpdateStatus("Tokens cleared - please log in", ConnectionState.Disconnected);
        Debug.Log($"{LOG_TAG} 🧹 All tokens cleared");
    }

    [ContextMenu("Test Manual Image Request")]
    public void TestManualImageRequest()
    {
        Debug.Log($"{LOG_TAG} 🧪 Testing manual image request...");
        StartCoroutine(CheckForNewImage());
    }

    [ContextMenu("Debug System Status")]
    public void DebugSystemStatus()
    {
        Debug.Log($"{LOG_TAG} === SYSTEM STATUS DEBUG ===");
        Debug.Log($"{LOG_TAG} RenderTexture: {(outputRenderTexture != null ? "✅ Assigned" : "❌ Missing")}");
        Debug.Log($"{LOG_TAG} UI Overlay: {(overlayDocument != null ? "✅ Assigned" : "❌ Missing")}");
        Debug.Log($"{LOG_TAG} Current texture: {(currentTexture != null ? "✅ Loaded" : "❌ None")}");
        Debug.Log($"{LOG_TAG} Polling: {(isPolling ? "✅ Active" : "❌ Inactive")}");
        Debug.Log($"{LOG_TAG} Connected: {(isConnected ? "✅ Yes" : "❌ No")}");
        Debug.Log($"{LOG_TAG} === SYSTEM STATUS DEBUG END ===");
    }

    [ContextMenu("Test RenderTexture Copy")]
    public void TestRenderTextureCopy()
    {
        if (outputRenderTexture == null)
        {
            Debug.LogError($"{LOG_TAG} ❌ No RenderTexture assigned!");
            return;
        }

        if (currentTexture == null)
        {
            Debug.LogError($"{LOG_TAG} ❌ No current texture to copy!");
            return;
        }

        CopyTextureToRenderTexture(currentTexture, outputRenderTexture);
        Debug.Log($"{LOG_TAG} ✅ Test copy completed");
    }

    [ContextMenu("Clear RenderTexture")]
    public void TestClearRenderTexture()
    {
        if (outputRenderTexture == null)
        {
            Debug.LogError($"{LOG_TAG} ❌ No RenderTexture assigned!");
            return;
        }

        ClearRenderTexture(outputRenderTexture);
        Debug.Log($"{LOG_TAG} ✅ RenderTexture cleared");
    }
}