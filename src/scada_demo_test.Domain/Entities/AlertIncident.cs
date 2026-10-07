namespace scada_demo_test.Domain.Entities;

public class AlertIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AlertRuleId { get; set; }
    public string DeviceExternalId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public double TriggerValue { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning"; // Info, Warning, Critical
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
