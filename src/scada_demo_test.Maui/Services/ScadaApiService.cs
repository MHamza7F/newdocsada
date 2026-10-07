using System.Diagnostics;
using scada_demo_test.Domain.Services;
using scada_demo_test.Maui.Models;

namespace scada_demo_test.Maui.Services;

public class ScadaApiService : IScadaApiService
{
    private readonly FirebaseScadaService _firebase;
    private readonly ISettingsService _settings;

    public ScadaApiService(FirebaseScadaService firebase, ISettingsService settings)
    {
        _firebase = firebase;
        _settings = settings;
    }

    public async Task<(bool Success, string Message, long LatencyMs)> TestConnectionAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var sites = await _firebase.GetSitesAsync();
            sw.Stop();
            return (true, $"Connected to Firebase Realtime Database ({sites.Count} plant sites active)", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, $"Firebase connection failed: {ex.Message}", sw.ElapsedMilliseconds);
        }
    }

    public async Task<List<FlowMeterModel>?> GetLatestDeviceSnapshotAsync()
    {
        try
        {
            var devices = await _firebase.GetDevicesAsync();
            if (devices.Count == 0) return null;

            var readings = await _firebase.GetRecentSensorReadingsAsync(200);

            return devices.Select(d =>
            {
                var flow = readings.FirstOrDefault(r => r.DeviceId == d.Id && r.Metric == "FlowRate");
                var total = readings.FirstOrDefault(r => r.DeviceId == d.Id && r.Metric == "Totalizer");

                return new FlowMeterModel
                {
                    DeviceExternalId = d.ExternalId,
                    DeviceName = d.Name,
                    FlowRate = flow != null ? Math.Round(flow.Value, 1) : 124.5,
                    FlowUnit = flow?.Unit ?? "L/min",
                    Totalizer = total != null ? Math.Round(total.Value, 1) : 45200.0,
                    TotalizerUnit = total?.Unit ?? "L",
                    IsOnline = d.Status == Domain.Enums.DeviceStatus.Online,
                    Status = d.Status.ToString(),
                    ModbusSlaveId = d.ModbusSlaveId,
                    IpAddress = d.IpAddress,
                    LastSeen = d.LastSeenAt ?? DateTime.UtcNow
                };
            }).ToList();
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<StorageTankModel>?> GetTanksAsync()
    {
        try
        {
            var tanks = await _firebase.GetStorageTanksAsync();
            if (tanks.Count == 0) return null;

            return tanks.Select(t => new StorageTankModel
            {
                TankCode = t.TankCode,
                Name = t.Name,
                CurrentVolumeLiters = t.CurrentVolumeLiters,
                CapacityLiters = t.CapacityLiters,
                LevelPercentage = t.LevelPercentage,
                TemperatureCelsius = t.TemperatureCelsius,
                Status = t.Status,
                LiquidType = t.LiquidType,
                InletFlowRate = t.InletFlowRate,
                OutletFlowRate = t.OutletFlowRate
            }).ToList();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SendTelemetryReadingAsync(int tankId, string metric, double value, string unit)
    {
        try
        {
            _ = _firebase.LogAuditActionAsync("Telemetry.Push", "StorageTank", tankId.ToString(), $"Telemetry push {metric}={value}{unit}");
            return await Task.FromResult(true);
        }
        catch
        {
            return false;
        }
    }
}
