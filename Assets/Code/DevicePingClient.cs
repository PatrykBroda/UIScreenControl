using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Collections.Generic;

[System.Serializable]
public class PingHeartbeatData
{
    public string appId;
    public string deviceModel;
    public string os;
    public string timestamp;
    public string status;
}

[System.Serializable]
public class PingMessage
{
    public string type;
    public string appId;
    public string pingId;
    public string message;
    public float responseTime;
}

public class DevicePingClient : MonoBehaviour
{
    [Header("Connection Manager Reference")]
    [Tooltip("Reference to the existing ConnectionManager")]
    public ConnectionManager connectionManager;

    [Header("Ping Settings")]
    [Tooltip("How often to send heartbeats (seconds)")]
    public float heartbeatInterval = 30f;

    [Tooltip("Enable verbose logging for debugging")]
    public bool verboseLogging = true;

    [Tooltip("Show debug GUI overlay")]
    public bool showDebugGUI = true;

    [Header("Status (Read Only)")]
    [SerializeField] private bool isPingSystemActive = false;
    [SerializeField] private bool isDeviceOnline = false;
    [SerializeField] private int heartbeatsSent = 0;
    [SerializeField] private int pingsReceived = 0;

    // Private state
    private string serverURL;
    private Coroutine heartbeatCoroutine;
    private Dictionary<string, float> pendingPings = new Dictionary<string, float>();

    // Events for external systems
    public static event Action<bool> OnDeviceStatusChanged;
    public static event Action<string> OnPingReceived;
    public static event Action<float> OnPongSent;

    void Start()
    {
        LogMessage("🏓 Unity Ping Client starting...");

        // Auto-find ConnectionManager if not assigned
        if (connectionManager == null)
        {
            connectionManager = FindFirstObjectByType<ConnectionManager>();
            if (connectionManager == null)
            {
                LogError("❌ No ConnectionManager found! Please assign one in the inspector.");
                return;
            }
            else
            {
                LogMessage("✅ Auto-found ConnectionManager");
            }
        }

        // Start monitoring ConnectionManager status
        StartCoroutine(MonitorConnectionManager());
    }

    IEnumerator MonitorConnectionManager()
    {
        LogMessage("👀 Monitoring ConnectionManager status...");

        while (true)
        {
            yield return new WaitForSeconds(1f);

            // Check if ConnectionManager is connected and authenticated
            bool cmConnected = IsConnectionManagerReady();

            if (cmConnected && !isPingSystemActive)
            {
                // ConnectionManager is ready, start ping system
                LogMessage("🔗 ConnectionManager is ready, starting ping system...");
                StartPingSystem();
            }
            else if (!cmConnected && isPingSystemActive)
            {
                // ConnectionManager lost connection, stop ping system
                LogMessage("🔗 ConnectionManager lost connection, stopping ping system...");
                StopPingSystem();
            }
        }
    }

    bool IsConnectionManagerReady()
    {
        if (connectionManager == null) return false;

        try
        {
            // Use reflection to check ConnectionManager properties
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
            if (verboseLogging)
            {
                LogMessage($"Could not check ConnectionManager status: {e.Message}");
            }
        }

        return false;
    }

    void StartPingSystem()
    {
        if (isPingSystemActive) return;

        // Get server URL from ConnectionManager
        serverURL = connectionManager.serverURL;
        if (string.IsNullOrEmpty(serverURL))
        {
            LogError("❌ No server URL available from ConnectionManager");
            return;
        }

        // Ensure proper URL format
        if (!serverURL.StartsWith("http://") && !serverURL.StartsWith("https://"))
        {
            serverURL = "https://" + serverURL;
        }
        serverURL = serverURL.TrimEnd('/');

        isPingSystemActive = true;
        isDeviceOnline = true;
        OnDeviceStatusChanged?.Invoke(true);

        LogMessage($"✅ Ping system started - Server: {serverURL}");

        // Start heartbeat system
        StartHeartbeat();
    }

    void StopPingSystem()
    {
        if (!isPingSystemActive) return;

        isPingSystemActive = false;
        isDeviceOnline = false;
        OnDeviceStatusChanged?.Invoke(false);

        // Stop heartbeat
        if (heartbeatCoroutine != null)
        {
            StopCoroutine(heartbeatCoroutine);
            heartbeatCoroutine = null;
        }

        LogMessage("❌ Ping system stopped");
    }

    void StartHeartbeat()
    {
        if (heartbeatCoroutine != null)
        {
            StopCoroutine(heartbeatCoroutine);
        }
        heartbeatCoroutine = StartCoroutine(SendHeartbeats());
    }

