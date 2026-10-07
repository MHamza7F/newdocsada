using scada_demo_test.Domain.Drivers;
using ScadaEngine.Core.Models;

namespace ScadaEngine.Core.Services;

public static class CheckpostFingerprintEngine
{
    public static ModbusDevicePacket? Inspect(RawScanResponse response)
    {
        if (response is null || !response.IsSuccess || response.SlaveId is < 1 or > 247 ||
            response.Evidence is null || response.Payload is null)
            return null;

        var matches = SensorDriverCatalog.Drivers
            .Where(driver => Matches(driver, response.Evidence))
            .ToArray();
        if (matches.Length != 1) return null;

        var driver = matches[0];
        // A hint may reject a mismatch, but can never supply missing evidence.
        var profile = CheckpostRouter.ResolveProfileByDriverKey(driver.DriverKey);
        if (response.HintedProfile != DeviceProfileType.Unknown && response.HintedProfile != profile)
            return null;
        if (response.FunctionCode != driver.FunctionCode || response.StartRegister != driver.StartRegister ||
            response.RegisterQuantity != driver.RegisterQuantity)
            return null;

        var payload = driver.ReadWindows.SelectMany(window =>
            response.Evidence.First(e => e.Window == window).Payload).ToArray();
        if (!payload.AsSpan().SequenceEqual(response.Payload)) return null;
        return TagRegisteredPayload(response.SlaveId, payload, profile, response.FunctionCode,
            response.StartRegister, response.RegisterQuantity);
    }

    public static bool Matches(ISensorDriver driver, IReadOnlyList<ScanWindowResponse> evidence)
    {
        var windows = driver.ReadWindows;
        if (windows.Count == 0 || evidence is null || evidence.Any(e => e is null || e.Payload is null)) return false;
        var combined = new List<byte>();
        foreach (var window in windows)
        {
            var samples = evidence.Where(e => e.Window == window).ToArray();
            if (samples.Length == 0 || samples.Any(e => e.Payload.Length != window.RegisterQuantity * 2))
                return false;
            combined.AddRange(samples[0].Payload);
        }
        var payload = combined.ToArray();
        if (!driver.ValidatePayloadStructure(payload) || !driver.ValidateValueBoundaries(payload))
            return false;

        if (driver.CorroborationWindow is { } proof)
        {
            var samples = evidence.Where(e => e.Window == proof).ToArray();
            if (samples.Length == 0 || samples.Any(e => !driver.ValidateCorroboration(e.Payload)))
                return false;
        }
        else
        {
            // A single-window sensor needs two independent successful reads. These are
            // fingerprint evidence, not two discovery results / two physical meters.
            var samples = evidence.Where(e => e.Window == windows[0]).ToArray();
            if (samples.Length < 2 || samples.Any(e => !driver.ValidatePayloadStructure(e.Payload) ||
                !driver.ValidateValueBoundaries(e.Payload))) return false;
        }
        return true;
    }

    internal static ModbusDevicePacket? TagRegisteredPayload(byte slaveId, byte[] payload,
        DeviceProfileType profile, byte function, ushort start, ushort quantity)
    {
        var descriptor = CheckpostRouter.GetDescriptor(profile);
        var driver = descriptor is null ? null : SensorDriverCatalog.GetByKey(descriptor.DriverKey);
        if (slaveId is < 1 or > 247 || driver is null || function != driver.FunctionCode ||
            start != driver.StartRegister || quantity != driver.RegisterQuantity ||
            !driver.ValidatePayloadStructure(payload) || !driver.ValidateValueBoundaries(payload)) return null;

        return new ModbusDevicePacket(slaveId, profile, driver.DriverKey, driver.DisplayName,
            payload, function, start, quantity, proofVerified: true);
    }
}

public sealed record ScanWindowResponse(SensorReadWindow Window, byte[] Payload);
