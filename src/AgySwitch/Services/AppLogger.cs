using System.Text;

namespace AgySwitch.Services;

public sealed class AppLogger
{
    private readonly AppPaths _paths;
    private readonly object _lock = new();

    public AppLogger(AppPaths paths) => _paths = paths;

    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogsDirectory);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message}{Environment.NewLine}";
            lock (_lock)
            {
                File.AppendAllText(Path.Combine(_paths.LogsDirectory, "app.log"), line, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Logging is best effort and must not break account operations.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging is best effort and must not break account operations.
        }
    }
}
