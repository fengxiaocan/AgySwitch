namespace AgySwitch.Models;

public sealed record QuotaQueryResult(
    bool Succeeded,
    string Output,
    string? Error,
    DateTimeOffset QueriedAt,
    DateTimeOffset? ResetAt = null);
