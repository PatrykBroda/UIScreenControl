using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityAtoms.BaseAtoms;

public class ConnectionManager : MonoBehaviour
{
    [Header("Connection Settings")]
    public string serverURL = "https://unity-server-control-patrykbroda.replit.app";

    [Header("App Configuration")]
    [Tooltip("Unity Atoms StringVariable for App ID")]
    public StringVariable appIdVariable;

    [SerializeField] private string fallbackAppId = "unity-app-001"; // Fallback if no atoms variable

    public string AppId
    {
        get
        {
            // Priority: Unity Atoms StringVariable -> Fallback
            if (appIdVariable != null && !string.IsNullOrEmpty(appIdVariable.Value))
            {
                return appIdVariable.Value;
            }

            return fallbackAppId;
        }
    }

    [Header("References")]
    public UserLoginData loginData;

    [Header("Polling Behavior")]
    public float pollInterval = 2f;
    public float heartbeatInterval = 30f;
    public bool autoConnect = true;

    [Header("Session Management")]
    public int sessionTimeoutMinutes = 30;
    public float loginCheckInterval = 5f;

    private bool isConnected = false;
    private bool shouldPoll = true;
    private Coroutine pollCoroutine;
    private Coroutine heartbeatCoroutine;
    private Coroutine loginCheckCoroutine;
    private int lastCommandId = 0;
    private bool wasLoggedIn = false;
    private bool isRegistering = false;
    private int registrationAttempts = 0;
    private int maxRegistrationAttempts = 3;

    public bool IsConnected => isConnected;
    public bool IsAuthenticated => loginData != null && loginData.IsLoggedIn && loginData.IsTokenValid() && !loginData.IsSessionExpired(sessionTimeoutMinutes);

    void Start()
    {
        if (loginData == null)
        {
            Debug.LogError("UserLoginData ScriptableObject reference is missing!");
            return;
        }

        // Log app ID source and value
        LogAppIdInfo();

        ValidateServerURL();
        LogLoginState();

        StartLoginStateMonitoring();

        if (autoConnect && IsAuthenticated)
        {
            Debug.Log("User already authenticated, connecting...");
            ConnectToServer();
        }
        else
        {
            Debug.Log("Please log in first");
        }

        wasLoggedIn = IsAuthenticated;
    }

    void LogAppIdInfo()
    {
        Debug.Log($"=== APP ID CONFIGURATION ===");
        if (appIdVariable != null)
        {
            Debug.Log($"✅ Unity Atoms StringVariable found: {appIdVariable.name}");
            Debug.Log($"   Value: '{appIdVariable.Value}'");
            Debug.Log($"   Using App ID from Atoms: '{AppId}'");
        }
        else
        {
            Debug.LogWarning("⚠️ No Unity Atoms StringVariable assigned for App ID");
            Debug.Log($"   Using fallback App ID: '{AppId}'");
        }
        Debug.Log($"=== END APP ID CONFIG ===");
    }

    void ValidateServerURL()
    {
        if (!serverURL.StartsWith("https://") && !serverURL.StartsWith("http://"))
        {
            serverURL = "https://" + serverURL;
        }
        serverURL = serverURL.TrimEnd('/');
    }

    void StartLoginStateMonitoring()
    {
        if (loginCheckCoroutine != null)
        {
            StopCoroutine(loginCheckCoroutine);
        }
        loginCheckCoroutine = StartCoroutine(MonitorLoginState());
    }

    IEnumerator MonitorLoginState()
    {
        while (Application.isPlaying)
        {
            yield return new WaitForSeconds(loginCheckInterval);

            bool currentlyLoggedIn = IsAuthenticated;

            if (currentlyLoggedIn != wasLoggedIn)
            {
                if (currentlyLoggedIn)
                {
                    Debug.Log("User login detected, connecting...");
                    if (autoConnect)
                    {
                        ConnectToServer();
                    }
                }
                else
                {
                    Debug.Log("User logout detected, disconnecting...");
                    DisconnectFromServer();
                    Debug.Log("Logged out");
                }
                wasLoggedIn = currentlyLoggedIn;
            }

            if (currentlyLoggedIn && loginData.IsSessionExpired(sessionTimeoutMinutes))
            {
                Debug.Log("Session expired, logging out...");
                loginData.Logout();
                DisconnectFromServer();
                Debug.LogError("Session expired");
            }
        }
    }

