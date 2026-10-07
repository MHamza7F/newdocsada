using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using scada_demo_test.Application.DTOs;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Enums;
using scada_demo_test.Domain.Interfaces;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

// Device = physical gateway / controller (Norvi ESP32 or USR-W610). Sensors are the
// slave meters hanging off the gateway's RS-485 bus; they are managed via SensorsController.
[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceRepository _devices;
    private readonly ISensorRepository _sensors;
    private readonly ISensorTelemetryRepository _telemetry;
    private readonly AuditLogService _auditLogs;
    private readonly ISmartScanService _scanner;

    public DevicesController(
        IDeviceRepository devices,
        ISensorRepository sensors,
        ISensorTelemetryRepository telemetry,
        AuditLogService auditLogs,
        ISmartScanService scanner)
    {
        _devices = devices;
        _sensors = sensors;
        _telemetry = telemetry;
        _auditLogs = auditLogs;
        _scanner = scanner;
    }

    public record DeviceDto(
        Guid Id,
        string ExternalId,
        string Name,
        string HardwareType,
        string? IpAddress,
        int Port,
        int BaudRate,
        string Parity,
        int StopBits,
        int TimeoutMs,
        int MaxSensorCapacity,
        int SensorCount,
        bool IsOnline,
        DateTime? LastSeenAt,
        DateTime? CreatedAt,
        Guid? SiteId = null);

    public record CreateDeviceRequest(
        string Name,
        string HardwareType,
        string? IpAddress,
        int? Port,
        int? BaudRate,
        string? Parity,
        int? StopBits,
        int? TimeoutMs,
        Guid? SiteId);

    public record ScanBusRequest(int? ProbeTimeoutMs, int? StartAddress, int? EndAddress, int? Passes, bool? DeepDuplicateCheck);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _devices.GetAllAsync();
        var sensors = await _sensors.GetAllAsync();
        var counts = sensors.GroupBy(s => s.DeviceId).ToDictionary(g => g.Key, g => g.Count());

        return Ok(devices.Select(d => Map(d, counts.GetValueOrDefault(d.Id))));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOne(Guid id)
    {
        var device = await _devices.GetByIdAsync(id);
        if (device is null) return NotFound(new { message = "Device not found." });

        var count = await _sensors.CountByDeviceAsync(id);
        return Ok(Map(device, count));
    }

    // Smart & Fast RS-485 bus scan: pings 1..247 with a short per-probe timeout,
    // auto-skips slave addresses already registered to this gateway, and reports
    // each meter's type from its register signature. NOTHING is persisted here -
    // the UI maps a found meter into a Sensor (which provisions its telemetry
    // table) via SensorsController POST /api/sensors.
    [HttpPost("{id:guid}/scan")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesView}")]
    public async Task<IActionResult> ScanBus(Guid id, [FromBody] ScanBusRequest? req = null)
    {
        var device = await _devices.GetByIdAsync(id);
        if (device is null) return NotFound(new { message = "Device not found." });
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return BadRequest(new { message = "This gateway has no IP address configured; set it before scanning the bus." });

        var attached = await _sensors.GetByDeviceIdAsync(id);
        var skip = attached.Select(s => s.SlaveAddress).Distinct().ToList();
        var registeredNames = attached
            .GroupBy(s => s.SlaveAddress)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var result = await _scanner.ScanAsync(new SmartScanRequest(
            device.Id,
            device.Name,
            device.IpAddress,
            device.Port,
            device.TimeoutMs,
            req?.ProbeTimeoutMs ?? 200,
            skip,
            req?.StartAddress ?? 1,
            req?.EndAddress ?? 247,
            req?.Passes ?? 0,
            req?.DeepDuplicateCheck ?? true,
            registeredNames), HttpContext.RequestAborted);

        await AuditAsync("Device.Scan", device, $"Smart-scanned RS-485 bus of '{device.Name}' ({device.ExternalId}) @ {device.IpAddress}:{device.Port}: {result.RespondingSlaves} responder(s), {result.UnknownResponders} unidentified");

        return Ok(result);
    }

    // Live TCP reachability pre-check so the UI can show "Device offline" instantly
    // instead of starting a multi-minute sweep. Intentionally ignores the
    // worker-maintained IsOnline (which can be a watchdog interval stale).
    [HttpGet("{id:guid}/reachability")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesView}")]
    public async Task<IActionResult> Reachability(Guid id, CancellationToken ct = default)
    {
        var device = await _devices.GetByIdAsync(id);
        if (device is null) return NotFound(new { message = "Device not found." });

        // Norvi ESP32 push devices have no Modbus TCP server; reachability is
        // data-driven from incoming telemetry.
        if (string.IsNullOrWhiteSpace(device.IpAddress) || device.HardwareType == DeviceHardwareType.NorviESP32)
        {
            return Ok(new
            {
                reachable = device.IsOnline,
                online = device.IsOnline,
                latencyMs = (int?)null,
                message = "Push-driven device: online state comes from incoming telemetry, not a TCP probe."
            });
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1500));
            await client.ConnectAsync(device.IpAddress, device.Port, cts.Token);
            sw.Stop();
            return Ok(new
            {
                reachable = true,
                online = device.IsOnline,
                latencyMs = (int)sw.ElapsedMilliseconds,
                message = $"Gateway {device.IpAddress}:{device.Port} accepted the connection."
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return Ok(new
            {
                reachable = false,
                online = device.IsOnline,
                latencyMs = (int)sw.ElapsedMilliseconds,
                message = $"Gateway {device.IpAddress}:{device.Port} is offline (no response within 1.5 s)."
            });
        }
        catch (Exception)
        {
            sw.Stop();
            return Ok(new
            {
                reachable = false,
                online = device.IsOnline,
                latencyMs = (int)sw.ElapsedMilliseconds,
                message = $"Gateway {device.IpAddress}:{device.Port} is offline (connection refused)."
            });
        }
    }

    [HttpPost]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesAdd}")]
    public async Task<IActionResult> CreateDevice([FromBody] CreateDeviceRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Device name is required." });
        if (string.IsNullOrWhiteSpace(dto.IpAddress))
            return BadRequest(new { message = "IP address / host is required." });

        if (!TryParseHardwareType(dto.HardwareType, out var hardwareType))
            return BadRequest(new { message = $"Unsupported hardware type '{dto.HardwareType}'. Supported: USR-W610, Norvi (ESP32)." });

        if (dto.Port is < 1 or > 65535)
            return BadRequest(new { message = "Port must be between 1 and 65535." });

        var ip = CleanIpAddress(dto.IpAddress);
        if (string.IsNullOrWhiteSpace(ip))
            return BadRequest(new { message = "Valid IP address / host is required." });
        var existingIp = (await _devices.GetAllAsync()).FirstOrDefault(d =>
            string.Equals(d.IpAddress?.Trim(), ip, StringComparison.OrdinalIgnoreCase));
        if (existingIp != null)
            return Conflict(new { message = $"IP address '{ip}' is already registered to gateway '{existingIp.Name}' ({existingIp.ExternalId}). Each gateway needs a unique IP address." });

        var device = new Device
        {
            Id = Guid.NewGuid(),
            ExternalId = "DEV-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Name = dto.Name.Trim(),
            DeviceType = DeviceType.Gateway,
            Protocol = ProtocolType.ModbusTcp,
            Status = DeviceStatus.Offline,
            HardwareType = hardwareType,
            IpAddress = ip,
            Port = dto.Port ?? 502,
            BaudRate = dto.BaudRate ?? 9600,
            Parity = ParseParity(dto.Parity),
            StopBits = dto.StopBits ?? 1,
            TimeoutMs = dto.TimeoutMs ?? 2000,
            MaxSensorCapacity = 25, // hard platform limit: 25 sensors per physical device
            IsOnline = false,
            SiteId = dto.SiteId,
            CreatedAt = DateTime.UtcNow
        };

        await _devices.AddAsync(device);
        await AuditAsync("Device.Create", device, $"Added gateway '{device.Name}' ({device.ExternalId}) as {device.HardwareType} @ {device.IpAddress}:{device.Port}");

        var count = await _sensors.CountByDeviceAsync(device.Id);
        return CreatedAtAction(nameof(GetOne), new { id = device.Id }, Map(device, count));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesEdit}")]
    public async Task<IActionResult> UpdateDevice(Guid id, [FromBody] CreateDeviceRequest dto)
    {
        var device = await _devices.GetByIdAsync(id);
        if (device is null) return NotFound(new { message = "Device not found." });

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Device name is required." });
        if (string.IsNullOrWhiteSpace(dto.IpAddress))
            return BadRequest(new { message = "IP address / host is required." });
        if (dto.Port is < 1 or > 65535)
            return BadRequest(new { message = "Port must be between 1 and 65535." });

        if (!TryParseHardwareType(dto.HardwareType, out var hardwareType))
            return BadRequest(new { message = $"Unsupported hardware type '{dto.HardwareType}'. Supported: USR-W610, Norvi (ESP32)." });

        var updateIp = CleanIpAddress(dto.IpAddress);
        if (string.IsNullOrWhiteSpace(updateIp))
            return BadRequest(new { message = "Valid IP address / host is required." });
        var ipOwner = (await _devices.GetAllAsync()).FirstOrDefault(d =>
            d.Id != id && string.Equals(d.IpAddress?.Trim(), updateIp, StringComparison.OrdinalIgnoreCase));
        if (ipOwner != null)
            return Conflict(new { message = $"IP address '{updateIp}' is already registered to gateway '{ipOwner.Name}' ({ipOwner.ExternalId}). Each gateway needs a unique IP address." });

        device.Name = dto.Name.Trim();
        device.HardwareType = hardwareType;
        device.IpAddress = updateIp;
        device.Port = dto.Port ?? 502;
        device.BaudRate = dto.BaudRate ?? 9600;
        device.Parity = ParseParity(dto.Parity);
        device.StopBits = dto.StopBits ?? 1;
        device.TimeoutMs = dto.TimeoutMs ?? 2000;
        device.SiteId = dto.SiteId;
        device.Protocol = ProtocolType.ModbusTcp;

        await _devices.UpdateAsync(device);
        await AuditAsync("Device.Update", device, $"Updated gateway '{device.Name}' ({device.ExternalId})");

        var count = await _sensors.CountByDeviceAsync(device.Id);
        return Ok(Map(device, count));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesDelete}")]
    public async Task<IActionResult> DeleteDevice(Guid id)
    {
        var device = await _devices.GetByIdAsync(id);
        if (device is null) return NotFound(new { message = "Device not found." });

        // Drop all dynamic telemetry tables associated with this device's sensors
        var sensors = await _sensors.GetByDeviceIdAsync(id);
        foreach (var s in sensors)
        {
            if (!string.IsNullOrWhiteSpace(s.TelemetryTableName))
            {
                await _telemetry.DropTableIfExistsAsync(s.TelemetryTableName);
            }
        }

        await _devices.DeleteAsync(device);
        await AuditAsync("Device.Delete", device, $"Deleted gateway '{device.Name}' ({device.ExternalId}) and cleaned up its sensor telemetry tables");

        return Ok(new { message = "Device and associated telemetry tables deleted successfully." });
    }

    // ----------------------------------------------------------------

    private static DeviceDto Map(Device d, int sensorCount) => new(
        d.Id,
        d.ExternalId,
        d.Name,
        HardwareTypeName(d.HardwareType),
        d.IpAddress,
        d.Port,
        d.BaudRate,
        d.Parity.ToString(),
        d.StopBits,
        d.TimeoutMs,
        d.MaxSensorCapacity,
        sensorCount,
        d.IsOnline,
        d.LastSeenAt,
        d.CreatedAt,
        d.SiteId);

    private static string HardwareTypeName(DeviceHardwareType type) => type switch
    {
        DeviceHardwareType.UsrW610 => "USR-W610",
        _ => "Norvi (ESP32)"
    };

    private static bool TryParseHardwareType(string? raw, out DeviceHardwareType type)
    {
        type = DeviceHardwareType.NorviESP32;

        if (string.IsNullOrWhiteSpace(raw)) return false;

        var norm = raw.Trim().ToLowerInvariant();
        if (norm is "norvi" or "norvi-esp32" or "norviesp32" or "esp32" or "norvi (esp32)")
        {
            type = DeviceHardwareType.NorviESP32;
            return true;
        }
        if (norm is "usr-w610" or "usw610" or "usr w610")
        {
            type = DeviceHardwareType.UsrW610;
            return true;
        }
        return Enum.TryParse(raw, ignoreCase: true, out type) &&
               Enum.IsDefined(type);
    }

    private static string CleanIpAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim();
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            s = s["http://".Length..];
        else if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            s = s["https://".Length..];

        // Trim trailing slashes or URL paths
        var slashIdx = s.IndexOf('/');
        if (slashIdx >= 0) s = s[..slashIdx];

        // Trim port if provided (e.g. 192.168.100.56:502)
        var colonIdx = s.IndexOf(':');
        if (colonIdx >= 0 && !s.Contains("::")) // exclude IPv6
            s = s[..colonIdx];

        return s.Trim();
    }

    private static ModbusParity ParseParity(string? parity) => (parity ?? "").Trim().ToLowerInvariant() switch
    {
        "even" => ModbusParity.Even,
        "odd" => ModbusParity.Odd,
        _ => ModbusParity.None
    };

    private async Task AuditAsync(string action, Device device, string details)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, action, "Device", device.Id.ToString(), details, HttpContext.Connection.RemoteIpAddress?.ToString());
    }
}
