namespace AgySwitch.Services;

public sealed class AppPaths
{
    public const string AccountsDirectoryName = "antigravity-accounts";

    public AppPaths(string geminiDirectory, string? userDirectory = null)
    {
        GeminiDirectory = Path.GetFullPath(geminiDirectory);
        UserDirectory = Path.GetFullPath(userDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AccountsDirectory = Path.Combine(UserDirectory, AccountsDirectoryName);
        LegacyAccountsDirectory = Path.Combine(GeminiDirectory, AccountsDirectoryName);
        LogsDirectory = Path.Combine(AccountsDirectory, "logs");
    }

    public string GeminiDirectory { get; }
    public string UserDirectory { get; }
    public string AccountsDirectory { get; }
    public string LegacyAccountsDirectory { get; }
    public string LogsDirectory { get; }

    public string GetProfileDirectory(string id) => Path.Combine(AccountsDirectory, id);

    public string GetProfileMetadata(string id) =>
        Path.Combine(GetProfileDirectory(id), "profile.json");

    public void MigrateLegacyMetadata()
    {
        if (string.Equals(LegacyAccountsDirectory, AccountsDirectory, StringComparison.OrdinalIgnoreCase)) return;
        if (!Directory.Exists(LegacyAccountsDirectory)) return;

        foreach (var legacyProfileDirectory in Directory.EnumerateDirectories(LegacyAccountsDirectory))
        {
            var id = Path.GetFileName(legacyProfileDirectory);
            if (id is null || !System.Text.RegularExpressions.Regex.IsMatch(id, "^account_[0-9]{3,}$")) continue;

            var source = Path.Combine(legacyProfileDirectory, "profile.json");
            var destination = GetProfileMetadata(id);
            if (!File.Exists(source) || File.Exists(destination)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }

        var legacyLog = Path.Combine(LegacyAccountsDirectory, "logs", "app.log");
        var newLog = Path.Combine(LogsDirectory, "app.log");
        if (File.Exists(legacyLog) && !File.Exists(newLog))
        {
            Directory.CreateDirectory(LogsDirectory);
            File.Copy(legacyLog, newLog);
        }
    }
}
