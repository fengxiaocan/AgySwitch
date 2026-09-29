using System.Diagnostics;

namespace AgySwitch.Services;

public sealed class CliProcessService
{
    private static readonly string[] ExecutableNames = ["agy.exe", "antigravity.exe"];
    private static readonly string[] ProcessNames = ["agy", "antigravity", "antigravity-cli"];

    public string? FindExecutable(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var executableName in ExecutableNames)
            {
                var candidate = Path.Combine(directory.Trim('"'), executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    public IReadOnlyList<string> GetRunningProcessNames()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            found.Add(name);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // The process may exit while it is being inspected.
                    }
                }
            }
        }

        return found.ToList();
    }
}
