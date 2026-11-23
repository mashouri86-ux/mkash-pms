using MKAsh.Core.Domain;

namespace MKAsh.Core.Rules;

public sealed record ReportingPeriod(ReportingFrequency Frequency, DateOnly Start, DateOnly End)
{
    /// <summary>Stable key stored on submissions. Monthly keeps the legacy "YYYY-MM" form.</summary>
    public string Key => Frequency == ReportingFrequency.Monthly
        ? $"{Start:yyyy-MM}"
        : $"{Frequency.ToString().ToLowerInvariant()}:{Start:yyyy-MM-dd}";

    public bool Contains(DateOnly d) => d >= Start && d <= End;
}

/// <summary>Bi-weekly cycles run Sunday to the second Saturday, anchored on Sunday 5 Jan 2025.</summary>
public static class ReportingPeriods
{
    public static readonly DateOnly BiweeklyAnchor = new(2025, 1, 5);

    /// <param name="back">0 = the period containing <paramref name="date"/>, 1 = the one before, …</param>
    public static ReportingPeriod Of(ReportingFrequency f, DateOnly date, int back = 0)
    {
        switch (f)
        {
            case ReportingFrequency.Biweekly:
            {
                var k = (int)Math.Floor(ProgressRules.Days(BiweeklyAnchor, date) / 14.0) - back;
                var s = BiweeklyAnchor.AddDays(k * 14);
                return new(f, s, s.AddDays(13));
            }
            case ReportingFrequency.Monthly:
            {
                var s = new DateOnly(date.Year, date.Month, 1).AddMonths(-back);
                return new(f, s, s.AddMonths(1).AddDays(-1));
            }
            case ReportingFrequency.Quarterly:
            {
                var q = (date.Month - 1) / 3;
                var s = new DateOnly(date.Year, q * 3 + 1, 1).AddMonths(-3 * back);
                return new(f, s, s.AddMonths(3).AddDays(-1));
            }
            default:
            {
                var s = new DateOnly(date.Year - back, 1, 1);
                return new(f, s, new DateOnly(s.Year, 12, 31));
            }
        }
    }

    public static ReportingPeriod Next(ReportingPeriod p) => Of(p.Frequency, p.End.AddDays(1));
}
