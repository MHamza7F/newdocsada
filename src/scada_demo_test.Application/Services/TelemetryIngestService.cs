using scada_demo_test.Application.DTOs;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Enums;
using scada_demo_test.Domain.Interfaces;

namespace scada_demo_test.Application.Services;

// The one place "a telemetry message arrived" gets turned into
// (1) a DB write and (2) a live broadcast. Every device (all 6 flowmeters,
// or 30 later) flows through this exact same code - nothing device-specific
// lives in this class, which is what keeps adding a 7th meter cheap.
public class TelemetryIngestService : ITelemetryIngestService
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly ISensorReadingRepository _readingRepository;
    private readonly ITelemetryBroadcaster _broadcaster;

    public TelemetryIngestService(
        IDeviceRepository deviceRepository,
        ISensorReadingRepository readingRepository,
        ITelemetryBroadcaster broadcaster)
    {
        _deviceRepository = deviceRepository;
        _readingRepository = readingRepository;
        _broadcaster = broadcaster;
    }

    public async Task IngestAsync(TelemetryMessageDto message, CancellationToken ct = default)
    {
        var device = await _deviceRepository.GetByExternalIdAsync(message.DeviceExternalId, ct);
        if (device is null)
        {
            // Devices are created through the Device Operations Dashboard - unknown
            // external IDs are dropped instead of being auto-registered with
            // hardcoded hardware assumptions.
            return;
        }

        device.LastSeenAt = DateTime.UtcNow;
        device.Status = DeviceStatus.Online;
        device.IsOnline = true;
        await _deviceRepository.UpdateAsync(device, ct);

        foreach (var (metric, value) in message.Metrics)
        {
            var unit = message.Units.GetValueOrDefault(metric);

            var reading = new SensorReading
            {
                DeviceId = device.Id,
                Metric = metric,
                Value = value,
                Timestamp = message.Timestamp,
                Unit = unit
            };

            await _readingRepository.AddAsync(reading, ct);

            await _broadcaster.BroadcastReadingAsync(new LiveReadingDto
            {
                DeviceExternalId = device.ExternalId,
                DeviceName = device.Name,
                Metric = metric,
                Value = value,
                Unit = unit,
                Timestamp = message.Timestamp,
                IsOnline = true
            }, ct);
        }
    }
}