    public void ConnectToServer()
    {
        if (!IsAuthenticated)
        {
            Debug.LogError("Cannot connect: User not authenticated");
            LogLoginState();
            return;
        }

        Debug.Log($"Connecting to server: {serverURL}");
        Debug.Log($"User: {loginData.Username}, Token length: {loginData.AuthToken?.Length ?? 0}");

        Debug.Log("Connecting...");
        shouldPoll = true;
        registrationAttempts = 0;
        StartCoroutine(RegisterDevice());
    }

    IEnumerator RegisterDevice()
    {
        if (isRegistering)
        {
            Debug.LogWarning("Already registering device, skipping...");
            yield break;
        }

        if (registrationAttempts >= maxRegistrationAttempts)
        {
            Debug.LogError($"Max registration attempts reached ({maxRegistrationAttempts})");
            OnConnectionError("Device registration failed");
            yield break;
        }

        isRegistering = true;
        registrationAttempts++;

        Debug.Log($"=== DEVICE REGISTRATION START (Attempt {registrationAttempts}) ===");
        Debug.Log($"AppId: '{AppId}'");
        Debug.Log($"Server URL: '{serverURL}'");
        Debug.Log($"Full Registration URL: '{serverURL}/api/device/register'");
        Debug.Log($"User Email: '{loginData.UserEmail}'");
        Debug.Log($"Username: '{loginData.Username}'");
        Debug.Log($"User ID: {loginData.UserId}");
        Debug.Log($"Token Length: {loginData.AuthToken?.Length ?? 0}");
        Debug.Log($"Token Preview: '{loginData.AuthToken?.Substring(0, Math.Min(50, loginData.AuthToken?.Length ?? 0))}...'");
        Debug.Log($"Is Authenticated: {IsAuthenticated}");

        var registrationData = new RegistrationData
        {
            appId = AppId, // Using the property that reads from Unity Atoms
            deviceModel = SystemInfo.deviceModel ?? "Unknown",
            os = SystemInfo.operatingSystem ?? "Unknown",
            timestamp = DateTime.UtcNow.ToString("O")
        };

        string registrationJson = JsonUtility.ToJson(registrationData);
        Debug.Log($"Registration Data JSON: {registrationJson}");

        yield return StartCoroutine(PostAuthenticatedData("/api/device/register", registrationData, (success, response) => {
            Debug.Log($"=== DEVICE REGISTRATION RESPONSE ===");
            Debug.Log($"Success: {success}");
            Debug.Log($"Response: '{response}'");
            Debug.Log($"Response Length: {response?.Length ?? 0}");

            if (success)
            {
                Debug.Log("✅ Device registration SUCCEEDED!");
                Debug.Log($"Registration attempt {registrationAttempts} completed successfully");
                registrationAttempts = 0;
                OnConnectionSuccess();
            }
            else
            {
                Debug.LogError($"❌ Device registration FAILED!");
                Debug.LogError($"Failure on attempt {registrationAttempts}/{maxRegistrationAttempts}");
                Debug.LogError($"Error details: {response}");

                if (response.Contains("401") || response.Contains("Invalid token"))
                {
                    Debug.LogError("Authentication issue detected in registration");
                    HandleTokenExpired();
                }
                else
                {
                    Debug.LogError("Non-auth registration failure");
                    OnConnectionError($"Registration failed: {response}");
                }
            }

            isRegistering = false;
            Debug.Log($"=== DEVICE REGISTRATION END ===");
        }));
    }

    void OnConnectionSuccess()
    {
        isConnected = true;
        isRegistering = false;
        registrationAttempts = 0;

        loginData.UpdateActivity();
        Debug.Log("Connected");

        Debug.Log("✅ Connection successful! Starting polling...");
        StartPolling();
        StartHeartbeat();
    }

    void OnConnectionError(string error)
    {
        isConnected = false;
        isRegistering = false;
        Debug.LogError($"Error: {error}");

        if (autoConnect && shouldPoll && IsAuthenticated && registrationAttempts < maxRegistrationAttempts)
        {
            Debug.Log($"Retrying connection in 5 seconds... (attempt {registrationAttempts + 1}/{maxRegistrationAttempts})");
            StartCoroutine(ReconnectAfterDelay());
        }
    }

