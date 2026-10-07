namespace scada_demo_test.Domain.Entities;

// Tier 2 storage. One row = one (Device, Metric, Hour) bucket, built by compressing
// every raw SensorReading that fell inside that hour. Raw rows are purged once this
// row exists (see RollupCompressionService), which is what keeps the raw table small.
public class HourlyRollup
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string Metric { get; set; } = string.Empty;
    public string? Unit { get; set; }

    public DateTime PeriodStart { get; set; } // hour bucket start, UTC
    public DateTime PeriodEnd { get; set; }   // PeriodStart + 1h

    public int SampleCount { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double FirstValue { get; set; }
    public double LastValue { get; set; }

    // Cumulative metrics (Totalizer): the consumed delta over this hour.
    // Non-cumulative metrics (FlowRate, Pressure, ...): the time-weighted average.
    public double AggregatedValue { get; set; }
    public bool IsCumulative { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
