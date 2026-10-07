using System.Buffers.Binary;
using scada_demo_test.Domain.Drivers;
using ScadaEngine.Core.Drivers;
using ScadaEngine.Core.Models;
using ScadaEngine.Core.Services;
using Xunit;

namespace ScadaEngine.Tests;

public class FingerprintTests
{
    [Theory]
    [InlineData("AOSONG_AQ3485")]
    [InlineData("VORTEX_FLOWMETER")]
    [InlineData("KAIFENG_EM_FLOWMETER")]
    [InlineData("SELEC_POWER_METER")]
    public void EveryDriverIdentifiesAtEveryValidAddress(string key)
    {
        for (int id = 1; id <= 247; id++)
        {
            var response = Fixtures.Response(key, (byte)id);
            var packet = CheckpostFingerprintEngine.Inspect(response);
            Assert.NotNull(packet);
            Assert.Equal(key, packet.DriverKey);
            Assert.Equal(id, packet.SlaveId);
            Assert.Equal(FingerprintConfidence.Verified, packet.ConfidenceScore);
            Assert.True(SensorDriverDispatcher.Dispatch(packet).IsValid);
        }
    }

    [Fact]
    public void HintAndByteLengthCannotSubstituteForEvidence()
    {
        var valid = Fixtures.Response("VORTEX_FLOWMETER", 131);
        var hintedOnly = new RawScanResponse
        {
            SlaveId = 131, IsSuccess = true, Payload = valid.Payload,
            HintedProfile = DeviceProfileType.V880BRVortex, FunctionCode = 4,
            StartRegister = 1026, RegisterQuantity = 2, ProofServed = true
        };
        Assert.Null(CheckpostRouter.InspectAndTag(hintedOnly));
        Assert.Equal(DeviceProfileType.Unknown, CheckpostRouter.RunSignatureTrial(valid.Payload));
        Assert.Null(CheckpostRouter.InspectAndTag(Fixtures.Copy(valid, hint: DeviceProfileType.Electromagnetic)));
    }

    [Fact]
    public void FunctionAndRegisterContextMustAgree()
    {
        var response = Fixtures.Response("VORTEX_FLOWMETER", 218);
        Assert.Null(CheckpostRouter.InspectAndTag(Fixtures.Copy(response, function: 3)));
        Assert.Null(CheckpostRouter.InspectAndTag(Fixtures.Copy(response, start: 90)));
    }

    [Fact]
    public void MissingWindowAndNaNProofAreRejected()
    {
        var response = Fixtures.Response("VORTEX_FLOWMETER", 21);
        Assert.Null(CheckpostRouter.InspectAndTag(Fixtures.Copy(response, evidence: response.Evidence.Take(1).ToArray())));
        var evidence = response.Evidence.ToArray();
        evidence[^1] = evidence[^1] with { Payload = Fixtures.Float(float.NaN) };
        Assert.Null(CheckpostRouter.InspectAndTag(Fixtures.Copy(response, evidence: evidence)));
        evidence = response.Evidence.ToArray();
        evidence[1] = evidence[1] with { Payload = Fixtures.Float(float.PositiveInfinity) };
        Assert.Null(CheckpostRouter.InspectAndTag(Fixtures.Copy(response, evidence: evidence)));
    }

    [Fact]
    public void GenuineZeroFlowAndHumidityBoundariesRemainValid()
    {
        foreach (var key in new[] { "VORTEX_FLOWMETER", "KAIFENG_EM_FLOWMETER", "SELEC_POWER_METER" })
            Assert.NotNull(CheckpostRouter.InspectAndTag(Fixtures.Response(key, 182, zero: true)));
        var response = Fixtures.Response("AOSONG_AQ3485", 92);
        var boundary = new byte[] { 3, 232, 0, 0 };
        var evidence = response.Evidence.Select(e => e with { Payload = boundary }).ToArray();
        Assert.NotNull(CheckpostRouter.InspectAndTag(Fixtures.Copy(response, evidence: evidence, payload: boundary)));
    }

    [Fact]
    public void EveryInstanceOfDuplicateIdIsBlockedIncludingIdenticalProfiles()
    {
        var duplicate = Fixtures.Response("AOSONG_AQ3485", 91);
        var unique = Fixtures.Response("VORTEX_FLOWMETER", 193);
        var service = new ModbusScannerService();
        foreach (var other in new[] { duplicate, Fixtures.Response("SELEC_POWER_METER", 91) })
        {
            var result = service.ExecuteDualGuardPipeline(new[] { duplicate, unique, other });
            Assert.Equal(new byte[] { 91 }, result.BlockedScanDuplicates);
            Assert.Equal(193, Assert.Single(result.UniqueFoundDevices).SlaveId);
            Assert.False(result.DispatchedCalculations.ContainsKey(91));
            Assert.Equal(2, Assert.Single(result.BlockedDuplicateDetails).SensorCount);
        }
    }