    IEnumerator RetryRegistrationAfterDelay()
    {
        yield return new WaitForSeconds(5f);
        if (shouldPoll && !isConnected && IsAuthenticated && registrationAttempts < maxRegistrationAttempts)
        {
            Debug.Log("Retrying device registration...");
            StartCoroutine(RegisterDevice());
        }
    }

    IEnumerator ReconnectAfterDelay()
    {
        yield return new WaitForSeconds(5f);
        if (shouldPoll && !isConnected && IsAuthenticated)
        {
            ConnectToServer();
        }
    }

    void StartPolling()
    {
        if (!isConnected)
        {
            Debug.LogWarning("Cannot start polling: Device not connected/registered");
            return;
        }

        if (pollCoroutine != null)
        {
            StopCoroutine(pollCoroutine);
        }

        Debug.Log("Starting command polling...");
        pollCoroutine = StartCoroutine(PollForCommands());
    }

    IEnumerator PollForCommands()
    {
        while (shouldPoll && Application.isPlaying)
        {
            yield return new WaitForSeconds(pollInterval);

            if (shouldPoll && IsAuthenticated)
            {
                loginData.UpdateActivity();

                string pollUrl = $"/api/device/{AppId}/commands?lastId={lastCommandId}";
                string fullPollUrl = serverURL + pollUrl;

                Debug.Log($"=== POLLING FOR COMMANDS ===");
                Debug.Log($"AppId: '{AppId}'");
                Debug.Log($"Poll URL: '{pollUrl}'");
                Debug.Log($"Full Poll URL: '{fullPollUrl}'");
                Debug.Log($"Last Command ID: {lastCommandId}");
                Debug.Log($"User Email: '{loginData.UserEmail}'");
                Debug.Log($"User ID: {loginData.UserId}");
                Debug.Log($"Token Length: {loginData.AuthToken?.Length ?? 0}");
                Debug.Log($"Token Preview: '{loginData.AuthToken?.Substring(0, Math.Min(50, loginData.AuthToken?.Length ?? 0))}...'");

                yield return StartCoroutine(GetAuthenticatedData(pollUrl, (success, response) => {
                    Debug.Log($"=== POLLING RESPONSE ===");
                    Debug.Log($"Success: {success}");
                    Debug.Log($"Response: '{response}'");
                    Debug.Log($"Response Length: {response?.Length ?? 0}");

                    if (success)
                    {
                        Debug.Log("✅ Polling SUCCEEDED!");
                        ProcessCommands(response);
                    }
                    else
                    {
                        Debug.LogWarning($"❌ Polling FAILED: {response}");

                        if (response.Contains("401") || response.Contains("Invalid token"))
                        {
                            Debug.LogError("Authentication token issue detected in polling");
                            HandleTokenExpired();
                            return;
                        }

                        if (response.Contains("403") && response.Contains("Device not found"))
                        {
                            Debug.LogWarning("Device not found error detected in polling");
                            HandleDeviceNotFound();
                            return;
                        }

                        Debug.LogError("Other polling error detected");
                        OnConnectionError($"Polling failed: {response}");
                    }
                    Debug.Log($"=== POLLING END ===");
                }));
            }
        }
    }

    void HandleDeviceNotFound()
    {
        Debug.Log($"=== HANDLING DEVICE NOT FOUND ===");
        Debug.Log($"Current state - isRegistering: {isRegistering}, registrationAttempts: {registrationAttempts}");

        if (isRegistering)
        {
            Debug.LogWarning("Already handling device not found (registration in progress), skipping...");
            return;
        }

        Debug.Log("Device not found on server, attempting re-registration...");
        Debug.Log($"AppId being used: '{AppId}'");
        Debug.Log($"User info: {loginData.Username} (ID: {loginData.UserId})");

        if (pollCoroutine != null)
        {
            Debug.Log("Stopping current polling coroutine...");
            StopCoroutine(pollCoroutine);
            pollCoroutine = null;
        }

        isConnected = false;
        Debug.Log("Re-registering device...");

        Debug.Log($"Resetting registration attempts (was: {registrationAttempts})");
        registrationAttempts = 0;

        Debug.Log("Starting device registration process...");
        StartCoroutine(RegisterDevice());
        Debug.Log($"=== DEVICE NOT FOUND HANDLING END ===");
    }

