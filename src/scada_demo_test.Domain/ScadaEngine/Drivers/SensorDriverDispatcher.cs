using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using scada_demo_test.Domain.Drivers;
using ScadaEngine.Core.Models;

namespace ScadaEngine.Core.Drivers;

/// <summary>
/// STEP 4: High-speed In-Memory Driver Dispatcher ("Ghar" Calculation Engine).
/// Architectural Rules Enforced:
///   1. NO BLIND PROCESSING: Calculation methods accept ONLY a verified <see cref="ModbusDevicePacket"/>
///      envelope produced by <see cref="ScadaEngine.Core.Services.CheckpostRouter"/>.
///   2. NO ROUTING DATABASE DEPENDENCY: Dispatching runs in O(1) microsecond time via
///      an immutable in-memory <see cref="FrozenDictionary{TKey,TValue}"/>.
///   3. Thread-Safe &amp; Stateless: All driver instances and delegates are immutable singletons.
/// </summary>
public static class SensorDriverDispatcher
{
    private static readonly AosongAQ3485Driver AosongDriver = new();
    private static readonly VortexFlowmeterDriver VortexDriver = new();
    private static readonly ElectromagneticFlowmeterDriver ElectromagneticDriver = new();
    private static readonly SelecPowerMeterDriver SelecPowerDriver = new();

    /// <summary>
    /// O(1) In-Memory Routing Table mapping <see cref="DeviceProfileType"/> directly to its
    /// dedicated "Ghar" calculation delegate.
    /// </summary>
    private static readonly FrozenDictionary<DeviceProfileType, Func<ModbusDevicePacket, DriverCalculationResult>> DispatchTable =
        new Dictionary<DeviceProfileType, Func<ModbusDevicePacket, DriverCalculationResult>>
        {
            [DeviceProfileType.AosongAQ3485] = AQ3485Ghar,
            [DeviceProfileType.V880BRVortex] = V880BRGhar,
            [DeviceProfileType.Electromagnetic] = ElectromagneticGhar,
            [DeviceProfileType.SelecPower] = SelecPowerGhar
        }.ToFrozenDictionary();

    /// <summary>
    /// Routes a verified <see cref="ModbusDevicePacket"/> envelope to its designated driver
    /// calculation logic in microseconds. Never touches the database.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DriverCalculationResult Dispatch(ModbusDevicePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var driver = SensorDriverCatalog.GetByKey(packet.DriverKey);
        if (!packet.ProofVerified || packet.ConfidenceScore != FingerprintConfidence.Verified || driver is null ||
            ScadaEngine.Core.Services.CheckpostRouter.ResolveProfileByDriverKey(packet.DriverKey) != packet.ProfileType ||
            packet.FunctionCode != driver.FunctionCode || packet.StartRegister != driver.StartRegister ||
            packet.RegisterQuantity != driver.RegisterQuantity ||
            !driver.ValidatePayloadStructure(packet.RawPayload) || !driver.ValidateValueBoundaries(packet.RawPayload))
            return InvalidResult(packet, "UNVERIFIED_PACKET");

        if (!DispatchTable.TryGetValue(packet.ProfileType, out var gharCalculator))
        {
            return InvalidResult(packet, "UNSUPPORTED_PROFILE");
        }

