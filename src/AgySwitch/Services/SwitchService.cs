using System.Text.Json;
using AgySwitch.Models;

namespace AgySwitch.Services;

public sealed class SwitchService
{
    private readonly AccountService _accounts;
    private readonly CliProcessService _processes;
    private readonly AppLogger _logger;

    public SwitchService(AccountService accounts, CliProcessService processes, AppLogger logger)
    {
        _accounts = accounts;
        _processes = processes;
        _logger = logger;
    }

    public IReadOnlyList<string> GetRunningCliProcesses() => _processes.GetRunningProcessNames();

    public void SwitchTo(AccountProfile target, bool allowWhileCliRunning)
    {
        var runningProcesses = _processes.GetRunningProcessNames();
        if (runningProcesses.Count > 0 && !allowWhileCliRunning)
        {
            throw new InvalidOperationException(
                $"检测到 Antigravity CLI 正在运行（{string.Join("、", runningProcesses)}）。请先关闭 CLI，再确认是否继续切换。");
        }

        var paths = _accounts.Paths;
        var targetDirectory = paths.GetProfileDirectory(target.Id);
        var targetGoogleAccounts = paths.GetProfileGoogleAccounts(target.Id);
        var targetOAuthToken = paths.GetProfileOAuthToken(target.Id);
        ValidateTarget(target, targetDirectory, targetGoogleAccounts, targetOAuthToken);

        var currentIdentity = _accounts.GetCurrentIdentity();
        if (_accounts.Matches(target, currentIdentity))
        {
            throw new InvalidOperationException("这个账户已经是当前账户。");
        }

        if (_accounts.AnyCurrentCredentialExists && !_accounts.CurrentCredentialsExist)
        {
            throw new InvalidOperationException("当前认证文件不完整，无法安全备份；已取消切换，请先检查 .gemini 目录。");
        }

        if (_accounts.CurrentCredentialsExist)
        {
            _accounts.SaveCurrentAutomatically();
        }

        CreateRecoverySnapshot(paths);
        try
        {
            FilePairTransaction.Apply(paths.GeminiDirectory,
            [
                (targetGoogleAccounts, paths.CurrentGoogleAccounts),
                (targetOAuthToken, paths.CurrentOAuthToken)
            ]);

            VerifyInstalledTarget(target, targetGoogleAccounts, targetOAuthToken, paths);

            var storedProfile = _accounts.LoadProfile(paths, target.Id);
            storedProfile.LastUsedAt = DateTimeOffset.Now;
            _accounts.SaveProfileMetadata(paths, storedProfile);
            _logger.Info($"账户切换成功：{target.Id}");
        }
        catch (Exception switchError)
        {
            try
            {
                RestoreRecoverySnapshot(paths);
                _logger.Error($"账户切换失败，已恢复切换前认证文件：{target.Id}");
            }
            catch (Exception recoveryError)
            {
                _logger.Error($"账户切换失败且自动恢复失败：{target.Id}");
                throw new IOException(
                    $"切换失败，自动恢复也未完成。请从以下目录手动恢复切换前文件：{paths.RecoveryDirectory}",
                    new AggregateException(switchError, recoveryError));
            }

            throw new IOException($"账户切换失败，已恢复切换前文件。{switchError.Message}", switchError);
        }
    }

    private static void ValidateTarget(AccountProfile target, string directory, string googleFile, string tokenFile)
    {
        if (!Directory.Exists(directory) || !File.Exists(googleFile) || !File.Exists(tokenFile))
        {
            throw new InvalidOperationException("目标账户的认证文件不完整，无法切换。");
        }

        if (new FileInfo(tokenFile).Length == 0)
        {
            throw new InvalidOperationException("目标账户的 OAuth Token 文件为空，无法切换。");
        }

        try
        {
            using var _ = JsonDocument.Parse(File.ReadAllBytes(googleFile));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("目标账户的 google_accounts.json 格式无效，无法切换。");
        }

        if (string.IsNullOrWhiteSpace(target.Id))
        {
            throw new InvalidOperationException("目标账户档案 ID 无效。");
        }
    }

