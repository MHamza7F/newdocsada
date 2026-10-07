using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Interfaces;
using scada_demo_test.Infrastructure.Resilience;

namespace scada_demo_test.Infrastructure.Persistence;

public class RollupRepository : IRollupRepository
{
    private readonly MyDbContextDxy _db;
    public RollupRepository(MyDbContextDxy db) => _db = db;

    // ---- Raw ----
    public async Task<IReadOnlyList<SensorReading>> GetRawOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        await _db.SensorReadings
            .Where(r => r.Timestamp < cutoffUtc)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task RemoveRawAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        var idList = ids as IReadOnlyCollection<long> ?? ids.ToList();
        if (idList.Count == 0) return;
        // Batched delete keeps the parameter list (and the transaction) small even
        // when a compression cycle clears tens of thousands of raw rows at once.
        foreach (var chunk in idList.Chunk(1000))
        {
            var rows = await _db.SensorReadings.Where(r => chunk.Contains(r.Id)).ToListAsync(ct);
            _db.SensorReadings.RemoveRange(rows);
            await EfResilience.RunAsync(() => _db.SaveChangesAsync(ct));
        }
    }

    // ---- Hourly ----
    public Task<bool> HourlyExistsAsync(Guid deviceId, string metric, DateTime periodStart, CancellationToken ct = default) =>
        _db.HourlyRollups.AnyAsync(h => h.DeviceId == deviceId && h.Metric == metric && h.PeriodStart == periodStart, ct);

    public async Task AddHourlyRollupsAsync(IEnumerable<HourlyRollup> rollups, CancellationToken ct = default)
    {
        _db.HourlyRollups.AddRange(rollups);
        await EfResilience.RunAsync(() => _db.SaveChangesAsync(ct));
    }

    public async Task<IReadOnlyList<HourlyRollup>> GetHourlyOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        await _db.HourlyRollups.Where(h => h.PeriodEnd <= cutoffUtc).AsNoTracking().ToListAsync(ct);

    public async Task RemoveHourlyAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids as IReadOnlyCollection<Guid> ?? ids.ToList();
        if (idList.Count == 0) return;
        var rows = await _db.HourlyRollups.Where(h => idList.Contains(h.Id)).ToListAsync(ct);
        _db.HourlyRollups.RemoveRange(rows);
        await EfResilience.RunAsync(() => _db.SaveChangesAsync(ct));
    }

    public async Task<IReadOnlyList<HourlyRollup>> GetHourlySeriesAsync(Guid deviceId, string metric, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.HourlyRollups
            .Where(h => h.DeviceId == deviceId && h.Metric == metric && h.PeriodStart >= from && h.PeriodStart < to)
            .OrderBy(h => h.PeriodStart)
            .AsNoTracking()
            .ToListAsync(ct);

    // ---- Daily ----
    public Task<bool> DailyExistsAsync(Guid deviceId, string metric, DateTime periodStart, CancellationToken ct = default) =>
        _db.DailyRollups.AnyAsync(d => d.DeviceId == deviceId && d.Metric == metric && d.PeriodStart == periodStart, ct);

    public async Task AddDailyRollupsAsync(IEnumerable<DailyRollup> rollups, CancellationToken ct = default)
    {
        _db.DailyRollups.AddRange(rollups);
        await EfResilience.RunAsync(() => _db.SaveChangesAsync(ct));
    }

    public async Task<IReadOnlyList<DailyRollup>> GetDailyOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        await _db.DailyRollups.Where(d => d.PeriodEnd <= cutoffUtc).AsNoTracking().ToListAsync(ct);

    public async Task RemoveDailyAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids as IReadOnlyCollection<Guid> ?? ids.ToList();
        if (idList.Count == 0) return;
        var rows = await _db.DailyRollups.Where(d => idList.Contains(d.Id)).ToListAsync(ct);
        _db.DailyRollups.RemoveRange(rows);
        await EfResilience.RunAsync(() => _db.SaveChangesAsync(ct));
    }

    public async Task<IReadOnlyList<DailyRollup>> GetDailySeriesAsync(Guid deviceId, string metric, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.DailyRollups
            .Where(d => d.DeviceId == deviceId && d.Metric == metric && d.PeriodStart >= from && d.PeriodStart < to)
            .OrderBy(d => d.PeriodStart)
            .AsNoTracking()
            .ToListAsync(ct);

    // ---- Monthly ----
    public Task<bool> MonthlyExistsAsync(Guid deviceId, string metric, DateTime periodStart, CancellationToken ct = default) =>
        _db.MonthlyRollups.AnyAsync(m => m.DeviceId == deviceId && m.Metric == metric && m.PeriodStart == periodStart, ct);

    public async Task AddMonthlyRollupsAsync(IEnumerable<MonthlyRollup> rollups, CancellationToken ct = default)
    {
        _db.MonthlyRollups.AddRange(rollups);
        await EfResilience.RunAsync(() => _db.SaveChangesAsync(ct));
    }

    public async Task<IReadOnlyList<MonthlyRollup>> GetMonthlySeriesAsync(Guid deviceId, string metric, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.MonthlyRollups
            .Where(m => m.DeviceId == deviceId && m.Metric == metric && m.PeriodStart >= from && m.PeriodStart < to)
            .OrderBy(m => m.PeriodStart)
            .AsNoTracking()
            .ToListAsync(ct);
}
