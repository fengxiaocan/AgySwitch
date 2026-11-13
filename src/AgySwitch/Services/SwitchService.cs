using System.Security.Cryptography;
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
        var running = _processes.GetRunningProcessNames();
        if (running.Count > 0 && !allowWhileCliRunning)
            throw new InvalidOperationException($"检测到 Antigravity CLI 正在运行（{string.Join("、", running)}）。请先关闭 CLI，再确认是否继续切换。");

        var paths = _accounts.Paths;
        var stored = _accounts.LoadProfile(paths, target.Id);
        var store = _accounts.Credentials;
        var targetCredential = _accounts.ReadProfileCredential(paths, stored.Id);
        var targetIdentity = _accounts.ReadIdentity(targetCredential);
        if (targetCredential is null || targetIdentity is null || !_accounts.Matches(stored, targetIdentity))
            throw new InvalidOperationException("目标账户缺少有效的新版 Windows 凭据，或凭据邮箱与档案不一致。请重新登录该账户并备份。");

        var current = store.Read(WindowsCredentialStore.CurrentTarget);
        var currentIdentity = _accounts.ReadIdentity(current);
        if (current is not null && currentIdentity is null)
            throw new InvalidOperationException("无法识别当前 Windows 凭据中的账户，已取消切换以保护现有登录状态。");
        if (_accounts.Matches(stored, currentIdentity))
            throw new InvalidOperationException("这个账户已经是当前账户。");

        if (current is not null)
        {
            _accounts.SaveCurrentAutomatically();
            var latest = store.Read(WindowsCredentialStore.CurrentTarget);
            if (latest is null || !CryptographicOperations.FixedTimeEquals(current.Blob, latest.Blob))
                throw new InvalidOperationException("当前凭据在备份期间发生变化，请关闭 CLI 后重试。");
            store.Write(WindowsCredentialStore.RecoveryTarget(paths), current);
        }
        else
        {
            store.Delete(WindowsCredentialStore.RecoveryTarget(paths));
        }

        try
        {
            store.Write(WindowsCredentialStore.CurrentTarget, targetCredential);
            var installed = store.Read(WindowsCredentialStore.CurrentTarget);
            if (installed is null || !CryptographicOperations.FixedTimeEquals(targetCredential.Blob, installed.Blob)
                || !_accounts.Matches(stored, _accounts.ReadIdentity(installed)))
                throw new IOException("切换后的 Windows 凭据校验失败。");
            stored.LastUsedAt = DateTimeOffset.Now;
            _accounts.SaveProfileMetadata(paths, stored);
            _logger.Info($"账户切换成功：{stored.Id}");
        }
        catch (Exception switchError)
        {
            try
            {
                if (current is null) store.Delete(WindowsCredentialStore.CurrentTarget);
                else store.Write(WindowsCredentialStore.CurrentTarget, current);
                _logger.Error($"账户切换失败，已恢复切换前凭据：{stored.Id}");
            }
            catch (Exception recoveryError)
            {
                _logger.Error($"账户切换失败且自动恢复失败：{stored.Id}");
                var message = current is null
                    ? "切换失败，且无法恢复原来的无凭据状态。请检查 Windows 凭据管理器中的 gemini:antigravity。"
                    : "切换失败，自动恢复也未完成。切换前凭据保存在 Windows 凭据管理器的 AgySwitch 恢复项中。";
                throw new IOException(message, new AggregateException(switchError, recoveryError));
            }
            throw new IOException("账户切换失败，已恢复切换前的 Windows 凭据。", switchError);
        }
    }
}
