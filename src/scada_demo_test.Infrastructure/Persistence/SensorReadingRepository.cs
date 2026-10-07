using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Interfaces;
using scada_demo_test.Infrastructure.Resilience;

namespace scada_demo_test.Infrastructure.Persistence;

public class SensorReadingRepository : ISensorReadingRepository
{
    private readonly MyDbContextDxy _db;
    public SensorReadingRepository(MyDbContextDxy db) => _db = db;

    public async Task AddAsync(SensorReading reading, CancellationToken ct = default)
    {
        _db.SensorReadings.Add(reading);
        // This runs on every single reading (potentially every second, per device,
        // per metric) so a one-off transient SQL error here must never crash the
        // ingest pipeline or drop the reading - retry with backoff instead.
        await EfResilience.RunAsync(() => _db.SaveChangesAsync(ct));
    }

    public async Task<IReadOnlyList<SensorReading>> GetRecentAsync(
        Guid deviceId, string metric, int count = 100, CancellationToken ct = default) =>
        await _db.SensorReadings
            .Where(r => r.DeviceId == deviceId && r.Metric == metric)
            .OrderByDescending(r => r.Timestamp)
            .Take(count)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SensorReading>> GetRangeAsync(
        Guid deviceId, string metric, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.SensorReadings
            .Where(r => r.DeviceId == deviceId && r.Metric == metric
                        && r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, SensorReading>> GetLatestForDevicesAsync(
        IReadOnlyCollection<Guid> deviceIds, string metric, CancellationToken ct = default)
    {
        if (deviceIds.Count == 0) return new();

        // Single grouped query (translates to a windowed/GROUP BY SQL statement)
        // instead of one query per device - this is what made the snapshot slow.
        var latest = await _db.SensorReadings
            .Where(r => deviceIds.Contains(r.DeviceId) && r.Metric == metric)
            .GroupBy(r => r.DeviceId)
            .Select(g => g.OrderByDescending(r => r.Timestamp).First())
            .AsNoTracking()
            .ToListAsync(ct);

        return latest.ToDictionary(r => r.DeviceId, r => r);
    }
}
