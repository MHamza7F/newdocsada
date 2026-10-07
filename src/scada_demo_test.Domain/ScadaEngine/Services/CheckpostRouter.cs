using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using scada_demo_test.Domain.Drivers;
using ScadaEngine.Core.Models;

namespace ScadaEngine.Core.Services;

/// <summary>
/// Immutable metadata descriptor for each <see cref="DeviceProfileType"/> stored in
/// an O(1) in-memory <see cref="FrozenDictionary{TKey,TValue}"/> (Architectural Rule #2:
/// NO ROUTING DATABASE DEPENDENCY).
/// </summary>
public sealed record ProfileDescriptor(
    DeviceProfileType ProfileType,
    string DriverKey,
    string SimpleName,
    string ModelName,
    byte DefaultFunctionCode,
    ushort DefaultStartRegister,
    ushort DefaultRegisterQuantity,
    string UnitPrimary,
    string UnitSecondary,
    int DefaultPollIntervalSeconds);

/// <summary>
/// STEP 2: In-Memory Checkpost Router &amp; Signature Trial Engine.
/// Inspects incoming raw byte arrays (checking length, 16-bit register alignment,
/// IEEE-754 float finiteness, and physical bounds) and wraps verified payloads into a
/// strict <see cref="ModbusDevicePacket"/> identity envelope.
/// Unrecognized or corrupted frames are dropped immediately as RS-485 bus noise.
/// </summary>
public static class CheckpostRouter
{
    private static readonly FrozenDictionary<DeviceProfileType, ProfileDescriptor> ProfileTable =
        new Dictionary<DeviceProfileType, ProfileDescriptor>
        {
            [DeviceProfileType.AosongAQ3485] = new(
                DeviceProfileType.AosongAQ3485,
                DriverKey: "AOSONG_AQ3485",
                SimpleName: "aosong",
                ModelName: "Aosong AQ3485/Y (Temperature & Humidity)",
                DefaultFunctionCode: 0x03,
                DefaultStartRegister: 0x0000,
                DefaultRegisterQuantity: 2,
                UnitPrimary: "°C",
                UnitSecondary: "%RH",
                DefaultPollIntervalSeconds: 5),

            [DeviceProfileType.V880BRVortex] = new(
                DeviceProfileType.V880BRVortex,
                DriverKey: "VORTEX_FLOWMETER",
                SimpleName: "vortex",
                ModelName: "V880BR / LUGB Vortex Flowmeter",
                DefaultFunctionCode: 0x04,
                DefaultStartRegister: 1026,
                DefaultRegisterQuantity: 2,
                UnitPrimary: "m³/h",
                UnitSecondary: "m³",
                DefaultPollIntervalSeconds: 3),

            [DeviceProfileType.SelecPower] = new(
                DeviceProfileType.SelecPower,
                DriverKey: "SELEC_POWER_METER",
                SimpleName: "selec_power",
                ModelName: "Selec RI-F200-C 3-Phase Power/Energy Meter",
                DefaultFunctionCode: 0x04,
                DefaultStartRegister: 42,
                DefaultRegisterQuantity: 2,
                UnitPrimary: "kW",
                UnitSecondary: "kWh",
                DefaultPollIntervalSeconds: 5),

            [DeviceProfileType.Electromagnetic] = new(
                DeviceProfileType.Electromagnetic,
                DriverKey: "KAIFENG_EM_FLOWMETER",
                SimpleName: "kaifeng_em",
                ModelName: "Kaifeng IEMFL Electromagnetic Flowmeter (Water)",
                DefaultFunctionCode: 0x03,
                DefaultStartRegister: 90,
                DefaultRegisterQuantity: 2,
                UnitPrimary: "m³/h",
                UnitSecondary: "m³",
                DefaultPollIntervalSeconds: 3)
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, DeviceProfileType> DriverKeyToProfile =
        ProfileTable.Values
            .ToDictionary(p => p.DriverKey, p => p.ProfileType, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// O(1) lookup of profile metadata by <see cref="DeviceProfileType"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProfileDescriptor? GetDescriptor(DeviceProfileType profile) =>
        ProfileTable.TryGetValue(profile, out var desc) ? desc : null;

    /// <summary>
    /// O(1) lookup of <see cref="DeviceProfileType"/> from an installed <c>DriverKey</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DeviceProfileType ResolveProfileByDriverKey(string? driverKey) =>
        !string.IsNullOrWhiteSpace(driverKey) && DriverKeyToProfile.TryGetValue(driverKey, out var profile)
            ? profile
            : DeviceProfileType.Unknown;

    /// <summary>
    /// Inspects a <see cref="RawScanResponse"/> from the Dual-Guard Scanner's RawBucket,
    /// executes byte-level Signature Trials, and returns a verified <see cref="ModbusDevicePacket"/>
    /// envelope. Returns <c>null</c> if the frame fails validation or is corrupted noise.
    /// </summary>
    public static ModbusDevicePacket? InspectAndTag(RawScanResponse? response) =>
        response is null ? null : CheckpostFingerprintEngine.Inspect(response);

    /// <summary>
    /// Direct raw-byte overload for pure in-memory signature inspection &amp; envelope tagging.
    /// </summary>
    public static ModbusDevicePacket? InspectAndTag(
        byte slaveId,
        byte[] rawPayload,
        DeviceProfileType hintedProfile = DeviceProfileType.Unknown,
        byte functionCode = 0x03,
        ushort startRegister = 0,
        ushort registerQuantity = 0,
        bool proofVerified = true)
    {
        // This overload validates the configured identity of an already registered
        // sensor. Discovery must use the evidence-bearing RawScanResponse overload.
        if (!proofVerified || rawPayload is null || hintedProfile == DeviceProfileType.Unknown)
            return null;
        return CheckpostFingerprintEngine.TagRegisteredPayload(slaveId, rawPayload,
            hintedProfile, functionCode, startRegister, registerQuantity);
    }

    /// <summary>
    /// Structural preflight only: function, register map, exact layout and ranges
    /// must agree. Discovery additionally requires multi-point evidence in
    /// <see cref="CheckpostFingerprintEngine"/> before creating a verified envelope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DeviceProfileType RunSignatureTrial(
        ReadOnlySpan<byte> payload,
        DeviceProfileType hintedProfile = DeviceProfileType.Unknown,
        byte functionCode = 0x03,
        ushort startRegister = 0)
    {
        var bytes = payload.ToArray();
        var matches = SensorDriverCatalog.Drivers.Where(driver =>
            driver.FunctionCode == functionCode && driver.StartRegister == startRegister &&
            driver.ValidatePayloadStructure(bytes) && driver.ValidateValueBoundaries(bytes)).ToArray();
        if (matches.Length != 1) return DeviceProfileType.Unknown;
        var profile = ResolveProfileByDriverKey(matches[0].DriverKey);
        return hintedProfile == DeviceProfileType.Unknown || hintedProfile == profile
            ? profile : DeviceProfileType.Unknown;
    }

}
