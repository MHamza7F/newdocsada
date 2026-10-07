using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Drivers;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Interfaces;
using scada_demo_test.Infrastructure.Persistence;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace scada_demo_test.API.Controllers;

// Sensor = slave meter / transducer attached to a physical Device (gateway).
// Creating a sensor registers the Modbus pointer AND provisions its isolated
// telemetry table (telemetry_sensor_{type}_{id}) - the polling worker picks it up
// on the very next cycle with zero restarts.
[ApiController]
[Route("api/[controller]")]
public class SensorsController : ControllerBase
{
    private readonly ISensorRepository _sensors;
    private readonly IDeviceRepository _devices;
    private readonly ISensorTelemetryRepository _telemetry;
    private readonly AuditLogService _auditLogs;

    public SensorsController(
        ISensorRepository sensors,
        IDeviceRepository devices,
        ISensorTelemetryRepository telemetry,
        AuditLogService auditLogs)
    {
        _sensors = sensors;
        _devices = devices;
        _telemetry = telemetry;
        _auditLogs = auditLogs;
    }

    public record DriverCatalogDto(
        string DriverKey,
        string SimpleName,
        string DisplayName,
        string Description,
        int DefaultSlaveAddress,
        int DefaultPollIntervalSeconds,
        ushort StartRegister,
        ushort RegisterQuantity,
        byte FunctionCode,
        string UnitPrimary,
        string UnitSecondary);

    public record SensorDto(
        Guid Id,
        string UniqueSensorId,
        string Name,
        Guid DeviceId,
        string DeviceName,
        string SensorTypeKey,
        string DriverDisplayName,
        int SlaveAddress,
        int PollIntervalSeconds,
        double CalibrationMultiplier,
        string? TelemetryTableName,
        bool IsActive,
        bool IsOnline,
        double? LatestPrimary,
        double? LatestSecondary,
        string? UnitPrimary,
        string? UnitSecondary,
        DateTime? LatestTimestampUtc,
        string? MetricFields,
        string? Config);

    public record CreateSensorRequest(
        Guid DeviceId,
        string SensorName,
        string SensorTypeKey,
        int SlaveAddress,
        int PollIntervalSeconds,
        double? CalibrationMultiplier,
        Dictionary<string, string>? SelectedParameters = null,
        string? SensorConfig = null);

    // Editing an EXISTING sensor: rename it, set poll/calibration, and - the main
    // use case - change WHICH meter parameters it reports (e.g. swap one selected
    // parameter for two). Slave address is the Modbus pointer identity and stays
    // immutable here (it's enforced unique per device).
    public record UpdateSensorRequest(
        string? SensorName,
        int? PollIntervalSeconds,
        double? CalibrationMultiplier,
        Dictionary<string, string>? SelectedParameters = null,
        string? SensorConfig = null);

    public record SensorReadingDto(
        DateTime TimestampUtc,
        string? RawHexBuffer,
        double PrimaryValue,
        double SecondaryValue,
        short ConnectionStatus,
        string? ErrorCode);

    // Installed driver library catalog - drives the "Add Sensor" form dropdown.
    [HttpGet("libraries")]
    public IActionResult GetLibraries() =>
        Ok(SensorDriverCatalog.Drivers.Select(d => new DriverCatalogDto(
            d.DriverKey,
            d.SimpleName,
            d.DisplayName,
            d.Description,
            d.DefaultSlaveAddress,
            d.DefaultPollIntervalSeconds,
            d.StartRegister,
            d.RegisterQuantity,
            d.FunctionCode,
            d.UnitPrimary,
            d.UnitSecondary)));

