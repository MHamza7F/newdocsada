namespace scada_demo_test.Domain.Entities;

// Tier 4 storage. One row = one (Device, Metric, calendar Month) bucket, built by
// compressing the DailyRollup rows for that month. This tier is kept forever - by
// the time data lands here it's already ~99% smaller than the original raw stream,
// so long-term trend reports (a year, several years) stay cheap to query.
public class MonthlyRollup
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string Metric { get; set; } = string.Empty;
    public string? Unit { get; set; }

    public DateTime PeriodStart { get; set; } // 1st of the month, UTC
    public DateTime PeriodEnd { get; set; }   // 1st of next month

    public int SampleCount { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double FirstValue { get; set; }
    public double LastValue { get; set; }

    public double AggregatedValue { get; set; }
    public bool IsCumulative { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
