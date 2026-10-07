using scada_demo_test.Maui.Models;

namespace scada_demo_test.Maui.Services;

public record ApiDeviceSnapshot(
    Guid Id,
    string ExternalId,
    string Name,
    string DeviceType,
    string Status,
    int? ModbusSlaveId,
    string? IpAddress,
    DateTime? LastSeenAt,
    double FlowRate,
    string FlowUnit,
    double Totalizer,
    string TotalizerUnit
);

public interface IScadaApiService
{
    Task<(bool Success, string Message, long LatencyMs)> TestConnectionAsync();
    Task<List<FlowMeterModel>?> GetLatestDeviceSnapshotAsync();
    Task<List<StorageTankModel>?> GetTanksAsync();
    Task<bool> SendTelemetryReadingAsync(int tankId, string metric, double value, string unit);
}
