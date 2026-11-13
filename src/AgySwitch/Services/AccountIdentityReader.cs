using System.Text.Json;
using AgySwitch.Models;

namespace AgySwitch.Services;

public sealed class AccountIdentityReader
{
    public DetectedAccount? Read(CredentialSnapshot? credential)
    {
        if (credential is null || credential.Blob.Length == 0) return null;
        try
        {
            // Credential Manager stores the CLI's UTF-8 JSON blob, sometimes with trailing NULs.
            var length = credential.Blob.Length;
            while (length > 0 && credential.Blob[length - 1] == 0) length--;
            using var document = JsonDocument.Parse(credential.Blob.AsMemory(0, length));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("id_token_email", out var property)
                || property.ValueKind != JsonValueKind.String) return null;
            var email = property.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Any(char.IsWhiteSpace)) return null;
            return new DetectedAccount(email, null, null, string.Empty);
        }
        catch (JsonException) { return null; }
    }
}
