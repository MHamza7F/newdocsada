using scada_demo_test.Domain.Drivers;

namespace scada_demo_test.Domain.Interfaces;

// One row written by the Modbus polling worker for every successful (or failed)
// poll of a sensor, into that sensor's isolated telemetry table.
public class SensorTelemetryReading
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? RawHexBuffer { get; set; }
    public double PrimaryValue { get; set; }
    public double SecondaryValue { get; set; }
    public short ConnectionStatus { get; set; } = 1; // 0 = offline, 1 = online
    public string? ErrorCode { get; set; }
}

// Provisions and streams rows in/out of the dynamically-created per-sensor
// telemetry tables (telemetry_sensor_{type}_{id}). Table and column names are
// always produced by the driver registry - never user-supplied.
public interface ISensorTelemetryRepository
{
    // CREATE TABLE IF NOT EXISTS for the given sensor using the driver's schema,
    // but only the value columns the operator actually selected for this sensor.
    // Idempotent ADD COLUMN IF NOT EXISTS covers tables provisioned earlier with
    // different (or the full) parameter set.
    Task EnsureTelemetryTableAsync(string tableName, ISensorDriver driver, SensorColumnSet columns, CancellationToken ct = default);

    // DROP TABLE IF EXISTS when a sensor is deleted.
    Task DropTableIfExistsAsync(string tableName, CancellationToken ct = default);

    Task<bool> TableExistsAsync(string tableName, CancellationToken ct = default);

    // Insert one telemetry row (resilient: Polly retry on transient SQL errors).
    // Only the SELECTED columns are written - an unselected parameter stays absent.
    Task InsertAsync(
        string tableName,
        ISensorDriver driver,
        SensorColumnSet columns,
        byte[] rawPayload,
        double primaryValue,
        double secondaryValue,
        bool online,
        string? errorCode,
        CancellationToken ct = default);

    Task<SensorTelemetryReading?> GetLatestAsync(string tableName, ISensorDriver driver, SensorColumnSet columns, CancellationToken ct = default);

    Task<IReadOnlyList<SensorTelemetryReading>> GetRecentAsync(
        string tableName, ISensorDriver driver, SensorColumnSet columns, int count = 20, CancellationToken ct = default);

    // Rows within a UTC time window (newest-first, capped at `limit`). Used by the
    // Reports page and CSV export.
    Task<IReadOnlyList<SensorTelemetryReading>> GetRangeAsync(
        string tableName, ISensorDriver driver, SensorColumnSet columns,
        DateTime fromUtc, DateTime toUtc, int limit = 1000, CancellationToken ct = default);
}