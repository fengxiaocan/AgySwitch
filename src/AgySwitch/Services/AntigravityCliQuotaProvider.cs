using System.Diagnostics;
using System.Text.Json;
using AgySwitch.Models;

namespace AgySwitch.Services;

public sealed class AntigravityCliQuotaProvider : IQuotaProvider
{
    public async Task<QuotaQueryResult> QueryAsync(
        string executablePath,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Directory.Exists(fullWorkingDirectory) ? fullWorkingDirectory : Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(fullWorkingDirectory)), ".gemini", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullWorkingDirectory)) is { Length: > 0 } homeDirectory)
        {
            // The CLI resolves its global configuration below the user's home. Point this
            // one child process at the selected .gemini directory when a custom path is used.
            startInfo.Environment["USERPROFILE"] = homeDirectory;
            startInfo.Environment["HOME"] = homeDirectory;
        }

        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("/usage");
        startInfo.ArgumentList.Add("--output-format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("--print-timeout");
        startInfo.ArgumentList.Add("45s");

        using var process = new Process { StartInfo = startInfo };
        var queriedAt = DateTimeOffset.Now;
        try
        {
            if (!process.Start())
            {
                return new QuotaQueryResult(false, string.Empty, "无法启动 Antigravity CLI。", queriedAt);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(55));
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            _ = await stderrTask;

            if (process.ExitCode != 0)
            {
                return new QuotaQueryResult(false, string.Empty,
                    "Antigravity CLI 查询失败。请确认 CLI 已登录，且该版本支持 /usage headless 命令。", queriedAt);
            }

            var output = UnwrapResponse(stdout);
            return string.IsNullOrWhiteSpace(output)
                ? new QuotaQueryResult(false, string.Empty, "CLI 没有返回额度信息。", queriedAt)
                : new QuotaQueryResult(true, output.Trim(), null, queriedAt, QuotaResetParser.FindNextReset(output, queriedAt));
        }
        catch (OperationCanceledException)
        {
            TryStop(process);
            var message = cancellationToken.IsCancellationRequested
                ? "额度查询已取消。"
                : "额度查询超时，请稍后重试。";
            return new QuotaQueryResult(false, string.Empty, message, queriedAt);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new QuotaQueryResult(false, string.Empty, "无法启动 CLI。请检查设置中的 antigravity.exe 路径。", queriedAt);
        }
        catch (IOException)
        {
            TryStop(process);
            return new QuotaQueryResult(false, string.Empty, "读取 CLI 输出失败。", queriedAt);
        }
    }

    private static string UnwrapResponse(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = document.RootElement;
                if (root.TryGetProperty("status", out var status)
                    && !string.Equals(status.GetString(), "SUCCESS", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.String)
                {
                    return response.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // Some CLI versions may return a plain text report even when JSON was requested.
        }

        return stdout;
    }

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between checks.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Best effort timeout cleanup.
        }
    }
}