    [Fact]
    public void RegisteredGuardPrecedesDuplicatesAndDoesNotClaimUnprobedResponses()
    {
        var response = Fixtures.Response("AOSONG_AQ3485", 161);
        var result = new ModbusScannerService(new[] { 161, 211 })
            .ExecuteDualGuardPipeline(new[] { response, response });
        Assert.Equal(new byte[] { 161, 211 }, result.AlreadyInSystemDevices);
        Assert.Empty(result.UniqueFoundDevices);
        Assert.Empty(result.BlockedScanDuplicates);
        Assert.Contains("responded on the bus", result.AlreadyInSystemDetails.Single(x => x.SlaveId == 161).Message);
        Assert.Contains("auto-skipped", result.AlreadyInSystemDetails.Single(x => x.SlaveId == 211).Message);
    }

    [Fact]
    public void PacketPayloadCannotBeMutatedAfterVerification()
    {
        var response = Fixtures.Response("VORTEX_FLOWMETER", 78);
        var packet = Assert.IsType<ModbusDevicePacket>(CheckpostRouter.InspectAndTag(response));
        var before = SensorDriverDispatcher.Dispatch(packet);
        Array.Fill(response.Payload, (byte)255);
        Array.Fill(packet.RawPayload, (byte)255);
        Assert.Equal(before, SensorDriverDispatcher.Dispatch(packet));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(248)]
    [InlineData(255)]
    public void ReservedAddressesAreRejected(int id) =>
        Assert.Null(CheckpostRouter.InspectAndTag(Fixtures.Response("AOSONG_AQ3485", (byte)id)));

    [Fact]
    public void ReusingBatchServiceDoesNotLeakPreviousSessionResults()
    {
        var service = new ModbusScannerService();
        var response = Fixtures.Response("AOSONG_AQ3485", 71);
        Assert.Single(service.ExecuteDualGuardPipeline(new[] { response, response }).BlockedScanDuplicates);
        var fresh = service.ExecuteDualGuardPipeline(new[] { response });
        Assert.Empty(fresh.BlockedScanDuplicates);
        Assert.Single(fresh.UniqueFoundDevices);
    }
}

internal static class Fixtures
{
    public static byte[] Float(float value, bool lowWord = false)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        return lowWord ? new[] { bytes[2], bytes[3], bytes[0], bytes[1] } : bytes;
    }

    public static RawScanResponse Response(string key, byte id, bool zero = false)
    {
        var driver = SensorDriverCatalog.RequireByKey(key);
        var evidence = driver.ReadWindows.Select((w, i) => new ScanWindowResponse(w,
            key == "AOSONG_AQ3485" ? new byte[] { 1, 244, 0, 250 } :
            Float(zero ? 0 : i == 0 ? 25 : 1000, key == "SELEC_POWER_METER"))).ToList();
        var payload = evidence.SelectMany(e => e.Payload).ToArray();
        if (driver.CorroborationWindow is { } proof)
            evidence.Add(new ScanWindowResponse(proof, Float(5, key == "SELEC_POWER_METER")));
        else
            evidence.Add(new ScanWindowResponse(driver.ReadWindows[0], evidence[0].Payload.ToArray()));
        return new RawScanResponse
        {
            SlaveId = id, IsSuccess = true, Payload = payload, Evidence = evidence,
            FunctionCode = driver.FunctionCode, StartRegister = driver.StartRegister,
            RegisterQuantity = driver.RegisterQuantity, ModelName = driver.DisplayName
        };
    }

    public static RawScanResponse Copy(RawScanResponse source, IReadOnlyList<ScanWindowResponse>? evidence = null,
        byte[]? payload = null, DeviceProfileType? hint = null, byte? function = null, ushort? start = null) => new()
        {
            SlaveId = source.SlaveId, IsSuccess = source.IsSuccess, Payload = payload ?? source.Payload,
            Evidence = evidence ?? source.Evidence, FunctionCode = function ?? source.FunctionCode,
            StartRegister = start ?? source.StartRegister, RegisterQuantity = source.RegisterQuantity,
            HintedProfile = hint ?? source.HintedProfile
        };
}