    [HttpGet]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesView}")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? deviceId = null)
    {
        var sensors = deviceId is null
            ? await _sensors.GetAllAsync()
            : await _sensors.GetByDeviceIdAsync(deviceId.Value);

        var devices = await _devices.GetAllAsync();
        var deviceNames = devices.ToDictionary(d => d.Id, d => d.Name);

        var result = new List<SensorDto>();
        foreach (var s in sensors)
        {
            result.Add(await MapAsync(s, deviceNames.GetValueOrDefault(s.DeviceId)));
        }
        return Ok(result);
    }

    [HttpGet("{id:guid}/telemetry")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesView}")]
    public async Task<IActionResult> GetTelemetry(Guid id, [FromQuery] int count = 50, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var sensor = await _sensors.GetByIdAsync(id);
        if (sensor is null) return NotFound(new { message = "Sensor not found." });

        var driver = SensorDriverCatalog.GetByKey(sensor.SensorTypeKey);
        if (driver is null || sensor.TelemetryTableName is null)
            return Ok(Array.Empty<SensorReadingDto>());

        if (!await _telemetry.TableExistsAsync(sensor.TelemetryTableName))
            return Ok(Array.Empty<SensorReadingDto>());

        var columns = SensorColumnSet.For(driver, sensor.MetricFields);
        IReadOnlyList<SensorTelemetryReading> rows;
        if (from.HasValue || to.HasValue)
        {
            var fromUtc = from?.ToUniversalTime() ?? DateTime.MinValue;
            var toUtc = to?.ToUniversalTime() ?? DateTime.UtcNow.AddHours(1);
            if (toUtc <= fromUtc) return BadRequest(new { message = "'to' must be after 'from'." });
            rows = await _telemetry.GetRangeAsync(sensor.TelemetryTableName, driver, columns, fromUtc, toUtc, Math.Clamp(count, 1, 2000));
        }
        else
        {
            rows = await _telemetry.GetRecentAsync(sensor.TelemetryTableName, driver, columns, Math.Clamp(count, 1, 500));
        }

        return Ok(rows.Select(r => new SensorReadingDto(
            r.TimestampUtc, r.RawHexBuffer, r.PrimaryValue, r.SecondaryValue, r.ConnectionStatus, r.ErrorCode)));
    }

    [HttpGet("{id:guid}/telemetry/export-csv")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesView}")]
    public async Task<IActionResult> ExportTelemetryCsv(Guid id, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var sensor = await _sensors.GetByIdAsync(id);
        if (sensor is null) return NotFound(new { message = "Sensor not found." });

        var driver = SensorDriverCatalog.GetByKey(sensor.SensorTypeKey);
        if (driver is null || sensor.TelemetryTableName is null)
            return BadRequest(new { message = "Sensor has no telemetry driver/table." });

        if (!await _telemetry.TableExistsAsync(sensor.TelemetryTableName))
            return Ok(Array.Empty<byte>());

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        if (toUtc <= fromUtc) return BadRequest(new { message = "'to' must be after 'from'." });

        var columns = SensorColumnSet.For(driver, sensor.MetricFields);
        var rows = await _telemetry.GetRangeAsync(sensor.TelemetryTableName, driver, columns, fromUtc, toUtc, limit: 50000);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("TimestampUtc,Status,ErrorCode,Primary,Secondary");
        foreach (var r in rows.OrderBy(x => x.TimestampUtc))
        {
            sb.AppendLine($"{r.TimestampUtc:yyyy-MM-dd HH:mm:ss},{r.ConnectionStatus},{r.ErrorCode ?? ""},{r.PrimaryValue},{r.SecondaryValue}");
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"telemetry_{sensor.TelemetryTableName}_{fromUtc:yyyyMMdd-HHmm}_{toUtc:yyyyMMdd-HHmm}.csv");
    }

    [HttpGet("{id:guid}/telemetry/export-pdf")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesView}")]
    public async Task<IActionResult> ExportTelemetryPdf(Guid id, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var sensor = await _sensors.GetByIdAsync(id);
        if (sensor is null) return NotFound(new { message = "Sensor not found." });

        var driver = SensorDriverCatalog.GetByKey(sensor.SensorTypeKey);
        if (driver is null || sensor.TelemetryTableName is null)
            return BadRequest(new { message = "Sensor has no telemetry driver/table." });

        if (!await _telemetry.TableExistsAsync(sensor.TelemetryTableName))
            return Ok(Array.Empty<byte>());

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        if (toUtc <= fromUtc) return BadRequest(new { message = "'to' must be after 'from'." });

        var columns = SensorColumnSet.For(driver, sensor.MetricFields);
        var rows = (await _telemetry.GetRangeAsync(sensor.TelemetryTableName, driver, columns, fromUtc, toUtc, limit: 20000))
            .OrderBy(r => r.TimestampUtc)
            .ToList();

        var deviceName = (await _devices.GetByIdAsync(sensor.DeviceId))?.Name ?? "Unknown";
        var metricName = columns.HasPrimary ? columns.Primary! : columns.Secondary!;
        var unit = columns.HasPrimary ? driver.UnitPrimary : driver.UnitSecondary;

        double avg = rows.Count > 0 ? rows.Average(r => r.PrimaryValue) : 0;
        double minVal = rows.Count > 0 ? rows.Min(r => r.PrimaryValue) : 0;
        double maxVal = rows.Count > 0 ? rows.Max(r => r.PrimaryValue) : 0;
        int onlineCount = rows.Count(r => r.ConnectionStatus == 1);

        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(38);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("ALAM IOT Enterprises").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"SCADA TELEMETRY REPORT - {sensor.Name}").FontSize(12).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Item().Text($"Sensor:  {sensor.Name} ({sensor.UniqueSensorId})").FontSize(12).Bold();
                    col.Item().Text($"Gateway: {deviceName} | Driver: {driver.DisplayName} | Slave: {sensor.SlaveAddress}");
                    col.Item().Text($"Range:   {fromUtc:yyyy-MM-dd HH:mm} - {toUtc:yyyy-MM-dd HH:mm} (UTC)");
                    col.Item().Text($"Metric:  {metricName} ({unit}) | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                    col.Item().PaddingTop(12).Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Sample Count").FontColor(Colors.Grey.Darken1).FontSize(9);
                            c.Item().Text($"{rows.Count}").FontSize(16).Bold();
                        });
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Average").FontColor(Colors.Grey.Darken1).FontSize(9);
                            c.Item().Text($"{avg:0.000} {unit}").FontSize(16).Bold();
                        });
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Min / Max").FontColor(Colors.Grey.Darken1).FontSize(9);
                            c.Item().Text($"{minVal:0.000} / {maxVal:0.000} {unit}").FontSize(13).Bold();
                        });
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text("Online Samples").FontColor(Colors.Grey.Darken1).FontSize(9);
                            c.Item().Text($"{onlineCount} / {rows.Count}").FontSize(16).Bold();
                        });
                    });

                    col.Item().PaddingTop(14).Text($"Data samples ({rows.Count})").FontSize(12).Bold();
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Timestamp (UTC)").Bold();
                            h.Cell().Text($"{metricName} ({unit})").Bold();
                            h.Cell().Text("Status").Bold();
                        });
                        foreach (var r in rows.Take(250))
                        {
                            table.Cell().Text(r.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                            table.Cell().Text($"{r.PrimaryValue:0.000}");
                            table.Cell().Text(r.ConnectionStatus == 1 ? "ONLINE" : "OFFLINE");
                        }
                        if (rows.Count > 250)
                        {
                            table.Cell().ColumnSpan(3).Text($"... {rows.Count - 250} more samples not shown (full CSV export available)").FontColor(Colors.Grey.Darken1).FontSize(9);
                        }
                    });
                });

                page.Footer().AlignCenter().Text("ALAM IOT Enterprises - SCADA Telemetry Platform").FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf();

        return File(pdfBytes, "application/pdf", $"{sensor.UniqueSensorId}-{metricName}-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.pdf");
    }

    [HttpPost]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesAdd}")]
    public async Task<IActionResult> Create([FromBody] CreateSensorRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.SensorName))
            return BadRequest(new { message = "Sensor name is required." });
        if (string.IsNullOrWhiteSpace(req.SensorTypeKey))
            return BadRequest(new { message = "Sensor library selection is required." });

        var driver = SensorDriverCatalog.GetByKey(req.SensorTypeKey);
        if (driver is null)
            return BadRequest(new { message = $"No driver installed for '{req.SensorTypeKey}'." });

        var device = await _devices.GetByIdAsync(req.DeviceId);
        if (device is null)
            return BadRequest(new { message = "Selected device does not exist." });

        if (req.SlaveAddress is < 1 or > 247)
            return BadRequest(new { message = "Modbus slave address must be between 1 and 247." });
        if (req.PollIntervalSeconds is < 1 or > 3600)
            return BadRequest(new { message = "Poll interval must be between 1 and 3600 seconds." });

        var attached = await _sensors.GetByDeviceIdAsync(device.Id);
        if (attached.Count >= device.MaxSensorCapacity)
            return BadRequest(new { message = $"Maximum capacity reached for this device! ({attached.Count} / {device.MaxSensorCapacity} sensors)" });

        // A gateway address belongs to one configured meter, regardless of its driver.
        if (attached.Any(s => s.SlaveAddress == req.SlaveAddress))
            return BadRequest(new { message = $"Slave address {req.SlaveAddress} is already registered on this gateway. Change the new meter's hardware Slave ID before adding it." });

        var sensor = new Sensor
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            UniqueSensorId = "S-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Name = req.SensorName.Trim(),
            SensorType = driver.DisplayName,
            SensorTypeKey = driver.DriverKey,
            SlaveAddress = req.SlaveAddress,
            PollIntervalSeconds = req.PollIntervalSeconds,
            CalibrationMultiplier = req.CalibrationMultiplier is > 0 ? req.CalibrationMultiplier.Value : 1.0,
            TelemetryTableName = null, // bound right below, tied to the stable sensor id
            IsActive = true,
            IsOnline = false,
            CreatedAt = DateTime.UtcNow
        };

        // Bind the telemetry table to THIS sensor id (stable, unique per sensor).
        sensor.TelemetryTableName = TelemetryTableNaming.Build(driver.SimpleName, sensor.Id);

        // Parameter selection from the Gateway scan workflow: which meter fields
        // the operator wants this sensor to report (key -> unit). Stored as JSON
        // so the dashboard can highlight exactly the chosen metrics later.
        sensor.MetricFields = req.SelectedParameters is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(req.SelectedParameters)
            : null;
        sensor.Config = req.SensorConfig;

        var columns = SensorColumnSet.For(driver, sensor.MetricFields);
        if (!columns.HasAny)
        {
            return BadRequest(new { message = "Select at least one measurable parameter for this sensor (metadata-only parameters don't create telemetry columns)." });
        }

        await _sensors.AddAsync(sensor);
        await _telemetry.EnsureTelemetryTableAsync(sensor.TelemetryTableName, driver, columns);

        await AuditAsync("Sensor.Create", sensor, $"Added sensor '{sensor.Name}' ({sensor.UniqueSensorId}) -> table {sensor.TelemetryTableName} on device '{device.Name}'");

        return Ok(await MapAsync(sensor, device.Name));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesAdd}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSensorRequest req)
    {
        var sensor = await _sensors.GetByIdAsync(id);
        if (sensor is null) return NotFound(new { message = "Sensor not found." });

        // A sensor must always report at least one meter parameter - an empty,
        // pointless telemetry row is never created.
        if (req.SelectedParameters is { Count: 0 })
            return BadRequest(new { message = "Select at least one parameter for the sensor." });

        if (req.PollIntervalSeconds is { } poll && (poll is < 1 or > 3600))
            return BadRequest(new { message = "Poll interval must be between 1 and 3600 seconds." });

        if (!string.IsNullOrWhiteSpace(req.SensorName))
            sensor.Name = req.SensorName.Trim();
        if (req.PollIntervalSeconds is { } p2)
            sensor.PollIntervalSeconds = p2;
        if (req.CalibrationMultiplier is > 0)
            sensor.CalibrationMultiplier = req.CalibrationMultiplier.Value;

        // null => leave the stored parameters untouched; non-null => replace them.
        if (req.SelectedParameters is { } prm)
        {
            sensor.MetricFields = prm.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(prm)
                : null;
        }

        sensor.Config = req.SensorConfig ?? sensor.Config;

        // After the selection change, the sensor must still target at least one
        // measurable driver column (metadata-only parameters don't create them).
        var updatedDriver = SensorDriverCatalog.GetByKey(sensor.SensorTypeKey);
        var updatedColumns = updatedDriver is null ? null : SensorColumnSet.For(updatedDriver, sensor.MetricFields);
        if (updatedDriver is null || updatedColumns is null || !updatedColumns.HasAny)
        {
            return BadRequest(new { message = "Select at least one measurable parameter - metadata-only parameters don't create telemetry columns." });
        }

        // Make sure newly-activated columns exist before the response is mapped.
        if (!string.IsNullOrWhiteSpace(sensor.TelemetryTableName))
        {
            await _telemetry.EnsureTelemetryTableAsync(sensor.TelemetryTableName, updatedDriver, updatedColumns);
        }

        await _sensors.UpdateAsync(sensor);
        await AuditAsync("Sensor.Update", sensor, $"Updated sensor '{sensor.Name}' ({sensor.UniqueSensorId}): parameters={sensor.MetricFields}");

        return Ok(await MapAsync(sensor, (await _devices.GetByIdAsync(sensor.DeviceId))?.Name));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesDelete}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var sensor = await _sensors.GetByIdAsync(id);
        if (sensor is null) return NotFound(new { message = "Sensor not found." });

        if (!string.IsNullOrWhiteSpace(sensor.TelemetryTableName))
        {
            await _telemetry.DropTableIfExistsAsync(sensor.TelemetryTableName);
        }

        await _sensors.DeleteAsync(sensor);
        await AuditAsync("Sensor.Delete", sensor, $"Deleted sensor '{sensor.Name}' ({sensor.UniqueSensorId}) and dropped table {sensor.TelemetryTableName}");

        return Ok(new { message = "Sensor and telemetry table deleted successfully." });
    }

    // ----------------------------------------------------------------

    private async Task<SensorDto> MapAsync(Sensor s, string? deviceName)
    {
        var driver = SensorDriverCatalog.GetByKey(s.SensorTypeKey);

        double? latestPrimary = null;
        double? latestSecondary = null;
        DateTime? latestTs = null;

        if (driver != null && !string.IsNullOrWhiteSpace(s.TelemetryTableName) &&
            await _telemetry.TableExistsAsync(s.TelemetryTableName))
        {
            var columns = SensorColumnSet.For(driver, s.MetricFields);
            var latest = await _telemetry.GetLatestAsync(s.TelemetryTableName, driver, columns);
            if (latest != null)
            {
                // Only the operator-selected parameters are surfaced; an unselected
                // column is never read nor displayed ("--").
                latestPrimary = s.IsOnline && columns.HasPrimary ? latest.PrimaryValue : null;
                latestSecondary = s.IsOnline && columns.HasSecondary ? latest.SecondaryValue : null;
                latestTs = latest.TimestampUtc;
            }
        }

        return new SensorDto(
            s.Id,
            s.UniqueSensorId,
            s.Name,
            s.DeviceId,
            deviceName ?? "Unknown",
            s.SensorTypeKey,
            driver?.DisplayName ?? s.SensorTypeKey,
            s.SlaveAddress,
            s.PollIntervalSeconds,
            s.CalibrationMultiplier,
            s.TelemetryTableName,
            s.IsActive,
            s.IsOnline,
            latestPrimary,
            latestSecondary,
            driver?.UnitPrimary,
            driver?.UnitSecondary,
            latestTs,
            s.MetricFields,
            s.Config);
    }

    private async Task AuditAsync(string action, Sensor sensor, string details)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, action, "Sensor", sensor.Id.ToString(), details, HttpContext.Connection.RemoteIpAddress?.ToString());
    }
}
