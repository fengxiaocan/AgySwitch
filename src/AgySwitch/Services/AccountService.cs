using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgySwitch.Models;

namespace AgySwitch.Services;

public sealed class AccountService
{
    private static readonly Regex AccountIdPattern = new("^account_[0-9]{3,}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppSettings _settings;
    private readonly AccountIdentityReader _identityReader;
    private readonly AppLogger _logger;

    public AccountService(AppSettings settings, AccountIdentityReader identityReader, AppLogger logger)
    {
        _settings = settings;
        _identityReader = identityReader;
        _logger = logger;
    }

    public AppPaths Paths => new(_settings.GeminiDirectory);

    public DetectedAccount? GetCurrentIdentity() => _identityReader.Read(Paths.CurrentGoogleAccounts);

    public bool CurrentCredentialsExist => File.Exists(Paths.CurrentGoogleAccounts) && File.Exists(Paths.CurrentOAuthToken);

    public bool AnyCurrentCredentialExists => File.Exists(Paths.CurrentGoogleAccounts) || File.Exists(Paths.CurrentOAuthToken);

    public string CurrentAccountLabel
    {
        get
        {
            var identity = GetCurrentIdentity();
            if (!string.IsNullOrWhiteSpace(identity?.Email))
            {
                return identity.Email;
            }

            return AnyCurrentCredentialExists ? "未知账户" : "尚未登录";
        }
    }

    public IReadOnlyList<AccountProfile> GetProfiles()
    {
        var paths = Paths;
        if (!Directory.Exists(paths.AccountsDirectory))
        {
            return [];
        }

        var current = GetCurrentIdentity();
        var result = new List<AccountProfile>();
        foreach (var directory in Directory.EnumerateDirectories(paths.AccountsDirectory))
        {
            var id = Path.GetFileName(directory);
            if (!AccountIdPattern.IsMatch(id))
            {
                continue;
            }

            var profileFile = paths.GetProfileMetadata(id);
            if (!File.Exists(profileFile))
            {
                continue;
            }

            try
            {
                var profile = JsonSerializer.Deserialize<AccountProfile>(File.ReadAllText(profileFile), JsonOptions);
                if (profile is null || !string.Equals(profile.Id, id, StringComparison.Ordinal))
                {
                    continue;
                }

                var hasGoogle = File.Exists(paths.GetProfileGoogleAccounts(id));
                var hasToken = File.Exists(paths.GetProfileOAuthToken(id));
                profile.HasCredentials = hasGoogle && hasToken;
                profile.CredentialStatus = hasGoogle && hasToken
                    ? "认证文件完整"
                    : !hasGoogle && !hasToken
                        ? "缺少两个认证文件"
                        : hasGoogle
                            ? "缺少 OAuth Token"
                            : "缺少 Google 账户文件";
                profile.IsCurrent = Matches(profile, current);
                if (profile.IsCurrent && string.IsNullOrWhiteSpace(profile.Email))
                {
                    profile.Email = current?.Email;
                }

                result.Add(profile);
            }
            catch (JsonException)
            {
                _logger.Error($"档案元数据无法读取：{id}");
            }
            catch (IOException)
            {
                _logger.Error($"档案元数据读取失败：{id}");
            }
        }

        return result.OrderByDescending(profile => profile.IsCurrent)
            .ThenBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public AccountProfile SaveCurrent(string name, bool saveAsNew = false)
    {
        var paths = Paths;
        if (!File.Exists(paths.CurrentGoogleAccounts))
        {
            throw new InvalidOperationException("找不到当前 google_accounts.json。请确认选择的是 Antigravity CLI 使用的 .gemini 目录。");
        }

        if (!File.Exists(paths.CurrentOAuthToken))
        {
            throw new InvalidOperationException("找不到当前 antigravity-oauth-token。请确认 Antigravity CLI 已登录。");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("账户名称不能为空。");
        }

        var identity = _identityReader.Read(paths.CurrentGoogleAccounts)
            ?? throw new InvalidOperationException("无法读取当前账户文件。");
        var existing = saveAsNew ? null : FindMatchingProfile(identity);
        var profile = existing ?? new AccountProfile
        {
            Id = CreateNextProfileId(paths),
            CreatedAt = DateTimeOffset.Now
        };
        profile.Name = name.Trim();
        profile.Email = identity.Email;
        profile.AccountId = identity.AccountId;
        profile.AccountKey = identity.AccountKey;
        profile.Fingerprint = identity.Fingerprint;
        profile.LastUsedAt = DateTimeOffset.Now;

        var profileDirectory = paths.GetProfileDirectory(profile.Id);
        Directory.CreateDirectory(profileDirectory);
        try
        {
            FilePairTransaction.Apply(paths.AccountsDirectory,
            [
                (paths.CurrentGoogleAccounts, paths.GetProfileGoogleAccounts(profile.Id)),
                (paths.CurrentOAuthToken, paths.GetProfileOAuthToken(profile.Id))
            ]);
            WriteProfile(paths, profile);
            _logger.Info(existing is null ? $"已保存账户档案：{profile.Id}" : $"已更新账户档案：{profile.Id}");
            return profile;
        }
        catch
        {
            if (existing is null && Directory.Exists(profileDirectory))
            {
                try
                {
                    Directory.Delete(profileDirectory, true);
                }
                catch (IOException)
                {
                    // Preserve files if cleanup is blocked; the user can inspect the account directory.
                }
            }

            throw;
        }
    }

    public AccountProfile? SaveCurrentAutomatically()
    {
        if (!CurrentCredentialsExist)
        {
            return null;
        }

        var identity = GetCurrentIdentity();
        var existing = identity is null ? null : FindMatchingProfile(identity);
        var proposedName = existing?.Name
            ?? (!string.IsNullOrWhiteSpace(identity?.Email) ? $"Google 账户 - {identity.Email}" : $"未知账户 - {DateTime.Now:HHmmss}");
        return SaveCurrent(proposedName);
    }

    public void UpdateCurrentBackup(AccountProfile profile)
    {
        var current = GetCurrentIdentity();
        if (!Matches(profile, current))
        {
            throw new InvalidOperationException("只有当前正在使用的账户可以更新备份。");
        }

        SaveCurrent(profile.Name);
    }

    public void Rename(AccountProfile profile, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("账户名称不能为空。");
        }

        var paths = Paths;
        var stored = LoadProfile(paths, profile.Id);
        stored.Name = name.Trim();
        WriteProfile(paths, stored);
        _logger.Info($"已重命名账户档案：{profile.Id}");
    }

    public void Delete(AccountProfile profile)
    {
        if (!AccountIdPattern.IsMatch(profile.Id))
        {
            throw new InvalidOperationException("账户档案 ID 无效。");
        }

        var paths = Paths;
        var directory = Path.GetFullPath(paths.GetProfileDirectory(profile.Id));
        var expectedParent = Path.GetFullPath(paths.AccountsDirectory) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("账户目录路径无效。");
        }

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        _logger.Info($"已删除账户档案：{profile.Id}");
    }

