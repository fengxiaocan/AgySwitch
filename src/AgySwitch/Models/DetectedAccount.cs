namespace AgySwitch.Models;

public sealed record DetectedAccount(
    string? Email,
    string? AccountId,
    string? DisplayName,
    string Fingerprint)
{
    public string AccountKey => !string.IsNullOrWhiteSpace(Email)
        ? $"email:{Email.Trim().ToLowerInvariant()}"
        : !string.IsNullOrWhiteSpace(AccountId)
            ? $"id:{AccountId.Trim()}"
            : $"hash:{Fingerprint}";
}
