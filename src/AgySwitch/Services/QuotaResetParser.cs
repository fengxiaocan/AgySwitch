using System.Globalization;
using System.Text.RegularExpressions;

namespace AgySwitch.Services;

public static class QuotaResetParser
{
    private static readonly Regex DateTimePattern = new(
        @"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}[T ]\d{1,2}:\d{2}(?::\d{2})?(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ClockPattern = new(
        @"(?<!\d)(?<hour>\d{1,2}):(?<minute>[0-5]\d)(?::(?<second>[0-5]\d))?\s*(?<ampm>AM|PM)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DurationPartPattern = new(
        @"(?<value>\d+)\s*(?<unit>d(?:ays?)?|天|h(?:rs?|ours?)?|小时|m(?:ins?|inutes?)?|分(?:钟)?|s(?:ecs?|econds?)?|秒)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] ResetMarkers = ["reset", "refresh", "remaining", "重置", "刷新", "恢复", "下次"];
    private static readonly string[] RelativeMarkers = ["in ", "in:", "within ", "within:", "after ", "after:", "left", "remaining", "剩余", "还要", "还有", "后", "倒计时"];

    public static DateTimeOffset? FindNextReset(string output, DateTimeOffset queriedAt)
    {
        var candidates = new List<DateTimeOffset>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!ContainsAny(line, ResetMarkers))
            {
                continue;
            }

            var isRelative = ContainsAny(line, RelativeMarkers);
            var dateMatch = DateTimePattern.Match(line);
            if (dateMatch.Success && DateTimeOffset.TryParse(
                    dateMatch.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var absoluteDate))
            {
                AddIfFuture(candidates, absoluteDate, queriedAt);
                continue;
            }

            if (isRelative && TryParseDuration(line, queriedAt, out var relativeReset))
            {
                AddIfFuture(candidates, relativeReset, queriedAt);
                continue;
            }

            var clockMatch = ClockPattern.Match(line);
            if (!clockMatch.Success)
            {
                continue;
            }

            var hour = int.Parse(clockMatch.Groups["hour"].Value, CultureInfo.InvariantCulture);
            var minute = int.Parse(clockMatch.Groups["minute"].Value, CultureInfo.InvariantCulture);
            var second = clockMatch.Groups["second"].Success
                ? int.Parse(clockMatch.Groups["second"].Value, CultureInfo.InvariantCulture)
                : 0;
            var ampm = clockMatch.Groups["ampm"].Value;

            if (isRelative)
            {
                var duration = new TimeSpan(hour, minute, second);
                if (duration > TimeSpan.Zero)
                {
                    AddIfFuture(candidates, queriedAt.Add(duration), queriedAt);
                }

                continue;
            }

            if (ampm.Equals("PM", StringComparison.OrdinalIgnoreCase) && hour < 12)
            {
                hour += 12;
            }
            else if (ampm.Equals("AM", StringComparison.OrdinalIgnoreCase) && hour == 12)
            {
                hour = 0;
            }

            var localNow = queriedAt.ToLocalTime();
            var localReset = new DateTimeOffset(
                localNow.Year,
                localNow.Month,
                localNow.Day,
                hour,
                minute,
                second,
                localNow.Offset);
            if (localReset <= localNow)
            {
                localReset = localReset.AddDays(1);
            }

            AddIfFuture(candidates, localReset, queriedAt);
        }

        return candidates.Count == 0 ? null : candidates.Min();
    }

    private static bool TryParseDuration(string line, DateTimeOffset queriedAt, out DateTimeOffset resetAt)
    {
        var matches = DurationPartPattern.Matches(line);
        if (matches.Count > 0)
        {
            var days = 0;
            var hours = 0;
            var minutes = 0;
            var seconds = 0;
            foreach (Match match in matches)
            {
                var value = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
                var unit = match.Groups["unit"].Value;
                if (unit.StartsWith('d') || unit == "天")
                {
                    days += value;
                }
                else if (unit.StartsWith('h') || unit == "小时")
                {
                    hours += value;
                }
                else if (unit.StartsWith('m') || unit.StartsWith("分", StringComparison.Ordinal))
                {
                    minutes += value;
                }
                else
                {
                    seconds += value;
                }
            }

            var duration = TimeSpan.FromDays(days) + TimeSpan.FromHours(hours)
                + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            if (duration > TimeSpan.Zero)
            {
                resetAt = queriedAt.Add(duration);
                return true;
            }
        }

        resetAt = default;
        return false;
    }

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static void AddIfFuture(List<DateTimeOffset> candidates, DateTimeOffset value, DateTimeOffset queriedAt)
    {
        if (value > queriedAt)
        {
            candidates.Add(value);
        }
    }
}
