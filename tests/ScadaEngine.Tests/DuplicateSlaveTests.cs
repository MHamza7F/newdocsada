using scada_demo_test.Application.DTOs;
using ScadaEngine.Core.Models;
using ScadaEngine.Core.Services;
using scada_demo_test.Domain.Drivers;
using scada_demo_test.Infrastructure.Modbus;
using Xunit;

namespace ScadaEngine.Tests;

public class DuplicateSlaveTests
{
    // Plant scenario: humidity meter on slave 1, two REAL meters (Kaifeng EM +
    // Selec energy) accidentally sharing slave 13. The unique slave must show in
    // the Found Box; BOTH meters on slave 13 must be hidden behind the conflict.
    [Fact]
    async Task TwoMetersOnSlave13BothHide_Slave1StillShows()
    {
        await using var gateway = new FakeGateway(
            Fixtures.Response("AOSONG_AQ3485", 1),
            Fixtures.Response("KAIFENG_EM_FLOWMETER", 13),
            Fixtures.Response("SELEC_POWER_METER", 13));

        var result = await new ModbusScanner().ScanAsync(Request(gateway, 1, 20));

        var conflict = Assert.Single(result.DuplicateIdConflicts!);
        Assert.Equal(13, conflict.SlaveAddress);
        Assert.Equal(2, conflict.SensorCount);
        Assert.DoesNotContain(result.Found, f => f.SlaveAddress == 13);

        var unique = Assert.Single(result.Found);
        Assert.Equal(1, unique.SlaveAddress);
        Assert.Equal("AOSONG_AQ3485", unique.DriverKey);
    }

    // Operator pairing test 2: humidity + energy meter forced onto ONE Slave ID.
    [Fact]
    async Task HumidityAndEnergyOnSameSlaveBothHide()
    {
        await using var gateway = new FakeGateway(
            Fixtures.Response("VORTEX_FLOWMETER", 7),
            Fixtures.Response("AOSONG_AQ3485", 9),
            Fixtures.Response("SELEC_POWER_METER", 9));

        var result = await new ModbusScanner().ScanAsync(Request(gateway, 1, 20));

        var conflict = Assert.Single(result.DuplicateIdConflicts!);
        Assert.Equal(9, conflict.SlaveAddress);
        Assert.DoesNotContain(result.Found, f => f.SlaveAddress == 9);
        Assert.Equal(7, Assert.Single(result.Found).SlaveAddress);
    }

    // When Kaifeng has non-zero data and Selec has zero data WITHOUT valid disproof,
    // both are on same FC (FC04) - they're two physical devices sharing one ID.
    // BOTH should be hidden as a conflict.
    [Fact]
    async Task KaifengNonZeroAndSelecZeroOnSameSlaveBothHide()
    {
        var selec = Fixtures.Response("SELEC_POWER_METER", 13);
        // Force Selec to be all zeros including disproof (simulating ghost/zero-filler)
        var selecZero = Fixtures.Copy(selec,
            evidence: selec.Evidence.Select(e => new ScanWindowResponse(e.Window,
                new byte[e.Window.RegisterQuantity * 2] // All zeros
            )).ToList(),
            payload: new byte[8]);

        await using var gateway = new FakeGateway(
            Fixtures.Response("KAIFENG_EM_FLOWMETER", 13),
            selecZero);

        var result = await new ModbusScanner().ScanAsync(Request(gateway, 13, 13));

        // Both on same FC04, same ID -> conflict
        Assert.Empty(result.Found);
        var conflict = Assert.Single(result.DuplicateIdConflicts!);
        Assert.Equal(13, conflict.SlaveAddress);
        Assert.Equal(2, conflict.SensorCount);
    }

    // Identical family double-wired on one ID must hide too (Guard 2 counts
    // frames per Slave ID, it never cares which driver family answered).
    [Fact]
    async Task TwoIdenticalMetersOnOneSlaveBothHide()
    {
        await using var gateway = new FakeGateway(
            Fixtures.Response("VORTEX_FLOWMETER", 7),
            Fixtures.Response("VORTEX_FLOWMETER", 7));

        var result = await new ModbusScanner().ScanAsync(Request(gateway, 7, 7));

        Assert.Empty(result.Found);
        Assert.Equal(7, Assert.Single(result.DuplicateIdConflicts!).SlaveAddress);
    }

    // Two meters on one ID must never leave ONE of them in the Found Box even
    // when one family answers every window and the other answers only its ident.
    [Fact]
    async Task PartialResponderOnSharedIdStillHidesBoth()
    {
        var full = Fixtures.Response("KAIFENG_EM_FLOWMETER", 13);
        var partial = Fixtures.Copy(Fixtures.Response("SELEC_POWER_METER", 13),
            evidence: Fixtures.Response("SELEC_POWER_METER", 13).Evidence.Take(1).ToArray());

        await using var gateway = new FakeGateway(full, partial);
        var result = await new ModbusScanner().ScanAsync(Request(gateway, 13, 13));

        Assert.Empty(result.Found);
        Assert.Equal(13, Assert.Single(result.DuplicateIdConflicts!).SlaveAddress);
    }

    private static SmartScanRequest Request(FakeGateway gateway, int start, int end, int[]? skip = null) =>
        new(Guid.NewGuid(), "test gateway", "127.0.0.1", gateway.Port, 500, 100,
            skip ?? Array.Empty<int>(), start, end, Passes: 1);

    // Cross-FC witness: an energy meter (Selec FC04) whose ident read fails but
    // whose cross-FC probe (FC03) returns non-zero data. AOSONG's cross-FC probe
    // detects this and counts it as a witness.
    [Fact]
    async Task CrossFcDataWitnessHidesBothMeters()
    {
        var selecEvidence = new List<ScanWindowResponse>
        {
            new(new SensorReadWindow(0x04, 42, 2), new byte[4]),
            new(new SensorReadWindow(0x04, 58, 2), new byte[4]),
            new(new SensorReadWindow(0x04, 64, 2), new byte[4]),
            new(new SensorReadWindow(0x04, 0x02AC, 2), new byte[] { 0x00, 0x6E, 0x7E, 0x19 }),
        };
        var crossFcPayload = new byte[] { 0x00, 0x01, 0x00, 0x02 }; // non-zero

        await using var gateway = new FakeGateway(
            Fixtures.Response("KAIFENG_EM_FLOWMETER", 13),
            new RawScanResponse
            {
                SlaveId = 13, IsSuccess = true,
                Payload = new byte[8],
                Evidence = selecEvidence,
                FunctionCode = 0x04, StartRegister = 42, RegisterQuantity = 2,
                ModelName = "Selec RI-F200-C 3-Phase Power/Energy Meter"
            });

        // Override: make FC03@0 return non-zero data (simulating KAIFENG's FC03 response)
        gateway.OverrideReply(13, 0x03, 0, crossFcPayload);

        var result = await new ModbusScanner().ScanAsync(Request(gateway, 13, 13));

        Assert.Empty(result.Found);
        var conflict = Assert.Single(result.DuplicateIdConflicts!);
        Assert.Equal(13, conflict.SlaveAddress);
        Assert.Equal(3, conflict.SensorCount); // KAIFENG + SELEC + cross-FC witness
    }
}
