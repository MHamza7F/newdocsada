namespace scada_demo_test.Application.DTOs;

// Decoded shape of one Norvi ESP32 firmware push (POST /api/telemetry/tank-reading).
// TankId is the Modbus SLAVE address of the meter on the device's RS-485 bus,
// NOT the device id - the device is attributed by source IP (or an optional
// deviceId/externalId supplied by the caller).
public class PushTelemetryMessageDto
{
    public int TankId { get; set; }

    // TCP source IP of the HTTP push - primary device attribution key.
    public string? SourceIpAddress { get; set; }

    // Optional explicit device identifier (fallback when the source IP is unknown).
    public string? DeviceExternalId { get; set; }

    public List<PushTelemetryReadingDto> Readings { get; set; } = new();
}

// One metric of a push. The firmware already sends engineering units, so the
// value is used directly - no Modbus register parsing on the push path.
// Null means the firmware sent a non-numeric failure placeholder ("--") and the
// reading is skipped instead of failing the whole request.
public class PushTelemetryReadingDto
{
    public string Metric { get; set; } = string.Empty;
    public double? Value { get; set; }
    public string Unit { get; set; } = string.Empty;
}