    private void VerifyInstalledTarget(
        AccountProfile target,
        string sourceGoogleAccounts,
        string sourceOAuthToken,
        AppPaths paths)
    {
        var installedGoogleAccounts = _accounts.GetCurrentIdentity();
        if (!string.IsNullOrWhiteSpace(target.Email)
            && !string.IsNullOrWhiteSpace(installedGoogleAccounts?.Email)
            && !string.Equals(target.Email, installedGoogleAccounts.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("切换后的账户邮箱与目标档案不一致。");
        }

        if (!FilesHaveSameContent(sourceGoogleAccounts, paths.CurrentGoogleAccounts)
            || !FilesHaveSameContent(sourceOAuthToken, paths.CurrentOAuthToken))
        {
            throw new IOException("切换后的认证文件校验失败。");
        }
    }

    private static bool FilesHaveSameContent(string first, string second)
    {
        using var firstStream = File.OpenRead(first);
        using var secondStream = File.OpenRead(second);
        if (firstStream.Length != secondStream.Length)
        {
            return false;
        }

        var firstHash = System.Security.Cryptography.SHA256.HashData(firstStream);
        var secondHash = System.Security.Cryptography.SHA256.HashData(secondStream);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(firstHash, secondHash);
    }

    private void CreateRecoverySnapshot(AppPaths paths)
    {
        Directory.CreateDirectory(paths.AccountsDirectory);
        var recoveryTemporary = paths.RecoveryDirectory + ".tmp-" + Guid.NewGuid().ToString("N");
        var previousRecovery = paths.RecoveryDirectory + ".previous-" + Guid.NewGuid().ToString("N");
        var previousMoved = false;
        try
        {
            Directory.CreateDirectory(recoveryTemporary);
            var googleExists = File.Exists(paths.CurrentGoogleAccounts);
            var tokenExists = File.Exists(paths.CurrentOAuthToken);
            if (googleExists)
            {
                File.Copy(paths.CurrentGoogleAccounts, Path.Combine(recoveryTemporary, AppPaths.GoogleAccountsFileName));
            }

            if (tokenExists)
            {
                File.Copy(paths.CurrentOAuthToken, Path.Combine(recoveryTemporary, AppPaths.OAuthTokenFileName));
            }

            var manifest = new RecoveryManifest(googleExists, tokenExists, DateTimeOffset.Now);
            File.WriteAllText(Path.Combine(recoveryTemporary, "manifest.json"), JsonSerializer.Serialize(manifest));

            if (Directory.Exists(paths.RecoveryDirectory))
            {
                Directory.Move(paths.RecoveryDirectory, previousRecovery);
                previousMoved = true;
            }

            Directory.Move(recoveryTemporary, paths.RecoveryDirectory);
            if (previousMoved)
            {
                Directory.Delete(previousRecovery, true);
            }
        }
        catch
        {
            if (!Directory.Exists(paths.RecoveryDirectory) && previousMoved && Directory.Exists(previousRecovery))
            {
                Directory.Move(previousRecovery, paths.RecoveryDirectory);
            }

            if (Directory.Exists(recoveryTemporary))
            {
                try
                {
                    Directory.Delete(recoveryTemporary, true);
                }
                catch (IOException)
                {
                    // Leave the temporary copy in place if cleanup is blocked.
                }
                catch (UnauthorizedAccessException)
                {
                    // Leave the temporary copy in place if cleanup is blocked.
                }
            }

            throw;
        }

        _logger.Info("已创建切换恢复副本。");
    }

    private static void RestoreRecoverySnapshot(AppPaths paths)
    {
        var manifestFile = Path.Combine(paths.RecoveryDirectory, "manifest.json");
        if (!File.Exists(manifestFile))
        {
            throw new FileNotFoundException("找不到切换恢复清单。", manifestFile);
        }

        var manifest = JsonSerializer.Deserialize<RecoveryManifest>(File.ReadAllText(manifestFile))
            ?? throw new InvalidDataException("切换恢复清单无效。");

        var googleSnapshot = Path.Combine(paths.RecoveryDirectory, AppPaths.GoogleAccountsFileName);
        var tokenSnapshot = Path.Combine(paths.RecoveryDirectory, AppPaths.OAuthTokenFileName);
        FilePairTransaction.Apply(paths.GeminiDirectory,
        [
            (manifest.GoogleAccountsExisted ? googleSnapshot : null, paths.CurrentGoogleAccounts),
            (manifest.OAuthTokenExisted ? tokenSnapshot : null, paths.CurrentOAuthToken)
        ]);
    }

    private sealed record RecoveryManifest(bool GoogleAccountsExisted, bool OAuthTokenExisted, DateTimeOffset CreatedAt);
}
