using System.Security.Cryptography;
using System.Text.Json;
using AgySwitch.Models;

namespace AgySwitch.Services;

public sealed class AccountIdentityReader
{
    private static readonly string[] EmailProperties = ["email", "emailAddress", "userEmail", "user_email"];
    private static readonly string[] AccountIdProperties = ["accountId", "account_id", "googleAccountId"];
    private static readonly string[] NameProperties = ["displayName", "display_name", "userName", "name"];
    private static readonly string[] ActiveProperties = ["active", "isActive", "is_active", "current", "selected"];

    public DetectedAccount? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        string? email = null;
        string? accountId = null;
        string? displayName = null;

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var candidates = new List<Candidate>();
            if (ReadRootActiveEmail(document.RootElement) is { } activeEmail)
            {
                candidates.Add(new Candidate(activeEmail, null, null, true, -1));
            }

            Visit(document.RootElement, candidates, false, 0);
            var chosen = candidates.OrderByDescending(candidate => candidate.IsActive)
                .ThenBy(candidate => candidate.Depth)
                .FirstOrDefault();
            email = chosen?.Email;
            accountId = chosen?.AccountId;
            displayName = chosen?.Name;
        }
        catch (JsonException)
        {
            // The fingerprint still allows the profile to be backed up and recognized later.
        }

        return new DetectedAccount(email, accountId, displayName, fingerprint);
    }

    private static string? ReadRootActiveEmail(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals("active", StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = property.Value.GetString()?.Trim();
            if (value is null)
            {
                return null;
            }

            var atIndex = value.IndexOf('@');
            return atIndex > 0
                   && atIndex < value.Length - 1
                   && !value.Any(char.IsWhiteSpace)
                   && value[(atIndex + 1)..].Contains('.', StringComparison.Ordinal)
                ? value
                : null;
        }

        return null;
    }

    private static void Visit(JsonElement element, List<Candidate> candidates, bool inheritedActive, int depth)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var isActive = inheritedActive || HasTruthyProperty(element, ActiveProperties);
            var email = ReadProperty(element, EmailProperties);
            if (!string.IsNullOrWhiteSpace(email))
            {
                candidates.Add(new Candidate(
                    email.Trim(),
                    ReadProperty(element, AccountIdProperties) ?? ReadProperty(element, ["id"]),
                    ReadProperty(element, NameProperties),
                    isActive,
                    depth));
            }

            foreach (var property in element.EnumerateObject())
            {
                var activeHint = isActive || property.Name.Equals("activeAccount", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("currentAccount", StringComparison.OrdinalIgnoreCase);
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    Visit(property.Value, candidates, activeHint, depth + 1);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Visit(item, candidates, inheritedActive, depth + 1);
            }
        }
    }

    private static bool HasTruthyProperty(JsonElement element, IEnumerable<string> names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                if (property.Value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (property.Value.ValueKind == JsonValueKind.String
                    && bool.TryParse(property.Value.GetString(), out var parsed) && parsed)
                {
                    return true;
                }

                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number))
                {
                    return number != 0;
                }
            }
        }

        return false;
    }

    private static string? ReadProperty(JsonElement element, IEnumerable<string> names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }

                if (property.Value.ValueKind == JsonValueKind.Number)
                {
                    return property.Value.ToString();
                }
            }
        }

        return null;
    }

    private sealed record Candidate(string Email, string? AccountId, string? Name, bool IsActive, int Depth);
}