        return gharCalculator(packet);
    }

    /// <summary>
    /// Designated calculation home ("Ghar") for <see cref="DeviceProfileType.AosongAQ3485"/>.
    /// Decodes 4-byte Holding Register payload:
    ///   Register[0] (unsigned 16-bit / 10.0) -&gt; Relative Humidity (%RH)
    ///   Register[1] (signed 16-bit / 10.0)   -&gt; Temperature (°C)
    /// </summary>
    private static DriverCalculationResult AQ3485Ghar(ModbusDevicePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.ProfileType != DeviceProfileType.AosongAQ3485)
            return InvalidResult(packet, "PROFILE_MISMATCH_AQ3485");

        var parsed = AosongDriver.ParseData(packet.RawPayload);
        if (!parsed.Success)
            return InvalidResult(packet, parsed.ErrorCode ?? "PARSE_ERROR_AQ3485");

        return new DriverCalculationResult(
            SlaveId: packet.SlaveId,
            ProfileType: packet.ProfileType,
            DriverKey: AosongDriver.DriverKey,
            ModelName: AosongDriver.DisplayName,
            IsValid: true,
            PrimaryValue: parsed.PrimaryValue,
            PrimaryColumnName: AosongDriver.PrimaryColumnName,
            PrimaryUnit: AosongDriver.UnitPrimary,
            SecondaryValue: parsed.SecondaryValue,
            SecondaryColumnName: AosongDriver.SecondaryColumnName,
            SecondaryUnit: AosongDriver.UnitSecondary,
            RawHexPayload: packet.RawHex,
            TimestampUtc: packet.Timestamp);
    }

    /// <summary>
    /// Designated calculation home ("Ghar") for <see cref="DeviceProfileType.V880BRVortex"/>.
    /// Decodes IEEE-754 High-Word-First 32-bit floats from Input Registers (FC04 @1026, @1032):
    ///   Primary   -&gt; Instantaneous Flow Rate (m³/h)
    ///   Secondary -&gt; Accumulated Totalizer (m³)
    /// </summary>
    private static DriverCalculationResult V880BRGhar(ModbusDevicePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.ProfileType != DeviceProfileType.V880BRVortex)
            return InvalidResult(packet, "PROFILE_MISMATCH_V880BR");

        var parsed = VortexDriver.ParseData(packet.RawPayload);
        if (!parsed.Success)
            return InvalidResult(packet, parsed.ErrorCode ?? "PARSE_ERROR_V880BR");

        return new DriverCalculationResult(
            SlaveId: packet.SlaveId,
            ProfileType: packet.ProfileType,
            DriverKey: VortexDriver.DriverKey,
            ModelName: VortexDriver.DisplayName,
            IsValid: true,
            PrimaryValue: parsed.PrimaryValue,
            PrimaryColumnName: VortexDriver.PrimaryColumnName,
            PrimaryUnit: VortexDriver.UnitPrimary,
            SecondaryValue: parsed.SecondaryValue,
            SecondaryColumnName: VortexDriver.SecondaryColumnName,
            SecondaryUnit: VortexDriver.UnitSecondary,
            RawHexPayload: packet.RawHex,
            TimestampUtc: packet.Timestamp);
    }



    /// <summary>
    /// Designated calculation home ("Ghar") for <see cref="DeviceProfileType.Electromagnetic"/>.
    /// Decodes IEEE-754 High-Word-First 32-bit floats for Kaifeng IEMFL Water Flowmeter:
    ///   Window 0 (bytes 0..3 @ reg 90) -&gt; Accumulated Totalizer (m³)
    ///   Window 1 (bytes 4..7 @ reg 98) -&gt; Instantaneous Flow Rate (m³/h)
    /// </summary>
    private static DriverCalculationResult ElectromagneticGhar(ModbusDevicePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.ProfileType != DeviceProfileType.Electromagnetic)
            return InvalidResult(packet, "PROFILE_MISMATCH_ELECTROMAGNETIC");

        if (packet.RawPayload.Length < 4)
            return InvalidResult(packet, "INVALID_PAYLOAD_ELECTROMAGNETIC");

        // Window 0 (@90) = Totalizer, Window 1 (@98) = Flow Rate
        double totalizer = Math.Round(ModbusValueCodec.ReadFloat32HighWordFirst(packet.RawPayload, 0), 2);
        double flowRate = packet.RawPayload.Length >= 8
            ? Math.Round(ModbusValueCodec.ReadFloat32HighWordFirst(packet.RawPayload, 4), 2)
            : 0.0;

        return new DriverCalculationResult(
            SlaveId: packet.SlaveId,
            ProfileType: packet.ProfileType,
            DriverKey: ElectromagneticDriver.DriverKey,
            ModelName: ElectromagneticDriver.DisplayName,
            IsValid: true,
            PrimaryValue: flowRate,
            PrimaryColumnName: ElectromagneticDriver.PrimaryColumnName,
            PrimaryUnit: ElectromagneticDriver.UnitPrimary,
            SecondaryValue: totalizer,
            SecondaryColumnName: ElectromagneticDriver.SecondaryColumnName,
            SecondaryUnit: ElectromagneticDriver.UnitSecondary,
            RawHexPayload: packet.RawHex,
            TimestampUtc: packet.Timestamp);
    }

    /// <summary>
    /// Designated calculation home ("Ghar") for <see cref="DeviceProfileType.SelecPower"/>.
    /// Decodes IEEE-754 Low-Word-First ("FLOAT REVERSE WORD") 32-bit floats for Selec RI-F200-C:
    ///   Window 0 (bytes 0..3 @ reg 42) -&gt; Active Power (kW)
    ///   Window 1 (bytes 4..7 @ reg 58) -&gt; Active Energy Totalizer (kWh)
    /// </summary>
    private static DriverCalculationResult SelecPowerGhar(ModbusDevicePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.ProfileType != DeviceProfileType.SelecPower)
            return InvalidResult(packet, "PROFILE_MISMATCH_SELEC");

        var parsed = SelecPowerDriver.ParseData(packet.RawPayload);
        if (!parsed.Success)
            return InvalidResult(packet, parsed.ErrorCode ?? "PARSE_ERROR_SELEC");

        return new DriverCalculationResult(
            SlaveId: packet.SlaveId,
            ProfileType: packet.ProfileType,
            DriverKey: SelecPowerDriver.DriverKey,
            ModelName: SelecPowerDriver.DisplayName,
            IsValid: true,
            PrimaryValue: parsed.PrimaryValue,
            PrimaryColumnName: SelecPowerDriver.PrimaryColumnName,
            PrimaryUnit: SelecPowerDriver.UnitPrimary,
            SecondaryValue: parsed.SecondaryValue,
            SecondaryColumnName: SelecPowerDriver.SecondaryColumnName,
            SecondaryUnit: SelecPowerDriver.UnitSecondary,
            RawHexPayload: packet.RawHex,
            TimestampUtc: packet.Timestamp);
    }

    private static DriverCalculationResult InvalidResult(ModbusDevicePacket packet, string errorCode) =>
        new(
            SlaveId: packet.SlaveId,
            ProfileType: packet.ProfileType,
            DriverKey: packet.DriverKey,
            ModelName: packet.ModelName,
            IsValid: false,
            PrimaryValue: 0,
            PrimaryColumnName: string.Empty,
            PrimaryUnit: string.Empty,
            SecondaryValue: 0,
            SecondaryColumnName: string.Empty,
            SecondaryUnit: string.Empty,
            RawHexPayload: packet.RawHex,
            TimestampUtc: packet.Timestamp,
            ErrorCode: errorCode);
}
