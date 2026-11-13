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
    private readonly string? _userDirectory;
    internal ICredentialStore Credentials { get; }

    public AccountService(AppSettings settings, AccountIdentityReader identityReader, AppLogger logger, ICredentialStore? credentials = null, string? userDirectory = null)
    {
        _settings = settings;
        _identityReader = identityReader;
        _logger = logger;
        _userDirectory = userDirectory;
        Credentials = credentials ?? new WindowsCredentialStore();
        Paths.MigrateLegacyMetadata();
    }

    public AppPaths Paths => new(_settings.GeminiDirectory, _userDirectory);

    public DetectedAccount? GetCurrentIdentity() => _identityReader.Read(Credentials.Read(WindowsCredentialStore.CurrentTarget));

    internal DetectedAccount? ReadIdentity(CredentialSnapshot? credential) => _identityReader.Read(credential);

    public bool CurrentCredentialsExist => Credentials.Read(WindowsCredentialStore.CurrentTarget) is { Blob.Length: > 0 };

    public bool AnyCurrentCredentialExists => Credentials.Read(WindowsCredentialStore.CurrentTarget) is not null;

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

                var hasToken = ReadProfileCredential(paths, id) is { Blob.Length: > 0 };
                profile.HasCredentials = hasToken;
                profile.CredentialStatus = hasToken ? "Windows 凭据已保存" : "缺少新版凭据，请重新登录并备份";
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
        var credential = Credentials.Read(WindowsCredentialStore.CurrentTarget)
            ?? throw new InvalidOperationException("找不到 Windows 凭据 gemini:antigravity，请先登录 Antigravity CLI。");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("账户名称不能为空。");
        var identity = _identityReader.Read(credential)
            ?? throw new InvalidOperationException("无法从当前 Windows 凭据识别账户邮箱，请重新登录 CLI 后再备份。");
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
        var credentialTarget = WindowsCredentialStore.ProfileTarget(paths, profile.Id);
        var previousCredential = ReadProfileCredential(paths, profile.Id);
        try
        {
            Credentials.Write(credentialTarget, credential);
            WriteProfile(paths, profile);
            _logger.Info(existing is null ? $"已保存账户档案：{profile.Id}" : $"已更新账户档案：{profile.Id}");
            return profile;
        }
        catch (Exception saveError)
        {
            try
            {
                if (previousCredential is null) Credentials.Delete(credentialTarget);
                else Credentials.Write(credentialTarget, previousCredential);
                if (existing is null && Directory.Exists(profileDirectory)) Directory.Delete(profileDirectory, true);
            }
            catch (Exception rollbackError)
            {
                throw new IOException("保存账户失败，且未能恢复原来的档案凭据。", new AggregateException(saveError, rollbackError));
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

        Credentials.Delete(WindowsCredentialStore.LegacyProfileTarget(paths, profile.Id));
        Credentials.Delete(WindowsCredentialStore.ProfileTarget(paths, profile.Id));
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        _logger.Info($"已删除账户档案：{profile.Id}");
    }

    public string GetProfileDirectory(AccountProfile profile) => Paths.GetProfileDirectory(profile.Id);

    internal CredentialSnapshot? ReadProfileCredential(AppPaths paths, string id)
    {
        var credential = Credentials.Read(WindowsCredentialStore.ProfileTarget(paths, id));
        if (credential is not null) return credential;

        credential = Credentials.Read(WindowsCredentialStore.LegacyProfileTarget(paths, id));
        if (credential is not null)
        {
            Credentials.Write(WindowsCredentialStore.ProfileTarget(paths, id), credential);
        }
        return credential;
    }

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

        if (!string.IsNullOrWhiteSpace(profile.Email) && !string.IsNullOrWhiteSpace(identity.Email))
        {
            return string.Equals(profile.Email.Trim(), identity.Email.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(profile.AccountKey)
            && profile.AccountKey.StartsWith("email:", StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.AccountKey, identity.AccountKey, StringComparison.OrdinalIgnoreCase);
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
