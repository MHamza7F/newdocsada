using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using scada_demo_test.Application.DTOs;
using scada_demo_test.Application.Interfaces;

namespace scada_demo_test.API.Controllers;

// This is the REAL hardware entry point - it matches the EXACT JSON shape
// the Norvi firmware already builds and sends via send_data_to_backend()
// in sensor_data.cpp (the "json1" variable). No firmware code changes
// needed beyond pointing its target URL at this endpoint.
[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    private readonly IPushTelemetryIngestService _pushIngestService;
    public TelemetryController(IPushTelemetryIngestService pushIngestService) => _pushIngestService = pushIngestService;

    public class TankReadingItem
    {
        public string Metric { get; set; } = string.Empty;

        // JsonElement (not double) on purpose: the firmware sends garbage like "--"
        // on its failure branch, which would otherwise make the whole payload fail
        // JSON deserialization with a 400. Non-numeric values are skipped later.
        public JsonElement Value { get; set; }

        public string Unit { get; set; } = string.Empty;
    }

    public class TankReadingRequest
    {
        // tank_id = Modbus SLAVE address of the meter on the device's RS-485 bus.
        [JsonPropertyName("tank_id")]
        public int TankId { get; set; }

        [JsonPropertyName("tankReadings")]
        public List<TankReadingItem> TankReadings { get; set; } = new();

        // Optional explicit device attribution (the firmware does not send these -
        // the source IP is matched against Devices.IpAddress instead).
        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("externalId")]
        public string? ExternalId { get; set; }
    }

    // POST /api/telemetry/tank-reading
    // Body (sent as-is by the Norvi firmware, unchanged):
    // { "tank_id": 1, "tankReadings": [{"metric":"Flowrate","value":12.3,"unit":"Nm3h"}, ...] }
    //
    // The device is attributed by the TCP source IP -> Devices.IpAddress (fallback:
    // deviceId/externalId in the payload). Unknown devices/sensors are dropped with
    // a 200 response so the firmware never sees an error it cannot handle.
    [HttpPost("tank-reading")]
    public async Task<IActionResult> TankReading([FromBody] TankReadingRequest request)
    {
        var message = new PushTelemetryMessageDto
        {
            TankId = request.TankId,
            SourceIpAddress = NormalizeIp(HttpContext.Connection.RemoteIpAddress?.ToString()),
            DeviceExternalId = !string.IsNullOrWhiteSpace(request.DeviceId)
                ? request.DeviceId.Trim()
                : !string.IsNullOrWhiteSpace(request.ExternalId)
                    ? request.ExternalId.Trim()
                    : null,
            Readings = (request.TankReadings ?? new())
                .Select(r => new PushTelemetryReadingDto
                {
                    Metric = r.Metric,
                    Value = ToNullableDouble(r.Value),
                    Unit = r.Unit
                })
                .ToList()
        };

        var result = await _pushIngestService.IngestAsync(message);

        return result.Status switch
        {
            PushIngestStatus.Accepted => Ok(new
            {
                status = "accepted",
                deviceId = result.DeviceId,
                persistedReadings = result.PersistedReadings,
                skippedReadings = result.SkippedReadings
            }),
            PushIngestStatus.NoMatchingSensor => Ok(new
            {
                status = "unknown_sensor",
                deviceId = result.DeviceId
            }),
            _ => Ok(new { status = "unknown_device" })
        };
    }

    // Strip the IPv4-mapped IPv6 prefix ("::ffff:192.168.1.5" -> "192.168.1.5") so a
    // source IP matches how the operator typed it into the Devices table.
    private static string? NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        ip = ip.Trim();
        const string ipv4MappedPrefix = "::ffff:";
        return ip.StartsWith(ipv4MappedPrefix, StringComparison.OrdinalIgnoreCase)
            ? ip[ipv4MappedPrefix.Length..]
            : ip;
    }

    // Tolerate the firmware's failure branch, which sends "--" as a value. Numeric
    // JSON tokens (and numeric strings) pass through; everything else is skipped so
    // a flaky sensor can never 400 the whole push.
    private static double? ToNullableDouble(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out var d) ? d : null;
            case JsonValueKind.String:
                return double.TryParse(element.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            case JsonValueKind.True:
                return 1.0;
            case JsonValueKind.False:
                return 0.0;
            default:
                return null;
        }
    }
}