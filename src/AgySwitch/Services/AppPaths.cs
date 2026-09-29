namespace AgySwitch.Services;

public sealed class AppPaths
{
    public const string GoogleAccountsFileName = "google_accounts.json";
    public const string OAuthTokenFileName = "antigravity-oauth-token";
    public const string AccountsDirectoryName = "antigravity-accounts";

    public AppPaths(string geminiDirectory)
    {
        GeminiDirectory = Path.GetFullPath(geminiDirectory);
        AccountsDirectory = Path.Combine(GeminiDirectory, AccountsDirectoryName);
        CurrentGoogleAccounts = Path.Combine(GeminiDirectory, GoogleAccountsFileName);
        CurrentOAuthToken = Path.Combine(GeminiDirectory, "antigravity-cli", OAuthTokenFileName);
        RecoveryDirectory = Path.Combine(AccountsDirectory, ".recovery");
        LogsDirectory = Path.Combine(AccountsDirectory, "logs");
    }

    public string GeminiDirectory { get; }
    public string AccountsDirectory { get; }
    public string CurrentGoogleAccounts { get; }
    public string CurrentOAuthToken { get; }
    public string RecoveryDirectory { get; }
    public string LogsDirectory { get; }

    public string GetProfileDirectory(string id) => Path.Combine(AccountsDirectory, id);

    public string GetProfileGoogleAccounts(string id) =>
        Path.Combine(GetProfileDirectory(id), GoogleAccountsFileName);

    public string GetProfileOAuthToken(string id) =>
        Path.Combine(GetProfileDirectory(id), OAuthTokenFileName);

    public string GetProfileMetadata(string id) =>
        Path.Combine(GetProfileDirectory(id), "profile.json");
}
