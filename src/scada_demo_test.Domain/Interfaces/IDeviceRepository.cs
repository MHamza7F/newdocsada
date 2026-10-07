using scada_demo_test.Domain.Entities;

namespace scada_demo_test.Domain.Interfaces;

public interface IDeviceRepository
{
    Task<Device?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<Device?> GetByIpAddressAsync(string ipAddress, CancellationToken ct = default);
    Task<Device?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Device device, CancellationToken ct = default);
    Task UpdateAsync(Device device, CancellationToken ct = default);
    Task DeleteAsync(Device device, CancellationToken ct = default);
}
