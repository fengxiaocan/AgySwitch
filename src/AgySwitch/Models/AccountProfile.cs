using System.Text.Json.Serialization;

namespace AgySwitch.Models;

public sealed class AccountProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? AccountId { get; set; }
    public string? AccountKey { get; set; }
    public string? Fingerprint { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    [JsonIgnore]
    public bool IsCurrent { get; set; }

    [JsonIgnore]
    public bool HasCredentials { get; set; }

    [JsonIgnore]
    public string CredentialStatus { get; set; } = string.Empty;

    private static readonly string[] AvatarPalette =
    [
        "#0D9488", "#2563EB", "#4F46E5", "#7C3AED",
        "#EA580C", "#0891B2", "#059669", "#D97706", "#E11D48"
    ];

    [JsonIgnore]
    public string Initials
    {
        get
        {
            var text = !string.IsNullOrWhiteSpace(Name) ? Name.Trim() : (Email ?? "AG");
            if (string.IsNullOrWhiteSpace(text)) return "AG";
            return text.Length <= 2 ? text.ToUpperInvariant() : text[..2].ToUpperInvariant();
        }
    }

    [JsonIgnore]
    public string AvatarBgColor
    {
        get
        {
            var key = string.IsNullOrEmpty(Id) ? (Name ?? "") : Id;
            var hash = Math.Abs(key.GetHashCode());
            return AvatarPalette[hash % AvatarPalette.Length];
        }
    }
}
