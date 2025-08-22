using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityAtoms.BaseAtoms;

public class ConnectionManager : MonoBehaviour
{
    // =========================
    // Logging
    // =========================
    public enum LogLevel { Error = 0, Warning = 1, Info = 2, Verbose = 3 }

    [Header("Logging")]
    [Tooltip("Master switch. If false, no logs (including errors) are emitted from this component.")]
    public bool logsEnabled = true;

    [Tooltip("How chatty logging should be when logs are enabled.")]
    public LogLevel logLevel = LogLevel.Info;

    void Log(LogLevel level, string message)
    {
        if (!logsEnabled) return;
        if (level > logLevel) return;

        switch (level)
        {
            case LogLevel.Error: Debug.LogError(message); break;
            case LogLevel.Warning: Debug.LogWarning(message); break;
            default: Debug.Log(message); break;
        }
    }
    void LogError(string msg) => Log(LogLevel.Error, msg);
    void LogWarn(string msg) => Log(LogLevel.Warning, msg);
    void LogInfo(string msg) => Log(LogLevel.Info, msg);
    void LogVerbose(string msg) => Log(LogLevel.Verbose, msg);

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
            if (appIdVariable != null && !string.IsNullOrEmpty(appIdVariable.Value))
                return appIdVariable.Value;
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
            LogError("UserLoginData ScriptableObject reference is missing!");
            return;
        }

        LogAppIdInfo();
        ValidateServerURL();
        LogLoginState();
        StartLoginStateMonitoring();

        if (autoConnect && IsAuthenticated)
        {
            LogInfo("User already authenticated, connecting...");
            ConnectToServer();
        }
        else
        {
            LogInfo("Please log in first");
        }

        wasLoggedIn = IsAuthenticated;
    }

    void LogAppIdInfo()
    {
        LogVerbose("=== APP ID CONFIGURATION ===");
        if (appIdVariable != null)
        {
            LogVerbose($"Unity Atoms StringVariable found: {appIdVariable.name}");
            LogVerbose($"Value: '{appIdVariable.Value}'");
            LogInfo($"Using App ID: '{AppId}'");
        }
        else
        {
            LogWarn("No Unity Atoms StringVariable assigned for App ID");
            LogInfo($"Using fallback App ID: '{AppId}'");
        }
        LogVerbose("=== END APP ID CONFIG ===");
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
            StopCoroutine(loginCheckCoroutine);

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
                    LogInfo("Login detected, connecting...");
                    if (autoConnect) ConnectToServer();
                }
                else
                {
                    LogInfo("Logout detected, disconnecting...");
                    DisconnectFromServer();
                    LogInfo("Logged out");
                }
                wasLoggedIn = currentlyLoggedIn;
            }

            if (currentlyLoggedIn && loginData.IsSessionExpired(sessionTimeoutMinutes))
            {
                LogWarn("Session expired, logging out...");
                loginData.Logout();
                DisconnectFromServer();
                LogError("Session expired");
            }
        }
    }

    public void ConnectToServer()
    {
        if (!IsAuthenticated)
        {
            LogError("Cannot connect: User not authenticated");
            LogLoginState();
            return;
        }

        LogInfo($"Connecting to server: {serverURL}");
        LogVerbose($"User: {loginData.Username}, Token length: {loginData.AuthToken?.Length ?? 0}");

        shouldPoll = true;
        registrationAttempts = 0;
        StartCoroutine(RegisterDevice());
    }

    IEnumerator RegisterDevice()
    {
        if (isRegistering)
        {
            LogWarn("Already registering device, skipping...");
            yield break;
        }

        if (registrationAttempts >= maxRegistrationAttempts)
        {
            LogError($"Max registration attempts reached ({maxRegistrationAttempts})");
            OnConnectionError("Device registration failed");
            yield break;
        }

        isRegistering = true;
        registrationAttempts++;

        LogInfo($"Registering device (attempt {registrationAttempts}/{maxRegistrationAttempts})...");

        var registrationData = new RegistrationData
        {
            appId = AppId,
            deviceModel = SystemInfo.deviceModel ?? "Unknown",
            os = SystemInfo.operatingSystem ?? "Unknown",
            timestamp = DateTime.UtcNow.ToString("O")
        };

        LogVerbose($"Registration JSON: {JsonUtility.ToJson(registrationData)}");

        yield return StartCoroutine(PostAuthenticatedData("/api/device/register", registrationData, (success, response) =>
        {
            if (success)
            {
                LogInfo("Device registration succeeded");
                registrationAttempts = 0;
                OnConnectionSuccess();
            }
            else
            {
                LogWarn($"Device registration failed: {Shorten(response)}");

                if (!string.IsNullOrEmpty(response) && (response.Contains("401") || response.Contains("Invalid token")))
                {
                    LogError("Authentication issue detected in registration");
                    HandleTokenExpired();
                }
                else
                {
                    OnConnectionError($"Registration failed: {Shorten(response)}");
                }
            }

            isRegistering = false;
        }));
    }

    void OnConnectionSuccess()
    {
        isConnected = true;
        isRegistering = false;
        registrationAttempts = 0;

        loginData.UpdateActivity();
        LogInfo("Connection successful. Starting polling & heartbeat.");

        StartPolling();
        StartHeartbeat();
    }

    void OnConnectionError(string error)
    {
        isConnected = false;
        isRegistering = false;
        LogError($"Error: {error}");

        if (autoConnect && shouldPoll && IsAuthenticated && registrationAttempts < maxRegistrationAttempts)
        {
            LogInfo($"Retrying connection in 5s... (next attempt {registrationAttempts + 1}/{maxRegistrationAttempts})");
            StartCoroutine(ReconnectAfterDelay());
        }
    }

    IEnumerator RetryRegistrationAfterDelay()
    {
        yield return new WaitForSeconds(5f);
        if (shouldPoll && !isConnected && IsAuthenticated && registrationAttempts < maxRegistrationAttempts)
        {
            LogInfo("Retrying device registration...");
            StartCoroutine(RegisterDevice());
        }
    }

    IEnumerator ReconnectAfterDelay()
    {
        yield return new WaitForSeconds(5f);
        if (shouldPoll && !isConnected && IsAuthenticated)
            ConnectToServer();
    }

    void StartPolling()
    {
        if (!isConnected)
        {
            LogWarn("Cannot start polling: not connected/registered");
            return;
        }

        if (pollCoroutine != null)
            StopCoroutine(pollCoroutine);

        LogInfo("Starting command polling...");
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
                LogVerbose($"Polling {pollUrl}");

                yield return StartCoroutine(GetAuthenticatedData(pollUrl, (success, response) =>
                {
                    if (success)
                    {
                        ProcessCommands(response);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(response) && (response.Contains("401") || response.Contains("Invalid token")))
                        {
                            LogError("Auth token issue detected in polling");
                            HandleTokenExpired();
                            return;
                        }

                        if (!string.IsNullOrEmpty(response) && response.Contains("403") && response.Contains("Device not found"))
                        {
                            LogWarn("Device not found during polling. Re-registering...");
                            HandleDeviceNotFound();
                            return;
                        }

                        LogWarn($"Polling failed: {Shorten(response)}");
                        OnConnectionError($"Polling failed: {Shorten(response)}");
                    }
                }));
            }
        }
    }

    void HandleDeviceNotFound()
    {
        LogVerbose("=== HANDLING DEVICE NOT FOUND ===");

        if (isRegistering)
        {
            LogWarn("Already handling device-not-found (registration in progress), skipping...");
            return;
        }

        if (pollCoroutine != null)
        {
            StopCoroutine(pollCoroutine);
            pollCoroutine = null;
        }

        isConnected = false;
        registrationAttempts = 0;
        StartCoroutine(RegisterDevice());
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
                LogInfo($"Processed {response.commands.Length} command(s). LastId={lastCommandId}");
            }
            else
            {
                LogVerbose("No new commands.");
            }
        }
        catch (Exception e)
        {
            LogError($"Failed to process commands: {e.Message}");
        }
    }

    void ProcessCommand(Command command)
    {
        LogInfo($"Command: {command.type}");
        loginData.UpdateActivity();

        switch (command.type?.ToLower())
        {
            case "ping":
                SendCommandResponse(command.id, "pong", "Alive");
                break;

            default:
                LogWarn($"Unknown command: {command.type}");
                SendCommandResponse(command.id, "error", "Unknown command");
                break;
        }
    }

    void SendCommandResponse(int commandId, string status, string message)
    {
        var responseData = new CommandResponseData
        {
            commandId = commandId,
            appId = AppId,
            status = status,
            message = message,
            timestamp = DateTime.UtcNow.ToString("O")
        };

        StartCoroutine(PostAuthenticatedData("/api/device/response", responseData, (success, response) =>
        {
            if (!success)
                LogWarn($"Failed to send command response: {Shorten(response)}");
        }));
    }

    void StartHeartbeat()
    {
        if (heartbeatCoroutine != null)
            StopCoroutine(heartbeatCoroutine);

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
                    appId = AppId,
                    timestamp = DateTime.UtcNow.ToString("O")
                };

                yield return StartCoroutine(PostAuthenticatedData("/api/device/heartbeat", heartbeatData, (success, response) =>
                {
                    if (!success)
                    {
                        LogWarn($"Heartbeat failed: {Shorten(response)}");
                        if (!string.IsNullOrEmpty(response) && (response.Contains("401") || response.Contains("Invalid token")))
                            HandleTokenExpired();
                    }
                    else
                    {
                        loginData.UpdateActivity();
                        LogVerbose("Heartbeat OK");
                    }
                }));
            }
        }
    }

    void HandleTokenExpired()
    {
        LogInfo("Token expired, logging out...");
        loginData.Logout();
        OnConnectionError("Authentication expired - please log in again");
    }

    IEnumerator PostAuthenticatedData(string endpoint, object data, System.Action<bool, string> callback)
    {
        if (!IsAuthenticated)
        {
            LogError("POST failed: Not authenticated");
            LogLoginState();
            callback(false, "Not authenticated");
            yield break;
        }

        string json = JsonUtility.ToJson(data);
        string fullUrl = serverURL + endpoint;

        LogVerbose($"POST {endpoint}");
        LogVerbose($"JSON: {json}");

        using (UnityWebRequest request = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + loginData.AuthToken);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                LogVerbose($"POST {endpoint} OK ({request.responseCode})");
                callback(true, request.downloadHandler.text);
            }
            else
            {
                string errorResponse = $"HTTP/{request.responseCode} {request.error} - {request.downloadHandler.text}";
                LogWarn($"POST {endpoint} failed: {Shorten(errorResponse)}");
                callback(false, errorResponse);
            }
        }
    }

    IEnumerator GetAuthenticatedData(string endpoint, System.Action<bool, string> callback)
    {
        if (!IsAuthenticated)
        {
            LogError("GET failed: Not authenticated");
            LogLoginState();
            callback(false, "Not authenticated");
            yield break;
        }

        string fullUrl = serverURL + endpoint;

        LogVerbose($"GET {endpoint}");

        using (UnityWebRequest request = UnityWebRequest.Get(fullUrl))
        {
            request.SetRequestHeader("Authorization", "Bearer " + loginData.AuthToken);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                LogVerbose($"GET {endpoint} OK ({request.responseCode})");
                callback(true, request.downloadHandler.text);
            }
            else
            {
                string errorResponse = $"HTTP/{request.responseCode} {request.error} - {request.downloadHandler.text}";
                LogWarn($"GET {endpoint} failed: {Shorten(errorResponse)}");
                callback(false, errorResponse);
            }
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

        LogInfo("Disconnected");
    }

    public void Logout()
    {
        DisconnectFromServer();
        if (loginData != null)
            loginData.Logout();

        LogInfo("Logged out");
    }

    void LogLoginState()
    {
        LogVerbose("=== LOGIN STATE DEBUG ===");

        if (loginData == null)
        {
            LogError("LoginData ScriptableObject is NULL!");
            return;
        }

        LogVerbose($"SO: {loginData.name}");
        LogVerbose($"Username: '{loginData.Username}'  Email: '{loginData.UserEmail}'  UserId: {loginData.UserId}");
        LogVerbose($"IsLoggedIn: {loginData.IsLoggedIn}  RememberMe: {loginData.RememberMe}");
        LogVerbose($"AuthTokenLength: {loginData.AuthToken?.Length ?? 0}  TokenValid: {loginData.IsTokenValid()}  SessionExpired: {loginData.IsSessionExpired(sessionTimeoutMinutes)}");

        // Token payload decode moved to Verbose only
        if (!string.IsNullOrEmpty(loginData.AuthToken))
        {
            try
            {
                string[] tokenParts = loginData.AuthToken.Split('.');
                if (tokenParts.Length >= 2)
                {
                    string payload = tokenParts[1];
                    while (payload.Length % 4 != 0) payload += "=";
                    byte[] jsonBytes = Convert.FromBase64String(payload);
                    string jsonString = Encoding.UTF8.GetString(jsonBytes);
                    LogVerbose($"Token Payload: {jsonString}");
                }
            }
            catch (Exception e)
            {
                LogVerbose($"Could not decode token payload: {e.Message}");
            }
        }
        else
        {
            LogVerbose("Auth Token is empty.");
        }

        LogVerbose("=== LOGIN STATE DEBUG END ===");
    }

    [ContextMenu("Debug Connection State")]
    public void DebugConnectionState()
    {
        LogInfo("=== FULL CONNECTION STATE DEBUG ===");
        LogLoginState();
        LogAppIdInfo();

        LogInfo($"Server URL: '{serverURL}'  App ID: '{AppId}'");
        LogInfo($"IsConnected: {isConnected}  ShouldPoll: {shouldPoll}  IsRegistering: {isRegistering}");
        LogInfo($"Registration Attempts: {registrationAttempts}/{maxRegistrationAttempts}  LastCommandId: {lastCommandId}");
        LogInfo($"AutoConnect: {autoConnect}  PollInterval: {pollInterval}  HeartbeatInterval: {heartbeatInterval}");
        LogInfo($"SessionTimeoutMinutes: {sessionTimeoutMinutes}  WasLoggedIn: {wasLoggedIn}");
        LogInfo($"Coroutines -> Poll:{(pollCoroutine != null)} Heartbeat:{(heartbeatCoroutine != null)} LoginCheck:{(loginCheckCoroutine != null)}");
        LogInfo("=== END CONNECTION STATE DEBUG ===");
    }

    [ContextMenu("Force Device Registration")]
    public void ForceDeviceRegistration()
    {
        LogInfo("Manually forcing device registration...");

        if (!IsAuthenticated)
        {
            LogError("Cannot force registration: User not authenticated");
            return;
        }

        isRegistering = false;
        registrationAttempts = 0;
        StartCoroutine(RegisterDevice());
    }

    // Method to change app ID at runtime using Unity Atoms
    public void SetAppId(string newAppId)
    {
        if (appIdVariable != null)
        {
            appIdVariable.Value = newAppId;
            LogInfo($"App ID changed via Unity Atoms to: {newAppId}");
        }
        else
        {
            LogWarn("Cannot set App ID - no Unity Atoms StringVariable assigned");
        }
    }

    [ContextMenu("Toggle Logs")]
    public void ToggleLogs()
    {
        logsEnabled = !logsEnabled;
        Debug.Log($"[ConnectionManager] LogsEnabled set to: {logsEnabled}");
    }

    void OnDestroy()
    {
        DisconnectFromServer();
        if (loginCheckCoroutine != null)
            StopCoroutine(loginCheckCoroutine);
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            shouldPoll = false;
            LogVerbose("App paused: polling stopped");
        }
        else if (autoConnect && IsAuthenticated)
        {
            shouldPoll = true;
            LogVerbose("App resumed: reconnecting");
            ConnectToServer();
        }
    }

    void OnApplicationQuit()
    {
        DisconnectFromServer();
    }

    // Utility to trim long responses in logs
    string Shorten(string s, int max = 240)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= max ? s : s.Substring(0, max) + "…";
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