    IEnumerator SendHeartbeats()
    {
        LogMessage("💓 Starting heartbeat system...");

        while (isPingSystemActive && IsConnectionManagerReady())
        {
            yield return new WaitForSeconds(heartbeatInterval);

            if (isPingSystemActive)
            {
                yield return StartCoroutine(SendHeartbeat());
            }
        }

        LogMessage("💓 Heartbeat system stopped");
    }

    IEnumerator SendHeartbeat()
    {
        // Get auth token and app ID from ConnectionManager (which now uses Unity Atoms StringVariable)
        string authToken = connectionManager.loginData?.AuthToken;
        string appId = connectionManager.AppId; // This now reads from Unity Atoms StringVariable

        // ENHANCED DEBUG: Show exactly what we got from ConnectionManager
        LogMessage($"🔍 Raw values from ConnectionManager:");
        LogMessage($"   connectionManager.AppId = '{appId}' (null: {appId == null}, empty: {string.IsNullOrEmpty(appId)})");
        LogMessage($"   authToken length = {authToken?.Length ?? 0}");

        // Validate App ID is configured
        if (string.IsNullOrEmpty(appId))
        {
            LogError("❌ App ID is not configured in ConnectionManager! Please drag a Unity Atoms StringVariable asset with a valid value.");
            yield break;
        }

        if (string.IsNullOrEmpty(authToken))
        {
            LogError("❌ Missing auth token from ConnectionManager - heartbeat aborted");
            yield break;
        }

        // Create heartbeat data with explicit debugging
        LogMessage($"💓 Creating heartbeat with appId: '{appId}' (from Unity Atoms StringVariable)");

        var heartbeatData = new PingHeartbeatData
        {
            appId = appId,
            deviceModel = SystemInfo.deviceModel ?? "Unknown",
            os = SystemInfo.operatingSystem ?? "Unknown",
            timestamp = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            status = "online"
        };

        // Verify the object was created correctly
        LogMessage($"💓 Heartbeat object created - appId field: '{heartbeatData.appId}'");

        string jsonData = JsonUtility.ToJson(heartbeatData);
        LogMessage($"💓 Final JSON being sent: {jsonData}");

        // Double-check the JSON contains appId
        if (!jsonData.Contains("appId"))
        {
            LogError("❌ CRITICAL: JSON does not contain 'appId' field!");
        }
        else if (!jsonData.Contains(appId))
        {
            LogError($"❌ CRITICAL: JSON does not contain expected appId value '{appId}'!");
        }

        string heartbeatUrl = $"{serverURL}/api/device/ping/heartbeat";

        using (UnityWebRequest request = new UnityWebRequest(heartbeatUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                heartbeatsSent++;
                if (verboseLogging)
                {
                    LogMessage($"💓 Heartbeat #{heartbeatsSent} sent successfully");
                    LogMessage($"💓 Response: {request.downloadHandler.text}");
                }
            }
            else
            {
                LogError($"💓 Heartbeat failed: {request.error}");
                LogError($"💓 Response Code: {request.responseCode}");
                LogError($"💓 Response: {request.downloadHandler.text}");
            }
        }
    }

    // Method to handle ping requests from the server
    public void HandleServerPing(string pingId)
    {
        LogMessage($"🏓 Received ping: {pingId}");
        pingsReceived++;

        // Record the ping
        pendingPings[pingId] = Time.time;
        OnPingReceived?.Invoke(pingId);

        // Send pong response immediately
        StartCoroutine(SendPongResponse(pingId));
    }

