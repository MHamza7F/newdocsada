using scada_demo_test.Domain.Entities;

namespace scada_demo_test.Domain.Interfaces;

public interface ISensorReadingRepository
{
    Task AddAsync(SensorReading reading, CancellationToken ct = default);

    Task<IReadOnlyList<SensorReading>> GetRecentAsync(
        Guid deviceId, string metric, int count = 100, CancellationToken ct = default);

    Task<IReadOnlyList<SensorReading>> GetRangeAsync(
        Guid deviceId, string metric, DateTime from, DateTime to, CancellationToken ct = default);

    // One grouped query for "latest value per device" instead of N+1 round trips -
    // powers the dashboard snapshot endpoint so cards never have to render empty.
    Task<Dictionary<Guid, SensorReading>> GetLatestForDevicesAsync(
        IReadOnlyCollection<Guid> deviceIds, string metric, CancellationToken ct = default);
}
