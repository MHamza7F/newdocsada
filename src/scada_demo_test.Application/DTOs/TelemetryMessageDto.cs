namespace scada_demo_test.Application.DTOs;

// Shape of the payload published by ANY device gateway (Norvi/ESP32 today,
// a different gateway brand tomorrow) - keep the wire format generic.
public class TelemetryMessageDto
{
    public string DeviceExternalId { get; set; } = string.Empty; // e.g. "fm-water-01"
    public DateTime Timestamp { get; set; }
    public Dictionary<string, double> Metrics { get; set; } = new();
    // e.g. { "FlowRate": 12.4, "Totalizer": 10456.2 }
    public Dictionary<string, string> Units { get; set; } = new();
    // e.g. { "FlowRate": "L/min", "Totalizer": "L" } - per-device, since a
    // steam line and a water line don't share units.
}

public class LiveReadingDto
{
    public string DeviceExternalId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public double Value { get; set; }
    public string? Unit { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsOnline { get; set; } = true;

    // Which SLAVE sensor produced this reading (populated by the polling worker).
    // The Monitoring dashboard keys its cards on the sensor, not the gateway.
    public string? SensorExternalId { get; set; }
    public string? SensorName { get; set; }
}
