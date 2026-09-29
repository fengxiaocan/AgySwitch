namespace AgySwitch.Models;

public sealed class AppSettings
{
    public string GeminiDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini");

    public string? CliPath { get; set; }
}
