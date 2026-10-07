using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Interfaces;

namespace scada_demo_test.Infrastructure.Persistence;

public class SensorRepository : ISensorRepository
{
    private readonly MyDbContextDxy _db;
    public SensorRepository(MyDbContextDxy db) => _db = db;

    public Task<Sensor?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Sensors.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Sensor>> GetByDeviceIdAsync(Guid deviceId, CancellationToken ct = default) =>
        await _db.Sensors
            .Where(s => s.DeviceId == deviceId)
            .OrderBy(s => s.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Sensor>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Sensors.AsNoTracking().ToListAsync(ct);

    public Task<int> CountByDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        _db.Sensors.CountAsync(s => s.DeviceId == deviceId, ct);

    public async Task AddAsync(Sensor sensor, CancellationToken ct = default)
    {
        _db.Sensors.Add(sensor);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Sensor sensor, CancellationToken ct = default)
    {
        _db.Sensors.Update(sensor);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Sensor sensor, CancellationToken ct = default)
    {
        _db.Sensors.Remove(sensor);
        await _db.SaveChangesAsync(ct);
    }
}