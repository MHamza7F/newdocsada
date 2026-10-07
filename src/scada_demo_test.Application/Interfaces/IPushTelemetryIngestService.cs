using scada_demo_test.Application.DTOs;

namespace scada_demo_test.Application.Interfaces;

public enum PushIngestStatus
{
    // Telemetry persisted and device/sensor marked online.
    Accepted,

    // Source IP (and optional external id) matched no registered device.
    UnknownDevice,

    // Device known, but no active sensor on it has this Modbus slave address.
    NoMatchingSensor
}

public class PushIngestResult
{
    public PushIngestStatus Status { get; set; }
    public Guid? DeviceId { get; set; }
    public int PersistedReadings { get; set; }
    public int SkippedReadings { get; set; }
}

// Entry point for firmware pushes (Norvi ESP32 currently): attributes the push
// to a registered device, matches readings to sensors by Modbus slave address,
// persists into the per-sensor telemetry table and drives ONLINE state from real
// data arrival. Unknown devices/sensors are dropped without side effects.
public interface IPushTelemetryIngestService
{
    Task<PushIngestResult> IngestAsync(PushTelemetryMessageDto message, CancellationToken ct = default);
}