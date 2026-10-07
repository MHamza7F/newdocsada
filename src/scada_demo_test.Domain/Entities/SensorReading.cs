namespace scada_demo_test.Domain.Entities;

// Generic reading row - one table serves every device type via the Metric/Value pair,
// so a new sensor type never needs a new table or migration.
public class SensorReading
{
    public long Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    public string Metric { get; set; } = string.Empty;   // e.g. "FlowRate", "Totalizer", "Pressure"
    public double Value { get; set; }
    public string? Unit { get; set; }                    // e.g. "L/min"

    public DateTime Timestamp { get; set; }
}
