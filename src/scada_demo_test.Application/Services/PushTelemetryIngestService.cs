using Microsoft.Extensions.Logging;
using scada_demo_test.Application.DTOs;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Drivers;
using scada_demo_test.Domain.Enums;
using scada_demo_test.Domain.Interfaces;

namespace scada_demo_test.Application.Services;

// Norvi ESP32 firmware is a Modbus RTU MASTER: it polls its own RS-485 slave
// meters and pushes the results over HTTP. This service turns each push into the
// same per-sensor telemetry-table rows the Modbus TCP poller produces, so the
// dashboard shows real pushed data for both gateway families. It also makes
// device/sensor ONLINE state data-driven by real HTTP arrival - the 1s poller
// watchdog (ModbusPollingHostedService) later flips them offline when pushes stop.
public class PushTelemetryIngestService : IPushTelemetryIngestService
{
    // Firmware metric name -> driver telemetry column (see AGENTS.md alias map).
    private static readonly IReadOnlyDictionary<string, string> MetricAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Temperature"] = "TemperatureC",
            ["Temp"] = "TemperatureC",
            ["TemperatureC"] = "TemperatureC",
            ["Humidity"] = "HumidityRH",
            ["HumidityRH"] = "HumidityRH",
            ["Flowrate"] = "InstantaneousFlowRate",
            ["Flow"] = "InstantaneousFlowRate",
            ["FlowRate"] = "InstantaneousFlowRate",
            ["InstantaneousFlowRate"] = "InstantaneousFlowRate",
            ["Nm3h"] = "InstantaneousFlowRate",
            ["Totalizer"] = "AccumulatedTotalizer",
            ["AccumulatedTotalizer"] = "AccumulatedTotalizer",
            ["Total"] = "AccumulatedTotalizer",
        };

    private readonly IDeviceRepository _deviceRepository;
    private readonly ISensorRepository _sensorRepository;
    private readonly ISensorTelemetryRepository _telemetryRepository;
    private readonly ILogger<PushTelemetryIngestService> _logger;

    public PushTelemetryIngestService(
        IDeviceRepository deviceRepository,
        ISensorRepository sensorRepository,
        ISensorTelemetryRepository telemetryRepository,
        ILogger<PushTelemetryIngestService> logger)
    {
        _deviceRepository = deviceRepository;
        _sensorRepository = sensorRepository;
        _telemetryRepository = telemetryRepository;
        _logger = logger;
    }

    public async Task<PushIngestResult> IngestAsync(PushTelemetryMessageDto message, CancellationToken ct = default)
    {
        var device = await ResolveDeviceAsync(message, ct);
        if (device is null)
        {
            _logger.LogWarning("Push telemetry dropped: no device matches source IP '{Ip}' / external id '{ExternalId}'.",
                message.SourceIpAddress, message.DeviceExternalId);
            return new PushIngestResult { Status = PushIngestStatus.UnknownDevice, SkippedReadings = message.Readings.Count };
        }

        // Whenever the Norvi gateway connects & pushes HTTP, mark the gateway itself ONLINE immediately
        await MarkDeviceOnlineAsync(device, ct);

        var sensor = (await _sensorRepository.GetByDeviceIdAsync(device.Id, ct))
            .FirstOrDefault(s => s.IsActive && s.SlaveAddress == message.TankId);

        if (sensor is null)
        {
            _logger.LogDebug("Push telemetry ignored for device '{Device}' ({DeviceId}): no active sensor configured for Modbus slave address {Slave}.",
                device.Name, device.Id, message.TankId);
            return new PushIngestResult
            {
                Status = PushIngestStatus.NoMatchingSensor,
                DeviceId = device.Id,
                SkippedReadings = message.Readings.Count
            };
        }

        var driver = SensorDriverCatalog.GetByKey(sensor.SensorTypeKey);
        if (driver is null)
        {
            _logger.LogDebug("Push telemetry ignored for sensor '{Sensor}': no driver installed for '{Key}'.",
                sensor.Name, sensor.SensorTypeKey);
            return new PushIngestResult
            {
                Status = PushIngestStatus.NoMatchingSensor,
                DeviceId = device.Id,
                SkippedReadings = message.Readings.Count
            };
        }

        // Map pushed engineering-unit metrics onto the driver's telemetry columns.
        // Unknown metrics and null values (firmware "--" failure placeholders) are
        // skipped - the firmware value is used directly (no register parsing on the
        // push path).
        var columnValues = new Dictionary<string, double>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var reading in message.Readings)
        {
            var metric = reading.Metric.Trim();
            if (reading.Value is not { } value ||
                !MetricAliases.TryGetValue(metric, out var column) ||
                (column != driver.PrimaryColumnName && column != driver.SecondaryColumnName))
            {
                skipped++;
                continue;
            }
            columnValues[column] = value;
        }

        var result = new PushIngestResult
        {
            Status = PushIngestStatus.Accepted,
            DeviceId = device.Id,
            SkippedReadings = skipped
        };

        if (columnValues.Count > 0)
        {
            var tableName = sensor.TelemetryTableName ?? TelemetryTableNaming.Build(driver.SimpleName, sensor.Id);
            var columns = SensorColumnSet.For(driver, sensor.MetricFields);
            await _telemetryRepository.EnsureTelemetryTableAsync(tableName, driver, columns, ct);
            await _telemetryRepository.InsertAsync(
                tableName,
                driver,
                columns,
                Array.Empty<byte>(),
                columnValues.GetValueOrDefault(driver.PrimaryColumnName),
                columnValues.GetValueOrDefault(driver.SecondaryColumnName),
                online: true,
                errorCode: null,
                ct);

            if (!sensor.IsOnline)
            {
                sensor.IsOnline = true;
                await _sensorRepository.UpdateAsync(sensor, ct);
            }

            result.PersistedReadings = 1;
        }
        else
        {
            // The sensor failed to read or is disconnected from RS-485 bus (all readings null / "--")
            if (sensor.IsOnline)
            {
                sensor.IsOnline = false;
                await _sensorRepository.UpdateAsync(sensor, ct);

                var tableName = sensor.TelemetryTableName ?? TelemetryTableNaming.Build(driver.SimpleName, sensor.Id);
                var columns = SensorColumnSet.For(driver, sensor.MetricFields);
                await _telemetryRepository.EnsureTelemetryTableAsync(tableName, driver, columns, ct);
                await _telemetryRepository.InsertAsync(
                    tableName,
                    driver,
                    columns,
                    Array.Empty<byte>(),
                    0,
                    0,
                    online: false,
                    errorCode: "SENSOR_DISCONNECTED",
                    ct);
            }
        }

        return result;
    }

    // Source IP is the primary attribution key (the Norvi payload carries no
    // device id); an explicit deviceId/externalId in the payload is the fallback.
    private async Task<Domain.Entities.Device?> ResolveDeviceAsync(PushTelemetryMessageDto message, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(message.SourceIpAddress))
        {
            var byIp = await _deviceRepository.GetByIpAddressAsync(message.SourceIpAddress.Trim(), ct);
            if (byIp is not null) return byIp;
        }

        if (!string.IsNullOrWhiteSpace(message.DeviceExternalId))
        {
            return await _deviceRepository.GetByExternalIdAsync(message.DeviceExternalId.Trim(), ct);
        }

        return null;
    }

    private async Task MarkDeviceOnlineAsync(Domain.Entities.Device device, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        device.IsOnline = true;
        device.Status = DeviceStatus.Online;
        device.LastSeenAt = now;

        await _deviceRepository.UpdateAsync(device, ct);
    }
}