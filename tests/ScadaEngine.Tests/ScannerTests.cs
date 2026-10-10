using scada_demo_test.Application.DTOs;
using scada_demo_test.Infrastructure.Modbus;
using Xunit;

namespace ScadaEngine.Tests;

public class ScannerTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(11)]
    public async Task ExceptionCodeComesFromPduNotFunction(int code)
    {
        await using var gateway = new FakeGateway(r => FakeGateway.ExceptionFrame(r, (byte)code));
        using var session = await new ModbusTcpMaster().OpenAsync("127.0.0.1", gateway.Port, 3000, default);
        var error = await Assert.ThrowsAsync<ModbusException>(() => session.ReadHoldingRegistersAsync(63, 0, 2, default));
        Assert.Equal((byte?)code, error.ExceptionCode);
        Assert.False(error.IsProtocolError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task InvalidResponseFramingIsRejected(int corruptIndex)
    {
        await using var gateway = new FakeGateway(r =>
        {
            var frame = FakeGateway.DataFrame(r, new byte[] { 1, 244, 0, 250 });
            frame[corruptIndex] = 255;
            return frame;
        });
        using var session = await new ModbusTcpMaster().OpenAsync("127.0.0.1", gateway.Port, 3000, default);
        var error = await Assert.ThrowsAsync<ModbusException>(() => session.ReadHoldingRegistersAsync(62, 0, 2, default));
        Assert.True(error.IsProtocolError);
    }

    [Fact]
    public async Task TimeoutInvalidatesSessionInsteadOfLeakingLateBytes()
    {
        await using var gateway = new FakeGateway(_ => null);
        using var session = await new ModbusTcpMaster().OpenAsync("127.0.0.1", gateway.Port, 3000, default);
        await Assert.ThrowsAsync<TimeoutException>(() => session.ReadHoldingRegistersAsync(59, 0, 2, default, 30));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReadHoldingRegistersAsync(60, 0, 2, default, 30));
    }

    [Fact]
    public async Task EveryAddressIsScannedAndRegisteredIdsAreNotProbed()
    {
        await using var gateway = new FakeGateway(Fixtures.Response("VORTEX_FLOWMETER", 247), Fixtures.Response("AOSONG_AQ3485", 1));
        var result = await new ModbusScanner().ScanAsync(Request(gateway, 1, 247, new[] { 117 }));
        Assert.Equal(246, result.SlavesScanned);
        Assert.Equal(new[] { 1, 247 }, result.Found.Select(f => f.SlaveAddress));
        Assert.Equal(117, Assert.Single(result.AlreadyInSystemSlaves!).SlaveAddress);
        Assert.DoesNotContain(gateway.Requests, r => r[6] == 117);
        Assert.DoesNotContain(gateway.Requests, r => r[6] is 0 or > 247);
    }

    [Fact]
    public async Task FullProfilesConflictAndNoRankedWinnerLeaksToFound()
    {
        await using var gateway = new FakeGateway(Fixtures.Response("AOSONG_AQ3485", 133), Fixtures.Response("VORTEX_FLOWMETER", 133));
        var result = await new ModbusScanner().ScanAsync(Request(gateway, 133, 133));
        Assert.Empty(result.Found);
        Assert.Equal(133, Assert.Single(result.DuplicateIdConflicts!).SlaveAddress);
    }

    [Fact]
    public async Task RepeatedIdenticalTransactionRepliesAreBlocked()
    {
        await using var gateway = new FakeGateway(r =>
        {
            var frame = FakeGateway.DataFrame(r, new byte[] { 1, 244, 0, 250 });
            return frame.Concat(frame).ToArray();
        });
        var result = await new ModbusScanner().ScanAsync(Request(gateway, 153, 153));
        Assert.Empty(result.Found);
        Assert.Equal(153, Assert.Single(result.DuplicateIdConflicts!).SlaveAddress);
    }

    [Fact]
    public async Task PartialProfileCannotMasqueradeAsACompleteMeter()
    {
        var meter = Fixtures.Response("VORTEX_FLOWMETER", 201);
        await using var gateway = new FakeGateway(Fixtures.Copy(meter, evidence: meter.Evidence.Take(1).ToArray()));
        var result = await new ModbusScanner().ScanAsync(Request(gateway, 201, 201));
        Assert.Empty(result.Found);
        Assert.Equal(1, result.UnknownResponders);
    }

    [Fact]
    public async Task CancelledScanNeverReturnsPartialFoundResults()
    {
        using var cancel = new CancellationTokenSource();
        await using var gateway = new FakeGateway(r =>
        {
            if (r[6] == 2) cancel.Cancel();
            return r[6] == 1 && r[7] == 3 && r[8] == 0 && r[9] == 0
                ? FakeGateway.DataFrame(r, new byte[] { 1, 244, 0, 250 })
                : FakeGateway.ExceptionFrame(r, 11);
        });
        var result = await new ModbusScanner().ScanAsync(Request(gateway, 1, 247), cancel.Token);
        Assert.Empty(result.Found);
        Assert.Equal("SCAN_CANCELLED", result.ErrorCode);
        Assert.False(ModbusScanCoordinator.IsScanning(result.DeviceId));
    }

    [Fact]
    public async Task GatewayLeaseExcludesPollingAndTracksQueuedScans()
    {
        var id = Guid.NewGuid();
        using var first = await ModbusScanCoordinator.AcquireScanAsync(id, "test-gateway", 502, default);
        var queuedScan = ModbusScanCoordinator.AcquireScanAsync(id, "test-gateway", 502, default);
        var polling = ModbusScanCoordinator.AcquireBusAsync("test-gateway", 502, default);
        Assert.False(queuedScan.IsCompleted);
        Assert.False(polling.IsCompleted);
        Assert.True(ModbusScanCoordinator.IsScanning(id));
        first.Dispose();
        using var second = await queuedScan.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(ModbusScanCoordinator.IsScanning(id));
        Assert.False(polling.IsCompleted);
        second.Dispose();
        using var poll = await polling.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(ModbusScanCoordinator.IsScanning(id));
    }

    private static SmartScanRequest Request(FakeGateway gateway, int start, int end, int[]? skip = null) =>
        new(Guid.NewGuid(), "test gateway", "127.0.0.1", gateway.Port, 500, 100,
            skip ?? Array.Empty<int>(), start, end, Passes: 1);
}
