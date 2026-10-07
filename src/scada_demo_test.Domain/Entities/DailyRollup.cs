namespace scada_demo_test.Domain.Entities;

// Tier 3 storage. One row = one (Device, Metric, Day) bucket, built by compressing
// the HourlyRollup rows for that day. Source hourly rows are purged once this row
// exists - a day's worth of hourly data (24 rows) collapses into a single row.
public class DailyRollup
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string Metric { get; set; } = string.Empty;
    public string? Unit { get; set; }

    public DateTime PeriodStart { get; set; } // day bucket start (midnight UTC)
    public DateTime PeriodEnd { get; set; }   // PeriodStart + 1d

    public int SampleCount { get; set; }      // number of raw samples this day represents
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double FirstValue { get; set; }
    public double LastValue { get; set; }

    public double AggregatedValue { get; set; }
    public bool IsCumulative { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
