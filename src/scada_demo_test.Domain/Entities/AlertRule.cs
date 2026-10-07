namespace scada_demo_test.Domain.Entities;

public class AlertRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DeviceId { get; set; }
    public string DeviceExternalId { get; set; } = "ALL"; // Specific device or ALL
    public string Metric { get; set; } = "FlowRate"; // FlowRate, Totalizer, Pressure, Temperature, Offline
    public string Condition { get; set; } = "GreaterThan"; // GreaterThan, LessThan, Equals, Offline
    public double ThresholdValue { get; set; }
    public string Severity { get; set; } = "Warning"; // Info, Warning, Critical
    public bool IsEnabled { get; set; } = true;
    public string? NotificationEmail { get; set; }
    public string? NotificationPhone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
