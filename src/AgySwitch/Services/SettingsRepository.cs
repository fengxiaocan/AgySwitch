using System.Text.Json;
using System.Text.Json.Serialization;
using AgySwitch.Models;

namespace AgySwitch.Services;

public sealed class SettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AntigravityAccountSwitcher");

    public string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions);
                if (settings is not null && !string.IsNullOrWhiteSpace(settings.GeminiDirectory))
                {
                    return settings;
                }
            }
        }
        catch (JsonException)
        {
            // Use the default path and let the next explicit save repair the settings file.
        }
        catch (IOException)
        {
            // Settings are optional; a read error must not prevent the application from opening.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temporaryFile = SettingsFile + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(settings, JsonOptions));
            if (File.Exists(SettingsFile))
            {
                File.Replace(temporaryFile, SettingsFile, null, true);
            }
            else
            {
                File.Move(temporaryFile, SettingsFile);
            }
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }
}
