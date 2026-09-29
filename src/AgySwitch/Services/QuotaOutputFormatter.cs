using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AgySwitch.Services;

public static class QuotaOutputFormatter
{
    private static readonly Regex UsageRowPattern = new(
        @"^\s*(?<model>.+?)\s+(?<period>Weekly|Five\s+Hour)\s+Limit\s+Remaining\s+(?<remaining>\d{1,3}(?:\.\d+)?%)\s+(?<resetAt>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Format(string output)
    {
        var rows = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseRow)
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();

        if (rows.Count == 0)
        {
            return output.Trim();
        }

        var formatted = new StringBuilder();
        foreach (var modelGroup in rows.GroupBy(row => row.Model))
        {
            if (formatted.Length > 0)
            {
                formatted.AppendLine();
            }

            formatted.AppendLine(modelGroup.Key);
            foreach (var row in modelGroup)
            {
                formatted.AppendLine($"{row.Period} : {row.Remaining,-4}  {row.ResetAt}");
            }
        }

        return formatted.ToString().TrimEnd();
    }

    public static List<AgySwitch.Models.QuotaModelCard> ParseModelCards(string output)
    {
        var rows = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseRow)
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();

        if (rows.Count == 0)
        {
            return [];
        }

        var result = new List<AgySwitch.Models.QuotaModelCard>();
        foreach (var modelGroup in rows.GroupBy(row => row.Model))
        {
            var card = new AgySwitch.Models.QuotaModelCard
            {
                Model = modelGroup.Key
            };

            foreach (var row in modelGroup)
            {
                var percent = 0.0;
                var rawPercent = row.Remaining.TrimEnd('%');
                if (double.TryParse(rawPercent, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    percent = Math.Clamp(parsed, 0, 100);
                }

                if (row.Period == "7d")
                {
                    card.HasWeekly = true;
                    card.WeeklyRemaining = row.Remaining;
                    card.WeeklyPercent = percent;
                    card.WeeklyResetAt = row.ResetAt;
                }
                else if (row.Period == "5h")
                {
                    card.HasFiveHour = true;
                    card.FiveHourRemaining = row.Remaining;
                    card.FiveHourPercent = percent;
                    card.FiveHourResetAt = row.ResetAt;
                }
            }

            result.Add(card);
        }

        return result;
    }

    private static QuotaUsageRow? ParseRow(string line)
    {
        var match = UsageRowPattern.Match(line);
        if (!match.Success || !DateTimeOffset.TryParse(
                match.Groups["resetAt"].Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var resetAt))
        {
            return null;
        }

        var model = match.Groups["model"].Value.Trim();
        const string modelsSuffix = " Models";
        if (model.EndsWith(modelsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            model = model[..^modelsSuffix.Length];
        }

        var periodName = match.Groups["period"].Value;
        var period = periodName.StartsWith("Weekly", StringComparison.OrdinalIgnoreCase) ? "7d" : "5h";
        var resetAtText = $"{resetAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} 本地";
        return new QuotaUsageRow(model, period, match.Groups["remaining"].Value, resetAtText);
    }

    private sealed record QuotaUsageRow(string Model, string Period, string Remaining, string ResetAt);
}
