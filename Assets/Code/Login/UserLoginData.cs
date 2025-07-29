using UnityEngine;

[CreateAssetMenu(fileName = "UserLoginData", menuName = "Authentication/User Login Data")]
public class UserLoginData : ScriptableObject
{
    [Header("User Credentials")]
    [SerializeField] private string userEmail;
    [SerializeField] private string username;

    [Header("Authentication")]
    [SerializeField] private string authToken;
    [SerializeField] private bool isLoggedIn;
    [SerializeField] private bool rememberMe;

    [Header("User Account Info")]
    [SerializeField] private int userId;
    [SerializeField] private string displayName;

    [Header("Session Info")]
    [SerializeField] private System.DateTime loginTime;
    [SerializeField] private System.DateTime lastActivity;

    public string UserEmail
    {
        get => userEmail;
        set => userEmail = value;
    }

    public string Username
    {
        get => username;
        set => username = value;
    }

    public string AuthToken
    {
        get => authToken;
        set => authToken = value;
    }

    public bool IsLoggedIn
    {
        get => isLoggedIn;
        set => isLoggedIn = value;
    }

    public bool RememberMe
    {
        get => rememberMe;
        set => rememberMe = value;
    }

    public int UserId
    {
        get => userId;
        set => userId = value;
    }

    public string DisplayName
    {
        get => displayName;
        set => displayName = value;
    }

    public System.DateTime LoginTime
    {
        get => loginTime;
        set => loginTime = value;
    }

    public System.DateTime LastActivity
    {
        get => lastActivity;
        set => lastActivity = value;
    }

    public void SetLoginCredentials(string email, string user, bool remember)
    {
        userEmail = email;
        username = user;
        rememberMe = remember;
        UpdateActivity();
    }

    public void SetAuthToken(string token)
    {
        authToken = token;
        isLoggedIn = !string.IsNullOrEmpty(token);
        if (isLoggedIn)
        {
            loginTime = System.DateTime.Now;
        }
        UpdateActivity();
    }

    public void SetUserAccountData(UserAccountData userData)
    {
        if (userData != null)
        {
            userId = userData.id;
            username = userData.name;
            userEmail = userData.email;
            displayName = userData.displayName ?? userData.name;
        }
        UpdateActivity();
    }

    public void UpdateActivity()
    {
        lastActivity = System.DateTime.Now;
    }

    public bool IsTokenValid()
    {
        return !string.IsNullOrEmpty(authToken) && isLoggedIn;
    }

    public bool IsSessionExpired(int timeoutMinutes = 30)
    {
        if (!isLoggedIn) return true;

        var timeSinceActivity = System.DateTime.Now - lastActivity;
        return timeSinceActivity.TotalMinutes > timeoutMinutes;
    }

    public void Logout()
    {
        authToken = "";
        isLoggedIn = false;
        userId = 0;
        displayName = "";

        if (!rememberMe)
        {
            userEmail = "";
            username = "";
        }

        UpdateActivity();
    }

    public void ClearAllData()
    {
        userEmail = "";
        username = "";
        authToken = "";
        isLoggedIn = false;
        rememberMe = false;
        userId = 0;
        displayName = "";
        loginTime = default;
        lastActivity = default;
    }

#if UNITY_EDITOR
    [Header("Debug Info (Editor Only)")]
    [SerializeField] private string debugInfo;

    void OnValidate()
    {
        debugInfo = $"Logged In: {isLoggedIn} | Token: {(!string.IsNullOrEmpty(authToken) ? "Present" : "None")} | Last Activity: {lastActivity}";
    }
#endif
}