    IEnumerator SendPongResponse(string pingId)
    {
        float startTime = Time.time;

        // Use ConnectionManager's auth token and app ID (from Unity Atoms StringVariable)
        string authToken = connectionManager.loginData?.AuthToken;
        string appId = connectionManager.AppId; // This now reads from Unity Atoms StringVariable

        // Validate App ID is configured
        if (string.IsNullOrEmpty(appId))
        {
            LogError("❌ Cannot send pong - App ID is not configured in ConnectionManager!");
            yield break;
        }

        if (string.IsNullOrEmpty(authToken))
        {
            LogError("❌ Cannot send pong - missing auth token");
            yield break;
        }

        var pongMessage = new PingMessage
        {
            type = "pong",
            appId = appId,
            pingId = pingId,
            responseTime = 0f // Will be calculated
        };

        string pongJson = JsonUtility.ToJson(pongMessage);
        string pongUrl = $"{serverURL}/api/device/ping/pong";

        using (UnityWebRequest request = new UnityWebRequest(pongUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(pongJson);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {authToken}");

            yield return request.SendWebRequest();

            float responseTime = (Time.time - startTime) * 1000f; // Convert to milliseconds

            if (request.result == UnityWebRequest.Result.Success)
            {
                LogMessage($"🏓 Pong sent for {pingId} in {responseTime:F2}ms");
                LogMessage($"🏓 Response: {request.downloadHandler.text}");
                OnPongSent?.Invoke(responseTime);
            }
            else
            {
                LogError($"🏓 Pong failed for {pingId}: {request.error}");
                LogError($"🏓 Response Code: {request.responseCode}");
                LogError($"🏓 Response: {request.downloadHandler.text}");
            }
        }

        // Remove from pending pings
        pendingPings.Remove(pingId);
    }

    // Test methods for debugging
    [ContextMenu("Test Ping Response")]
    public void TestPingResponse()
    {
        if (!isPingSystemActive)
        {
            LogMessage("⚠️ Ping system not active - cannot test ping");
            return;
        }

        string testPingId = "test-ping-" + UnityEngine.Random.Range(1000, 9999);
        HandleServerPing(testPingId);
    }

    [ContextMenu("Test JSON Creation")]
    public void TestJSONCreation()
    {
        LogMessage("🧪 Testing JSON creation manually...");

        string testAppId = connectionManager?.AppId ?? "test-app-id";
        LogMessage($"🧪 Using App ID: '{testAppId}' for test");

        var testData = new PingHeartbeatData
        {
            appId = testAppId,
            deviceModel = "Test Device",
            os = "Test OS",
            timestamp = "2024-01-01T00:00:00.000Z",
            status = "online"
        };

        string testJson = JsonUtility.ToJson(testData);
        LogMessage($"🧪 Test JSON: {testJson}");

        if (testJson.Contains("appId"))
        {
            LogMessage("✅ Test JSON contains 'appId' field");
        }
        else
        {
            LogError("❌ Test JSON missing 'appId' field!");
        }

        if (testJson.Contains(testAppId))
        {
            LogMessage("✅ Test JSON contains correct appId value");
        }
        else
        {
            LogError("❌ Test JSON missing appId value!");
        }
    }

    [ContextMenu("Force Heartbeat")]
    public void ForceHeartbeat()
    {
        if (isPingSystemActive)
        {
            StartCoroutine(SendHeartbeat());
        }
        else
        {
            LogMessage("⚠️ Ping system not active - cannot send heartbeat");
        }
    }

    [ContextMenu("Debug Status")]
    public void DebugStatus()
    {
        LogMessage("=== PING CLIENT STATUS ===");
        LogMessage($"ConnectionManager: {(connectionManager != null ? "✅ Found" : "❌ Missing")}");
        LogMessage($"CM Connected: {IsConnectionManagerReady()}");
        LogMessage($"Ping System Active: {isPingSystemActive}");
        LogMessage($"Device Online: {isDeviceOnline}");
        LogMessage($"Server URL: {serverURL}");
        LogMessage($"AppId from CM (Unity Atoms): '{connectionManager?.AppId}'"); // Now from Unity Atoms StringVariable
        LogMessage($"Heartbeats Sent: {heartbeatsSent}");
        LogMessage($"Pings Received: {pingsReceived}");
        LogMessage($"Pending Pings: {pendingPings.Count}");
        LogMessage("=== END STATUS ===");
    }

    // Utility methods
    void LogMessage(string message)
    {
        if (verboseLogging)
        {
            Debug.Log($"[PING] {message}");
        }
    }

    void LogError(string message)
    {
        Debug.LogError($"[PING] {message}");
    }

    void OnDestroy()
    {
        StopPingSystem();
        LogMessage("🔌 Ping client shutting down...");
    }

    // Debug GUI
    void OnGUI()
    {
        if (!showDebugGUI) return;

        GUILayout.BeginArea(new Rect(10, 270, 350, 200));

        GUILayout.Label("=== PING CLIENT STATUS ===");
        GUILayout.Label($"ConnectionManager: {(connectionManager != null ? "✅" : "❌")}");
        GUILayout.Label($"CM Ready: {(IsConnectionManagerReady() ? "✅" : "❌")}");
        GUILayout.Label($"Ping System: {(isPingSystemActive ? "✅ Active" : "❌ Inactive")}");
        GUILayout.Label($"Device Status: {(isDeviceOnline ? "🟢 Online" : "🔴 Offline")}");
        GUILayout.Label($"AppId: '{connectionManager?.AppId}'"); // Now from Unity Atoms StringVariable
        GUILayout.Label($"Heartbeats: {heartbeatsSent} | Pings: {pingsReceived}");

        GUILayout.Space(10);

        if (GUILayout.Button("Force Heartbeat"))
        {
            ForceHeartbeat();
        }

        if (GUILayout.Button("Test Ping Response"))
        {
            TestPingResponse();
        }

        if (GUILayout.Button("Debug Status"))
        {
            DebugStatus();
        }

        GUILayout.EndArea();
    }
}