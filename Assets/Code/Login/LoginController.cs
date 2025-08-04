using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class LoginController : MonoBehaviour
{
    [Header("API Configuration")]
    [SerializeField] private string baseUrl = "https://unity-server-control-patrykbroda.replit.app";
    [SerializeField] private string loginEndpoint = "/api/auth/unity/login";
    [SerializeField] private string registerEndpoint = "/api/auth/unity/register";
    [SerializeField] private string validateEndpoint = "/api/auth/unity/me";

    [Header("Data Storage")]
    [SerializeField] private UserLoginData loginData;

    [Header("Scene Management")]
    [SerializeField] private string mainSceneName = "MainSceneIMage";
    [SerializeField] private bool autoLoadSceneOnLogin = true;

    private UIDocument uiDocument;
    private VisualElement root;

    private VisualElement loginPanel;
    private TextField usernameField;
    private TextField passwordField;
    private Toggle rememberToggle;
    private Button loginButton;
    private Label forgotPasswordLink;
    private Label signupLink;
    private VisualElement errorMessage;
    private Label errorText;

    public event Action<string> OnLoginSuccess;
    public event Action<string> OnLoginFailed;

    void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("UIDocument component not found!");
            return;
        }

        root = uiDocument.rootVisualElement;
        loginPanel = root.Q<VisualElement>("login-panel");

        // Hide UI until auto login completes
        if (loginPanel != null)
            loginPanel.style.display = DisplayStyle.None;

        AutoLogin();
    }

    void Start()
    {
        if (loginData == null)
        {
            Debug.LogWarning("UserLoginData ScriptableObject not assigned! Create one using Assets > Create > Authentication > User Login Data");
        }

        InitializeUI();
        SetupEventHandlers();

        // Show login panel if not auto logging in
        if (loginPanel != null && loginPanel.style.display != DisplayStyle.Flex)
            loginPanel.style.display = DisplayStyle.Flex;
    }

    void InitializeUI()
    {
        usernameField = root.Q<TextField>("username-field");
        passwordField = root.Q<TextField>("password-field");
        rememberToggle = root.Q<Toggle>("remember-toggle");
        loginButton = root.Q<Button>("login-button");
        forgotPasswordLink = root.Q<Label>("forgot-password-link");
        signupLink = root.Q<Label>("signup-link");
        errorMessage = root.Q<VisualElement>("error-message");
        errorText = root.Q<Label>("error-text");

        if (usernameField == null || passwordField == null || loginButton == null)
        {
            Debug.LogError("Required UI elements not found! Check element names in UXML.");
        }
    }

    void SetupEventHandlers()
    {
        if (loginButton != null)
            loginButton.clicked += OnLoginButtonClicked;

        if (forgotPasswordLink != null)
            forgotPasswordLink.RegisterCallback<ClickEvent>(OnForgotPasswordClicked);

        if (signupLink != null)
            signupLink.RegisterCallback<ClickEvent>(OnSignupClicked);

        if (usernameField != null)
            usernameField.RegisterCallback<KeyDownEvent>(OnFieldKeyDown);

        if (passwordField != null)
            passwordField.RegisterCallback<KeyDownEvent>(OnFieldKeyDown);
    }

    // AUTO LOGIN LOGIC
    void AutoLogin()
    {
        string token = PlayerPrefs.GetString("auth_token", "");
        string rememberedUsername = PlayerPrefs.GetString("remembered_username", "");
        bool hasRememberMe = !string.IsNullOrEmpty(rememberedUsername);

        if (hasRememberMe && !string.IsNullOrEmpty(token))
        {
            // Hide UI while attempting auto login
            if (loginPanel != null)
                loginPanel.style.display = DisplayStyle.None;

            StartCoroutine(ValidateTokenAndLogin(token));
        }
        else
        {
            // Show login form if auto login not possible
            if (loginPanel != null)
                loginPanel.style.display = DisplayStyle.Flex;
        }
    }

    IEnumerator ValidateTokenAndLogin(string token)
    {
        SetLoadingState(true);
        string fullUrl = baseUrl + validateEndpoint;
        using (UnityWebRequest request = UnityWebRequest.Get(fullUrl))
        {
            request.SetRequestHeader("Authorization", "Bearer " + token);

            yield return request.SendWebRequest();

            SetLoadingState(false);

            if (request.result == UnityWebRequest.Result.Success)
            {
                TokenValidationResponse response = JsonUtility.FromJson<TokenValidationResponse>(request.downloadHandler.text);

                if (response.success && response.user != null)
                {
                    // Restore login data and proceed
                    if (loginData != null)
                    {
                        loginData.SetAuthToken(token);
                        loginData.SetUserAccountData(response.user);
                        loginData.SetLoginCredentials(response.user.email, response.user.email, true);
                    }
                    Debug.Log("Auto login successful!");
                    OnLoginSuccess?.Invoke(token);

                    if (autoLoadSceneOnLogin)
                    {
                        LoadMainScene();
                    }
                    yield break; // Stop further execution
                }
            }

            // If validation fails, clear stored token/username and show login form
            PlayerPrefs.DeleteKey("auth_token");
            PlayerPrefs.DeleteKey("remembered_username");
            PlayerPrefs.Save();

            Debug.Log("Auto login failed, returning to login screen.");

            // Show login form
            if (loginPanel != null)
                loginPanel.style.display = DisplayStyle.Flex;
        }
    }

    void OnFieldKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            OnLoginButtonClicked();
        }

        if (IsErrorVisible())
        {
            HideError();
        }
    }

    void OnLoginButtonClicked()
    {
        if (!ValidateInputs())
            return;

        string username = usernameField.value.Trim();
        string password = passwordField.value;
        bool rememberMe = rememberToggle?.value ?? false;

        StartCoroutine(LoginCoroutine(username, password, rememberMe));
    }

    bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(usernameField.value))
        {
            ShowError("Please enter your email address.");
            usernameField.Focus();
            return false;
        }

        if (!IsValidEmail(usernameField.value.Trim()))
        {
            ShowError("Please enter a valid email address.");
            usernameField.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(passwordField.value))
        {
            ShowError("Please enter your password.");
            passwordField.Focus();
            return false;
        }

        if (passwordField.value.Length < 6)
        {
            ShowError("Password must be at least 6 characters long.");
            passwordField.Focus();
            return false;
        }

        return true;
    }

    bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    IEnumerator LoginCoroutine(string username, string password, bool rememberMe)
    {
        SetLoadingState(true);
        HideError();

        LoginRequestData requestData = new LoginRequestData
        {
            email = username,
            password = password,
            deviceInfo = new UnityDeviceInfo
            {
                deviceModel = SystemInfo.deviceModel,
                os = SystemInfo.operatingSystem
            }
        };

        string jsonData = JsonUtility.ToJson(requestData);
        string fullUrl = baseUrl + loginEndpoint;

        using (UnityWebRequest request = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            SetLoadingState(false);

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    LoginResponseData response = JsonUtility.FromJson<LoginResponseData>(request.downloadHandler.text);

                    if (response.success)
                    {
                        HandleLoginSuccess(response, rememberMe);
                    }
                    else
                    {
                        ShowError(response.message ?? "Login failed. Please try again.");
                        OnLoginFailed?.Invoke(response.message ?? "Unknown error");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error parsing login response: {e.Message}");
                    ShowError("An error occurred. Please try again.");
                    OnLoginFailed?.Invoke("Response parsing error");
                }
            }
            else
            {
                HandleLoginError(request);
            }
        }
    }

    void HandleLoginSuccess(LoginResponseData response, bool rememberMe)
    {
        Debug.Log("Login successful!");

        if (loginData != null)
        {
            loginData.SetLoginCredentials(usernameField.value.Trim(), usernameField.value.Trim(), rememberMe);
            loginData.SetAuthToken(response.token);
            loginData.SetUserAccountData(response.user);
        }

        if (!string.IsNullOrEmpty(response.token))
        {
            PlayerPrefs.SetString("auth_token", response.token);
        }

        if (rememberMe && !string.IsNullOrEmpty(usernameField.value))
        {
            PlayerPrefs.SetString("remembered_username", usernameField.value);
        }
        else
        {
            PlayerPrefs.DeleteKey("remembered_username");
        }

        PlayerPrefs.Save();

        ClearForm();
        OnLoginSuccess?.Invoke(response.token);

        if (autoLoadSceneOnLogin)
        {
            LoadMainScene();
        }
    }

    void HandleLoginError(UnityWebRequest request)
    {
        string errorMessage = "Login failed. Please try again.";

        switch (request.responseCode)
        {
            case 401:
                errorMessage = "Invalid email or password.";
                break;
            case 403:
                errorMessage = "Account is locked or suspended.";
                break;
            case 429:
                errorMessage = "Too many login attempts. Please try again later.";
                break;
            case 500:
                errorMessage = "Server error. Please try again later.";
                break;
        }

        Debug.LogError($"Login failed: {request.error} (Code: {request.responseCode})");
        ShowError(errorMessage);
        OnLoginFailed?.Invoke(errorMessage);
    }

    void OnForgotPasswordClicked(ClickEvent evt)
    {
        Debug.Log("Forgot password clicked");
    }

    void OnSignupClicked(ClickEvent evt)
    {
        Debug.Log("Signup clicked");
    }

    void SetLoadingState(bool isLoading)
    {
        if (loginButton != null)
        {
            loginButton.SetEnabled(!isLoading);
            loginButton.text = isLoading ? "Signing In..." : "Sign In";

            if (isLoading)
                loginButton.AddToClassList("loading");
            else
                loginButton.RemoveFromClassList("loading");
        }

        if (usernameField != null)
            usernameField.SetEnabled(!isLoading);

        if (passwordField != null)
            passwordField.SetEnabled(!isLoading);
    }

    void ShowError(string message)
    {
        if (errorMessage != null && errorText != null)
        {
            errorText.text = message;
            errorMessage.AddToClassList("show");
        }
    }

    void HideError()
    {
        if (errorMessage != null)
        {
            errorMessage.RemoveFromClassList("show");
        }
    }

    bool IsErrorVisible()
    {
        return errorMessage != null && errorMessage.ClassListContains("show");
    }

    void ClearForm()
    {
        if (passwordField != null)
            passwordField.value = "";

        if (!rememberToggle.value && usernameField != null)
            usernameField.value = "";
    }

    void OnEnable()
    {
        LoadRememberedUsername();
    }

    void LoadRememberedUsername()
    {
        string rememberedUsername = "";

        if (loginData != null && loginData.RememberMe && !string.IsNullOrEmpty(loginData.Username))
        {
            rememberedUsername = loginData.Username;
        }
        else if (PlayerPrefs.HasKey("remembered_username"))
        {
            rememberedUsername = PlayerPrefs.GetString("remembered_username");
        }

        if (!string.IsNullOrEmpty(rememberedUsername) && usernameField != null)
        {
            usernameField.value = rememberedUsername;
            rememberToggle.value = true;
        }
    }

    public UserLoginData GetLoginData()
    {
        return loginData;
    }

    public bool IsUserLoggedIn()
    {
        return loginData != null && loginData.IsLoggedIn && loginData.IsTokenValid();
    }

    public string GetCurrentUserToken()
    {
        return loginData?.AuthToken ?? "";
    }

    public string GetCurrentUsername()
    {
        return loginData?.Username ?? "";
    }

    public void ValidateToken()
    {
        string token = GetCurrentUserToken();
        if (!string.IsNullOrEmpty(token))
        {
            StartCoroutine(ValidateTokenCoroutine(token));
        }
        else
        {
            Debug.Log("No token to validate");
        }
    }

    IEnumerator ValidateTokenCoroutine(string token)
    {
        string fullUrl = baseUrl + validateEndpoint;

        using (UnityWebRequest request = UnityWebRequest.Get(fullUrl))
        {
            request.SetRequestHeader("Authorization", "Bearer " + token);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    TokenValidationResponse response = JsonUtility.FromJson<TokenValidationResponse>(request.downloadHandler.text);

                    if (response.success && loginData != null)
                    {
                        loginData.SetUserAccountData(response.user);
                        loginData.UpdateActivity();
                        Debug.Log("Token validation successful");
                    }
                    else
                    {
                        Debug.Log("Token validation failed - logging out");
                        Logout();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error parsing validation response: {e.Message}");
                    Logout();
                }
            }
            else
            {
                Debug.Log($"Token validation failed: {request.error}");
                Logout();
            }
        }
    }

    public void Logout()
    {
        if (loginData != null)
        {
            loginData.Logout();
        }

        PlayerPrefs.DeleteKey("auth_token");
        PlayerPrefs.Save();
        ClearForm();
        HideError();

        // Show login form after logout
        if (loginPanel != null)
            loginPanel.style.display = DisplayStyle.Flex;
    }

    public UnityWebRequest CreateAuthenticatedRequest(string endpoint, string method = "GET", string jsonData = null)
    {
        string token = GetCurrentUserToken();
        if (string.IsNullOrEmpty(token))
        {
            Debug.LogError("No valid token available for authenticated request");
            return null;
        }

        string fullUrl = baseUrl + endpoint;
        UnityWebRequest request;

        if (method == "GET")
        {
            request = UnityWebRequest.Get(fullUrl);
        }
        else
        {
            request = new UnityWebRequest(fullUrl, method);
            request.downloadHandler = new DownloadHandlerBuffer();

            if (!string.IsNullOrEmpty(jsonData))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.SetRequestHeader("Content-Type", "application/json");
            }
        }

        request.SetRequestHeader("Authorization", "Bearer " + token);
        return request;
    }

    void LoadMainScene()
    {
        try
        {
            Debug.Log($"Loading main scene: {mainSceneName}");
            SceneManager.LoadScene(mainSceneName);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load scene '{mainSceneName}': {e.Message}");
            ShowError($"Failed to load main scene. Please check if '{mainSceneName}' is added to Build Settings.");

            if (loginButton != null)
            {
                loginButton.text = "Sign In";
                loginButton.SetEnabled(true);
            }
        }
    }

    public void LoadMainSceneManually()
    {
        if (IsUserLoggedIn())
        {
            LoadMainScene();
        }
        else
        {
            Debug.LogWarning("Cannot load main scene - user not logged in");
            ShowError("Please log in first.");
        }
    }
}

// --- DATA CLASSES ---

[System.Serializable]
public class LoginRequestData
{
    public string email;
    public string password;
    public UnityDeviceInfo deviceInfo;
}

[System.Serializable]
public class UnityDeviceInfo
{
    public string deviceModel;
    public string os;
}

[System.Serializable]
public class LoginResponseData
{
    public bool success;
    public string message;
    public string token;
    public UserAccountData user;
}

[System.Serializable]
public class UserAccountData
{
    public int id;
    public string name;
    public string email;
    public string displayName;
}

[System.Serializable]
public class TokenValidationResponse
{
    public bool success;
    public UserAccountData user;
}
