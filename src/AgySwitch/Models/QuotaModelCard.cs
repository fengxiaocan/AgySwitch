namespace AgySwitch.Models;

public sealed class QuotaModelCard
{
    public string Model { get; set; } = string.Empty;
    public string WeeklyRemaining { get; set; } = "--";
    public double WeeklyPercent { get; set; }
    public string WeeklyResetAt { get; set; } = string.Empty;
    public bool HasWeekly { get; set; }

    public string FiveHourRemaining { get; set; } = "--";
    public double FiveHourPercent { get; set; }
    public string FiveHourResetAt { get; set; } = string.Empty;
    public bool HasFiveHour { get; set; }
}
