using AgySwitch.Models;

namespace AgySwitch.Services;

public interface IQuotaProvider
{
    Task<QuotaQueryResult> QueryAsync(string executablePath, string workingDirectory, CancellationToken cancellationToken = default);
}
