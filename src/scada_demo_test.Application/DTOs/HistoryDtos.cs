namespace scada_demo_test.Application.DTOs;

public class HistoryPointDto
{
    public DateTime Timestamp { get; set; }
    public double Value { get; set; }
    public int SampleCount { get; set; } = 1;
    public double? Min { get; set; }
    public double? Max { get; set; }
}

public class HistorySeriesDto
{
    public string DeviceExternalId { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string Resolution { get; set; } = string.Empty; // "Raw" | "Hourly" | "Daily" | "Monthly"
    public bool IsCumulative { get; set; }
    public double? Total { get; set; }   // sum of deltas, only meaningful for cumulative metrics
    public double? Average { get; set; } // only meaningful for non-cumulative metrics
    public double? Min { get; set; }
    public double? Max { get; set; }
    public List<HistoryPointDto> Points { get; set; } = new();
}