    void ProcessCommands(string jsonResponse)
    {
        try
        {
            var response = JsonUtility.FromJson<CommandResponse>(jsonResponse);

            if (response.commands != null && response.commands.Length > 0)
            {
                foreach (var command in response.commands)
                {
                    ProcessCommand(command);
                    lastCommandId = Math.Max(lastCommandId, command.id);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to process commands: {e.Message}");
        }
    }

    void ProcessCommand(Command command)
    {
        Debug.Log($"Processing command: {command.type} - {command.data}");
        loginData.UpdateActivity();

        switch (command.type?.ToLower())
        {
            case "ping":
                SendCommandResponse(command.id, "pong", "Alive");
                break;

            default:
                Debug.LogWarning($"Unknown command: {command.type}");
                SendCommandResponse(command.id, "error", "Unknown command");
                break;
        }
    }

    void SendCommandResponse(int commandId, string status, string message)
    {
        var responseData = new CommandResponseData
        {
            commandId = commandId,
            appId = AppId, // Using the property that reads from Unity Atoms
            status = status,
            message = message,
            timestamp = DateTime.UtcNow.ToString("O")
        };

        StartCoroutine(PostAuthenticatedData("/api/device/response", responseData, (success, response) => {
            if (!success)
            {
                Debug.LogWarning($"Failed to send command response: {response}");
            }
        }));
    }

    void StartHeartbeat()
    {
        if (heartbeatCoroutine != null)
        {
            StopCoroutine(heartbeatCoroutine);
        }
        heartbeatCoroutine = StartCoroutine(HeartbeatCoroutine());
    }

    IEnumerator HeartbeatCoroutine()
    {
        while (isConnected && shouldPoll)
        {
            yield return new WaitForSeconds(heartbeatInterval);

            if (isConnected && shouldPoll && IsAuthenticated)
            {
                var heartbeatData = new HeartbeatData
                {
                    appId = AppId, // Using the property that reads from Unity Atoms
                    timestamp = DateTime.UtcNow.ToString("O")
                };

                yield return StartCoroutine(PostAuthenticatedData("/api/device/heartbeat", heartbeatData, (success, response) => {
                    if (!success)
                    {
                        Debug.LogWarning($"Heartbeat failed: {response}");
                        if (response.Contains("401") || response.Contains("Invalid token"))
                        {
                            HandleTokenExpired();
                        }
                    }
                    else
                    {
                        loginData.UpdateActivity();
                    }
                }));
            }
        }
    }

    void HandleTokenExpired()
    {
        Debug.Log("Token expired, logging out...");
        loginData.Logout();
        OnConnectionError("Authentication expired - please log in again");
    }

    IEnumerator PostAuthenticatedData(string endpoint, object data, System.Action<bool, string> callback)
    {
        if (!IsAuthenticated)
        {
            Debug.LogError("POST REQUEST FAILED: Not authenticated");
            LogLoginState();
            callback(false, "Not authenticated");
            yield break;
        }

        string json = JsonUtility.ToJson(data);
        string fullUrl = serverURL + endpoint;

        Debug.Log($"=== POST REQUEST START ===");
        Debug.Log($"Endpoint: '{endpoint}'");
        Debug.Log($"Full URL: '{fullUrl}'");
        Debug.Log($"JSON Data: '{json}'");
        Debug.Log($"Token Length: {loginData.AuthToken?.Length ?? 0}");
        Debug.Log($"Token Preview: '{loginData.AuthToken?.Substring(0, Math.Min(50, loginData.AuthToken?.Length ?? 0))}...'");

        using (UnityWebRequest request = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + loginData.AuthToken);

            Debug.Log($"Request Headers Set:");
            Debug.Log($"  Content-Type: application/json");
            Debug.Log($"  Authorization: Bearer {loginData.AuthToken?.Substring(0, Math.Min(50, loginData.AuthToken?.Length ?? 0))}...");

            Debug.Log($"Sending POST request to: {fullUrl}");
            yield return request.SendWebRequest();

            Debug.Log($"=== POST REQUEST RESPONSE ===");
            Debug.Log($"Response Code: {request.responseCode}");
            Debug.Log($"Response Text: '{request.downloadHandler.text}'");
            Debug.Log($"Request Error: '{request.error}'");
            Debug.Log($"Request Result: {request.result}");

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("✅ POST Request SUCCESS");
                callback(true, request.downloadHandler.text);
            }
            else
            {
                Debug.LogError("❌ POST Request FAILED");
                string errorResponse = $"HTTP/{request.responseCode} {request.error} - {request.downloadHandler.text}";
                Debug.LogError($"Error Response: {errorResponse}");
                callback(false, errorResponse);
            }
            Debug.Log($"=== POST REQUEST END ===");
        }
    }

