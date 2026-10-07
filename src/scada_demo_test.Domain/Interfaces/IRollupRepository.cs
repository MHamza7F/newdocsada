using scada_demo_test.Domain.Entities;

namespace scada_demo_test.Domain.Interfaces;

// Everything the compression pipeline and the Reports/Charts read-path need,
// split by tier. Kept as plain, boring CRUD-shaped methods (same style as
// ISensorReadingRepository) so RollupCompressionService in the Application layer
// never has to talk EF/SQL directly.
public interface IRollupRepository
{
    // ---- Raw (Tier 1) ----
    Task<IReadOnlyList<SensorReading>> GetRawOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
    Task RemoveRawAsync(IEnumerable<long> ids, CancellationToken ct = default);

    // ---- Hourly (Tier 2) ----
    Task<bool> HourlyExistsAsync(Guid deviceId, string metric, DateTime periodStart, CancellationToken ct = default);
    Task AddHourlyRollupsAsync(IEnumerable<HourlyRollup> rollups, CancellationToken ct = default);
    Task<IReadOnlyList<HourlyRollup>> GetHourlyOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
    Task RemoveHourlyAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<HourlyRollup>> GetHourlySeriesAsync(Guid deviceId, string metric, DateTime from, DateTime to, CancellationToken ct = default);

    // ---- Daily (Tier 3) ----
    Task<bool> DailyExistsAsync(Guid deviceId, string metric, DateTime periodStart, CancellationToken ct = default);
    Task AddDailyRollupsAsync(IEnumerable<DailyRollup> rollups, CancellationToken ct = default);
    Task<IReadOnlyList<DailyRollup>> GetDailyOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
    Task RemoveDailyAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<DailyRollup>> GetDailySeriesAsync(Guid deviceId, string metric, DateTime from, DateTime to, CancellationToken ct = default);

    // ---- Monthly (Tier 4) ----
    Task<bool> MonthlyExistsAsync(Guid deviceId, string metric, DateTime periodStart, CancellationToken ct = default);
    Task AddMonthlyRollupsAsync(IEnumerable<MonthlyRollup> rollups, CancellationToken ct = default);
    Task<IReadOnlyList<MonthlyRollup>> GetMonthlySeriesAsync(Guid deviceId, string metric, DateTime from, DateTime to, CancellationToken ct = default);
}
