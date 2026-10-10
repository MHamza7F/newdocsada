using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Drivers;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Enums;
using scada_demo_test.Domain.Interfaces;
using scada_demo_test.Infrastructure.Modbus;
using scada_demo_test.Infrastructure.RealTime;
using Xunit;

namespace ScadaEngine.Tests;

public class ModbusReliabilityTests
{
    [Fact]
    public async Task CallerCancellationInvalidatesAnInFlightSession()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var gateway = new FakeGateway(_ => { received.TrySetResult(); return null; });
        using var session = await new ModbusTcpMaster().OpenAsync("127.0.0.1", gateway.Port, 3000, default);
        using var cancel = new CancellationTokenSource();
        var read = session.ReadHoldingRegistersAsync(1, 0, 1, cancel.Token);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReadHoldingRegistersAsync(2, 0, 1, default, 50));
        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task PreCancelledReadDoesNotKillTheSession()
    {
        await using var gateway = new FakeGateway(r => FakeGateway.DataFrame(r, new byte[] { 0, 42 }));
        using var session = await new ModbusTcpMaster().OpenAsync("127.0.0.1", gateway.Port, 3000, default);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ReadHoldingRegistersAsync(1, 0, 1, cancel.Token));
        Assert.Equal(new byte[] { 0, 42 }, await session.ReadHoldingRegistersAsync(1, 0, 1, default));
    }

    [Fact]
    public async Task InvalidHeaderInvalidatesSessionWithUnreadPdu()
    {
        await using var gateway = new FakeGateway(r =>
        {
            var frame = FakeGateway.DataFrame(r, new byte[] { 0, 42 });
            frame[2] = 1;
            return frame;
        });
        using var session = await new ModbusTcpMaster().OpenAsync("127.0.0.1", gateway.Port, 3000, default);
        var error = await Assert.ThrowsAsync<ModbusException>(() => session.ReadHoldingRegistersAsync(1, 0, 1, default));
        Assert.True(error.IsProtocolError);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReadHoldingRegistersAsync(1, 0, 1, default));
    }

    [Fact]
    public async Task CompleteModbusExceptionDoesNotPoisonTheSession()
    {
        await using var gateway = new FakeGateway(r => r[6] == 1
            ? FakeGateway.ExceptionFrame(r, 2)
            : FakeGateway.DataFrame(r, new byte[] { 0, 42 }));
        using var session = await new ModbusTcpMaster().OpenAsync("127.0.0.1", gateway.Port, 3000, default);
        await Assert.ThrowsAsync<ModbusException>(() => session.ReadHoldingRegistersAsync(1, 0, 1, default));
        Assert.Equal(new byte[] { 0, 42 }, await session.ReadHoldingRegistersAsync(2, 0, 1, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidTrailingReplyIsNotSwallowed(bool truncated)
    {
        await using var gateway = new FakeGateway(r =>
        {
            var frame = FakeGateway.DataFrame(r, new byte[] { 0, 42 });
            var trailing = (byte[])frame.Clone();
            trailing[2] = 1;
            return frame.Concat(truncated ? trailing.Take(1) : trailing).ToArray();
        });
        using var session = await new ModbusTcpMaster().OpenAsync("127.0.0.1", gateway.Port, 3000, default);
        // The trailing frame may become available at the end of this read or at
        // the start of the next one. Either way it must fail and kill the session.
        var error = await Record.ExceptionAsync(async () =>
        {
            await session.ReadHoldingRegistersAsync(1, 0, 1, default, 250);
            await session.ReadHoldingRegistersAsync(1, 0, 1, default, 250);
        });
        Assert.NotNull(error);
        Assert.True(error is TimeoutException || error is ModbusException { IsProtocolError: true });
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReadHoldingRegistersAsync(1, 0, 1, default));
    }

    [Fact]
    public async Task ReachableStaleGatewayIsProbedBeforePublishingOffline()
    {
        await using var gateway = new FakeGateway(_ => null);
        var device = Gateway(gateway.Port);
        device.LastSeenAt = DateTime.UtcNow.AddMinutes(-5);
        var updates = new List<bool>();
        using var services = PollServices(new[] { device }, updates);
        using var worker = Worker(services);
        await PollAsync(worker);
        Assert.True(device.IsOnline);
        Assert.DoesNotContain(false, updates);
        Assert.True(device.LastSeenAt > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task ScanOnAnAliasDoesNotBlockPollingOtherGateways()
    {
        await using var busy = new FakeGateway(_ => null);
        await using var free = new FakeGateway(_ => null);
        var blocked = Gateway(busy.Port);
        var reachable = Gateway(free.Port);
        using var scan = await ModbusScanCoordinator.AcquireScanAsync(Guid.NewGuid(), "127.0.0.1", busy.Port, default);
        using var services = PollServices(new[] { blocked, reachable }, new List<bool>());
        using var worker = Worker(services);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await PollAsync(worker, timeout.Token);
        Assert.False(timeout.IsCancellationRequested);
        Assert.True(reachable.LastSeenAt > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task FailedEmptyGatewayProbeIsNotRetriedEveryPumpTick()
    {
        // A local refused port exercises connect failure without any plant network access.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var device = Gateway(port);
        using var services = PollServices(new[] { device }, new List<bool>());
        using var worker = Worker(services);
        await PollAsync(worker);
        await PollAsync(worker);
        await PollAsync(worker);
        Assert.True(device.IsOnline);
        var failures = (Dictionary<Guid, int>)typeof(ModbusPollingHostedService)
            .GetField("_zeroSensorProbeFailures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(worker)!;
        Assert.Equal(1, failures[device.Id]);
    }

    private static Device Gateway(int port) => new()
    {
        IpAddress = "127.0.0.1", Port = port, HardwareType = DeviceHardwareType.UsrW610,
        IsOnline = true, Status = DeviceStatus.Online, LastSeenAt = DateTime.UtcNow.AddSeconds(-20), TimeoutMs = 100
    };

    private static ModbusPollingHostedService Worker(ServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(), Array.Empty<ISensorDriver>(),
        NullLogger<ModbusPollingHostedService>.Instance);

    private static Task PollAsync(ModbusPollingHostedService worker, CancellationToken ct = default) =>
        (Task)typeof(ModbusPollingHostedService).GetMethod("PollOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(worker, new object[] { ct })!;

    private static ServiceProvider PollServices(IReadOnlyList<Device> devices, List<bool> updates)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Stub<IDeviceRepository>((method, args) => method.Name switch
        {
            nameof(IDeviceRepository.GetAllAsync) => Task.FromResult(devices),
            nameof(IDeviceRepository.UpdateAsync) => RecordUpdate((Device)args![0]!, updates),
            _ => throw new InvalidOperationException(method.Name)
        }));
        services.AddSingleton(Stub<ISensorRepository>((method, _) => method.Name == nameof(ISensorRepository.GetByDeviceIdAsync)
            ? Task.FromResult<IReadOnlyList<Sensor>>(Array.Empty<Sensor>()) : throw new InvalidOperationException(method.Name)));
        services.AddSingleton(Stub<ISensorTelemetryRepository>((method, _) => throw new InvalidOperationException(method.Name)));
        services.AddSingleton(Stub<ITelemetryBroadcaster>((method, _) => throw new InvalidOperationException(method.Name)));
        // No sensors in these scheduling tests, so alerts and storage are never called.
        services.AddSingleton(new AlertService(null!, null!, NullLogger<AlertService>.Instance, null!));
        return services.BuildServiceProvider();
    }

    private static Task RecordUpdate(Device device, List<bool> updates)
    {
        updates.Add(device.IsOnline);
        return Task.CompletedTask;
    }

    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class RepositoryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
