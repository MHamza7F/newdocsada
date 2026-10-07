namespace scada_demo_test.Domain.Entities;

// One entity for every slave meter / transducer attached to a physical Device
// (gateway). The SensorTypeKey references an installed driver in the sensor
// driver registry; TelemetryTableName is the isolated time-series table that is
// automatically provisioned for this sensor the moment it is created.
public class Sensor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    public string UniqueSensorId { get; set; } = string.Empty; // unique per project, e.g. "S-101"
    public string Name { get; set; } = string.Empty;
    public string SensorType { get; set; } = string.Empty; // legacy display type (deprecated)

    // ---- Driver registry link (AOSONG_AQ3485 | KAIFENG_FLOWMETER) ----
    public string SensorTypeKey { get; set; } = string.Empty;

    // ---- Modbus communication tuning (pre-filled from the driver manual) ----
    public int SlaveAddress { get; set; } = 1;
    public int PollIntervalSeconds { get; set; } = 5;
    public double CalibrationMultiplier { get; set; } = 1.0;

    // ---- Physical telemetry storage ----
    public string? TelemetryTableName { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsOnline { get; set; }

    // Type-specific fields stored as JSON/strings for flexibility
    public string? MetricFields { get; set; } // JSON: {"Totalizer":"L","Unit":"L/min"}
    public string? Config { get; set; } // JSON: sensor settings

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}