using System.Collections.Concurrent;

namespace scada_demo_test.Infrastructure.Modbus;

/// <summary>
/// Coordinates bus access between the background polling hosted service and the
/// interactive Modbus bus scanner. Prevents TCP socket collisions on gateways
/// like USR-W610 that support only a single active TCP client.
/// </summary>
public static class ModbusScanCoordinator
{
    private static readonly ConcurrentDictionary<Guid, int> ActiveScans = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BusLocks = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsScanning(Guid deviceId) => ActiveScans.GetValueOrDefault(deviceId) > 0;

    public static async Task<IDisposable> AcquireScanAsync(Guid deviceId, string host, int port, CancellationToken ct)
    {
        ActiveScans.AddOrUpdate(deviceId, 1, (_, count) => count + 1);
        try
        {
            var bus = await AcquireBusAsync(host, port, ct);
            return new Lease(() =>
            {
                bus.Dispose();
                ActiveScans.AddOrUpdate(deviceId, 0, (_, count) => Math.Max(0, count - 1));
            });
        }
        catch
        {
            ActiveScans.AddOrUpdate(deviceId, 0, (_, count) => Math.Max(0, count - 1));
            throw;
        }
    }

    public static async Task<IDisposable> AcquireBusAsync(string host, int port, CancellationToken ct)
    {
        var gate = BusLocks.GetOrAdd($"{host.Trim()}:{port}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Lease(() => gate.Release());
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