    public string GetProfileDirectory(AccountProfile profile) => Paths.GetProfileDirectory(profile.Id);

    public bool IsCurrent(AccountProfile profile) => Matches(profile, GetCurrentIdentity());

    internal AccountProfile LoadProfile(AppPaths paths, string id)
    {
        if (!AccountIdPattern.IsMatch(id))
        {
            throw new InvalidOperationException("账户档案 ID 无效。");
        }

        var profileFile = paths.GetProfileMetadata(id);
        if (!File.Exists(profileFile))
        {
            throw new FileNotFoundException("账户档案不存在。", profileFile);
        }

        return JsonSerializer.Deserialize<AccountProfile>(File.ReadAllText(profileFile), JsonOptions)
            ?? throw new InvalidDataException("账户档案元数据无效。");
    }

    internal void SaveProfileMetadata(AppPaths paths, AccountProfile profile) => WriteProfile(paths, profile);

    internal bool Matches(AccountProfile profile, DetectedAccount? identity)
    {
        if (identity is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(profile.Email) && !string.IsNullOrWhiteSpace(identity.Email)
            && string.Equals(profile.Email.Trim(), identity.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(profile.AccountId) && !string.IsNullOrWhiteSpace(identity.AccountId)
            && string.Equals(profile.AccountId, identity.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(profile.AccountKey)
            && string.Equals(profile.AccountKey, identity.AccountKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(profile.Fingerprint)
            && string.Equals(profile.Fingerprint, identity.Fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private AccountProfile? FindMatchingProfile(DetectedAccount identity) =>
        GetProfiles().FirstOrDefault(profile => Matches(profile, identity));

    private static string CreateNextProfileId(AppPaths paths)
    {
        Directory.CreateDirectory(paths.AccountsDirectory);
        var max = Directory.EnumerateDirectories(paths.AccountsDirectory)
            .Select(Path.GetFileName)
            .Where(name => name is not null && AccountIdPattern.IsMatch(name))
            .Select(name => int.TryParse(name![8..], out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"account_{max + 1:000}";
    }

    private static void WriteProfile(AppPaths paths, AccountProfile profile)
    {
        var destination = paths.GetProfileMetadata(profile.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profile, JsonOptions));
            if (File.Exists(destination))
            {
                File.Replace(temporary, destination, null, true);
            }
            else
            {
                File.Move(temporary, destination);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
