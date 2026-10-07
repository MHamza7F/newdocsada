using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExportController : ControllerBase
{
    private readonly MyDbContextDxy _db;

    public ExportController(MyDbContextDxy db)
    {
        _db = db;
    }

    [HttpGet("readings")]
    public async Task<IActionResult> ExportReadings(
        [FromQuery] string? deviceId,
        [FromQuery] string? metric,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "json")
    {
        var fromUtc = from?.ToUniversalTime() ?? DateTime.UtcNow.AddHours(-6);
        var toUtc = to?.ToUniversalTime() ?? DateTime.UtcNow;

        var query = _db.SensorReadings.AsNoTracking()
            .Where(r => r.Timestamp >= fromUtc && r.Timestamp <= toUtc);

        if (!string.IsNullOrEmpty(deviceId) && Guid.TryParse(deviceId, out var dId))
            query = query.Where(r => r.DeviceId == dId);

        if (!string.IsNullOrEmpty(metric))
            query = query.Where(r => r.Metric == metric);

        var data = await query.OrderBy(r => r.Timestamp).Take(5000).ToListAsync();

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.AppendLine("ReadingId,DeviceId,Metric,Value,Unit,TimestampUtc");
            foreach (var r in data)
            {
                sb.AppendLine($"{r.Id},{r.DeviceId},{r.Metric},{r.Value},{r.Unit},{r.Timestamp:yyyy-MM-dd HH:mm:ss}");
            }
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"scada_readings_{DateTime.UtcNow:yyyyMMddHHmm}.csv");
        }

        return Ok(data);
    }

    [HttpGet("rollups")]
    public async Task<IActionResult> ExportRollups(
        [FromQuery] string tier = "daily",
        [FromQuery] string? metric = null,
        [FromQuery] string format = "json")
    {
        if (tier.ToLowerInvariant() == "hourly")
        {
            var query = _db.HourlyRollups.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(metric)) query = query.Where(h => h.Metric == metric);
            var data = await query.OrderByDescending(h => h.PeriodStart).Take(1000).ToListAsync();

            if (format.ToLowerInvariant() == "csv")
            {
                var sb = new StringBuilder();
                sb.AppendLine("DeviceId,Metric,PeriodStart,PeriodEnd,AggregatedValue,Unit,SampleCount,Min,Max");
                foreach (var r in data)
                    sb.AppendLine($"{r.DeviceId},{r.Metric},{r.PeriodStart:yyyy-MM-dd HH:mm},{r.PeriodEnd:yyyy-MM-dd HH:mm},{r.AggregatedValue},{r.Unit},{r.SampleCount},{r.MinValue},{r.MaxValue}");
                return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"hourly_rollups_{DateTime.UtcNow:yyyyMMdd}.csv");
            }
            return Ok(data);
        }
        else
        {
            var query = _db.DailyRollups.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(metric)) query = query.Where(d => d.Metric == metric);
            var data = await query.OrderByDescending(d => d.PeriodStart).Take(1000).ToListAsync();

            if (format.ToLowerInvariant() == "csv")
            {
                var sb = new StringBuilder();
                sb.AppendLine("DeviceId,Metric,PeriodStart,PeriodEnd,AggregatedValue,Unit,SampleCount,Min,Max");
                foreach (var r in data)
                    sb.AppendLine($"{r.DeviceId},{r.Metric},{r.PeriodStart:yyyy-MM-dd},{r.PeriodEnd:yyyy-MM-dd},{r.AggregatedValue},{r.Unit},{r.SampleCount},{r.MinValue},{r.MaxValue}");
                return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"daily_rollups_{DateTime.UtcNow:yyyyMMdd}.csv");
            }
            return Ok(data);
        }
    }
}
