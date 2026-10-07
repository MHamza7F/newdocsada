using scada_demo_test.Domain.Entities;

namespace scada_demo_test.Domain.Interfaces;

public interface ISensorRepository
{
    Task<Sensor?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Sensor>> GetByDeviceIdAsync(Guid deviceId, CancellationToken ct = default);
    Task<IReadOnlyList<Sensor>> GetAllAsync(CancellationToken ct = default);
    Task<int> CountByDeviceAsync(Guid deviceId, CancellationToken ct = default);

    Task AddAsync(Sensor sensor, CancellationToken ct = default);
    Task UpdateAsync(Sensor sensor, CancellationToken ct = default);
    Task DeleteAsync(Sensor sensor, CancellationToken ct = default);
}