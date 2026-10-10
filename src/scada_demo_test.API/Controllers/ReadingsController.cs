using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/readings")]
public sealed class ReadingsController : ControllerBase
{
    private readonly MyDbContextDxy _db;

    public ReadingsController(MyDbContextDxy db) => _db = db;

    public record ReadingPointDto(DateTime Timestamp, double Value, string? Unit);
    public record LatestSnapshotDto(Guid Id, string ExternalId, string Name, string DeviceType, string Status,
        int? ModbusSlaveId, string? IpAddress, DateTime? LastSeenAt,
        double FlowRate, string FlowUnit, double Totalizer, string TotalizerUnit);

    [HttpGet("latest")]
    [Authorize(Policy = $"Action:{AppPermissions.DevicesView}")]
    public async Task<ActionResult<IEnumerable<LatestSnapshotDto>>> GetLatest(CancellationToken cancellationToken)
    {
        // SensorReadings is the local normalized/legacy compatibility stream populated by
        // the telemetry ingest path; per-sensor driver tables remain available for raw
        // diagnostics, while this endpoint preserves the Web chart contract. Correlated
        // TOP(1) subqueries keep this bounded to two readings per device and
        // let the (DeviceId, Metric, Timestamp) index do the work. LastSeenAt is returned
        // as stored: an old/offline device must not appear freshly online.
        var snapshots = await _db.Devices.AsNoTracking().OrderBy(device => device.ExternalId).Select(device => new LatestSnapshotDto(
            device.Id,
            device.ExternalId,
            device.Name,
            device.DeviceType.ToString(),
            device.Status.ToString(),
            device.ModbusSlaveId,
            device.IpAddress,
            device.LastSeenAt,
            device.Readings.Where(r => r.Metric == "FlowRate").OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.Id)
                .Select(r => (double?)r.Value).FirstOrDefault() ?? 0,
            device.Readings.Where(r => r.Metric == "FlowRate").OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.Id)
                .Select(r => r.Unit).FirstOrDefault() ?? "",
            device.Readings.Where(r => r.Metric == "Totalizer").OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.Id)
                .Select(r => (double?)r.Value).FirstOrDefault() ?? 0,
            device.Readings.Where(r => r.Metric == "Totalizer").OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.Id)
                .Select(r => r.Unit).FirstOrDefault() ?? ""
        )).ToListAsync(cancellationToken);

        return Ok(snapshots);
    }

    [HttpGet("{externalId}")]
    [Authorize(Policy = $"Action:{AppPermissions.ReportsView}")]
    public async Task<ActionResult<IEnumerable<ReadingPointDto>>> GetReadings(
        string externalId, [FromQuery] string metric = "FlowRate", [FromQuery] int count = 30,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId) || externalId.Trim().Length > 200)
            return BadRequest(new { message = "A valid externalId is required." });
        if (string.IsNullOrWhiteSpace(metric) || metric.Trim().Length > 100)
            return BadRequest(new { message = "A valid metric is required." });
        if (count is < 1 or > 500)
            return BadRequest(new { message = "count must be between 1 and 500." });

        var deviceId = await _db.Devices.AsNoTracking()
            .Where(d => d.ExternalId == externalId.Trim())
            .Select(d => (Guid?)d.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (deviceId is null) return NotFound(new { message = "Device not found." });

        var rows = await _db.SensorReadings.AsNoTracking()
            .Where(r => r.DeviceId == deviceId.Value && r.Metric == metric.Trim())
            .OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.Id)
            .Take(count)
            .Select(r => new ReadingPointDto(r.Timestamp, r.Value, r.Unit))
            .ToListAsync(cancellationToken);

        // The legacy client expects chart points oldest-to-newest, while the query is
        // newest-first so Take(count) remains efficient and bounded.
        rows.Reverse();
        return Ok(rows);
    }
}