    IEnumerator GetAuthenticatedData(string endpoint, System.Action<bool, string> callback)
    {
        if (!IsAuthenticated)
        {
            Debug.LogError("GET REQUEST FAILED: Not authenticated");
            LogLoginState();
            callback(false, "Not authenticated");
            yield break;
        }

        string fullUrl = serverURL + endpoint;

        Debug.Log($"=== GET REQUEST START ===");
        Debug.Log($"Endpoint: '{endpoint}'");
        Debug.Log($"Full URL: '{fullUrl}'");
        Debug.Log($"Token Length: {loginData.AuthToken?.Length ?? 0}");
        Debug.Log($"Token Preview: '{loginData.AuthToken?.Substring(0, Math.Min(50, loginData.AuthToken?.Length ?? 0))}...'");

        using (UnityWebRequest request = UnityWebRequest.Get(fullUrl))
        {
            request.SetRequestHeader("Authorization", "Bearer " + loginData.AuthToken);

            Debug.Log($"Request Headers Set:");
            Debug.Log($"  Authorization: Bearer {loginData.AuthToken?.Substring(0, Math.Min(50, loginData.AuthToken?.Length ?? 0))}...");

            Debug.Log($"Sending GET request to: {fullUrl}");
            yield return request.SendWebRequest();

            Debug.Log($"=== GET REQUEST RESPONSE ===");
            Debug.Log($"Response Code: {request.responseCode}");
            Debug.Log($"Response Text: '{request.downloadHandler.text}'");
            Debug.Log($"Request Error: '{request.error}'");
            Debug.Log($"Request Result: {request.result}");

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("✅ GET Request SUCCESS");
                callback(true, request.downloadHandler.text);
            }
            else
            {
                Debug.LogError("❌ GET Request FAILED");
                string errorResponse = $"HTTP/{request.responseCode} {request.error} - {request.downloadHandler.text}";
                Debug.LogError($"Error Response: {errorResponse}");
                callback(false, errorResponse);
            }
            Debug.Log($"=== GET REQUEST END ===");
        }
    }

    public void DisconnectFromServer()
    {
        shouldPoll = false;
        isConnected = false;
        isRegistering = false;

        if (pollCoroutine != null)
        {
            StopCoroutine(pollCoroutine);
            pollCoroutine = null;
        }

        if (heartbeatCoroutine != null)
        {
            StopCoroutine(heartbeatCoroutine);
            heartbeatCoroutine = null;
        }

        Debug.Log("Disconnected");
    }

    public void Logout()
    {
        DisconnectFromServer();
        if (loginData != null)
        {
            loginData.Logout();
        }
        Debug.Log("Logged out");
    }

    void LogLoginState()
    {
        Debug.Log($"=== LOGIN STATE DEBUG ===");
        if (loginData == null)
        {
            Debug.LogError("❌ LoginData ScriptableObject is NULL!");
            return;
        }

        Debug.Log($"ScriptableObject Reference: {loginData.name}");
        Debug.Log($"Username: '{loginData.Username}'");
        Debug.Log($"Email: '{loginData.UserEmail}'");
        Debug.Log($"User ID: {loginData.UserId}");
        Debug.Log($"Display Name: '{loginData.DisplayName}'");
        Debug.Log($"Is Logged In: {loginData.IsLoggedIn}");
        Debug.Log($"Remember Me: {loginData.RememberMe}");
        Debug.Log($"Login Time: {loginData.LoginTime}");
        Debug.Log($"Last Activity: {loginData.LastActivity}");
        Debug.Log($"Auth Token Length: {loginData.AuthToken?.Length ?? 0}");
        Debug.Log($"Auth Token Valid: {loginData.IsTokenValid()}");
        Debug.Log($"Session Expired: {loginData.IsSessionExpired(sessionTimeoutMinutes)}");
        Debug.Log($"Overall IsAuthenticated: {IsAuthenticated}");

        if (!string.IsNullOrEmpty(loginData.AuthToken))
        {
            Debug.Log($"Token Preview: '{loginData.AuthToken.Substring(0, Math.Min(100, loginData.AuthToken.Length))}...'");

            try
            {
                string[] tokenParts = loginData.AuthToken.Split('.');
                if (tokenParts.Length >= 2)
                {
                    string payload = tokenParts[1];
                    while (payload.Length % 4 != 0)
                    {
                        payload += "=";
                    }

                    byte[] jsonBytes = System.Convert.FromBase64String(payload);
                    string jsonString = System.Text.Encoding.UTF8.GetString(jsonBytes);
                    Debug.Log($"Token Payload: {jsonString}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not decode token payload: {e.Message}");
            }
        }
        else
        {
            Debug.LogError("❌ Auth Token is NULL or EMPTY!");
        }
        Debug.Log($"=== LOGIN STATE DEBUG END ===");
    }

    [ContextMenu("Debug Connection State")]
    public void DebugConnectionState()
    {
        Debug.Log($"=======================================");
        Debug.Log($"FULL CONNECTION STATE DEBUG");
        Debug.Log($"=======================================");

        LogLoginState();
        LogAppIdInfo();

        Debug.Log($"=== CONNECTION MANAGER STATE ===");
        Debug.Log($"Server URL: '{serverURL}'");
        Debug.Log($"App ID: '{AppId}'");
        Debug.Log($"Is Connected: {isConnected}");
        Debug.Log($"Should Poll: {shouldPoll}");
        Debug.Log($"Is Registering: {isRegistering}");
        Debug.Log($"Registration Attempts: {registrationAttempts}");
        Debug.Log($"Max Registration Attempts: {maxRegistrationAttempts}");
        Debug.Log($"Last Command ID: {lastCommandId}");
        Debug.Log($"Auto Connect: {autoConnect}");
        Debug.Log($"Poll Interval: {pollInterval}");
        Debug.Log($"Heartbeat Interval: {heartbeatInterval}");
        Debug.Log($"Session Timeout Minutes: {sessionTimeoutMinutes}");
        Debug.Log($"Was Logged In: {wasLoggedIn}");

        Debug.Log($"=== COROUTINE STATE ===");
        Debug.Log($"Poll Coroutine Active: {pollCoroutine != null}");
        Debug.Log($"Heartbeat Coroutine Active: {heartbeatCoroutine != null}");
        Debug.Log($"Login Check Coroutine Active: {loginCheckCoroutine != null}");

        Debug.Log($"=======================================");
        Debug.Log($"FULL CONNECTION STATE DEBUG END");
        Debug.Log($"=======================================");
    }

    [ContextMenu("Force Device Registration")]
    public void ForceDeviceRegistration()
    {
        Debug.Log("🔧 MANUALLY FORCING DEVICE REGISTRATION");
        DebugConnectionState();

        if (!IsAuthenticated)
        {
            Debug.LogError("Cannot force registration: User not authenticated");
            return;
        }

        isRegistering = false;
        registrationAttempts = 0;

        Debug.Log("Manual registration...");
        StartCoroutine(RegisterDevice());
    }

    // Method to change app ID at runtime using Unity Atoms
    public void SetAppId(string newAppId)
    {
        if (appIdVariable != null)
        {
            appIdVariable.Value = newAppId;
            Debug.Log($"App ID changed via Unity Atoms to: {newAppId}");
        }
        else
        {
            Debug.LogWarning("Cannot set App ID - no Unity Atoms StringVariable assigned");
        }
    }

    void OnDestroy()
    {
        DisconnectFromServer();
        if (loginCheckCoroutine != null)
        {
            StopCoroutine(loginCheckCoroutine);
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            shouldPoll = false;
        }
        else if (autoConnect && IsAuthenticated)
        {
            shouldPoll = true;
            ConnectToServer();
        }
    }

    void OnApplicationQuit()
    {
        DisconnectFromServer();
    }
}

// =========================
// Data Classes
// =========================

[System.Serializable]
public class RegistrationData
{
    public string appId;
    public string deviceModel;
    public string os;
    public string timestamp;
}

[System.Serializable]
public class HeartbeatData
{
    public string appId;
    public string timestamp;
}

[System.Serializable]
public class CommandResponseData
{
    public int commandId;
    public string appId;
    public string status;
    public string message;
    public string timestamp;
}

[System.Serializable]
public class Command
{
    public int id;
    public string type;
    public string data;
    public string timestamp;
}

[System.Serializable]
public class CommandResponse
{
    public Command[] commands;
    public int lastId;
    public string status;
}