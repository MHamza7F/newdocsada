using System.Collections.Frozen;

namespace ScadaEngine.Core.Models;

/// <summary>
/// Strictly enumerated hardware profiles supported by the USR-W610 RS-485 gateway engine.
/// Every incoming frame must be classified into one of these profiles by the CheckpostRouter
/// before any engineering calculation is permitted.
/// </summary>
public enum FingerprintConfidence { Unverified, Verified }

public enum DeviceProfileType : byte
{
    Unknown = 0,
    AosongAQ3485 = 1,
    V880BRVortex = 2,
    SelecPower = 4,
    Electromagnetic = 5
}

/// <summary>
/// Strict Identity Envelope (Architectural Rule #1: NO BLIND PROCESSING).
/// Raw response bytes from the USR-W610 gateway are never passed directly to driver
/// calculation methods. Only a verified, Checkpost-tagged <see cref="ModbusDevicePacket"/>
/// may enter the Driver Dispatcher ("Ghar" calculation methods).
/// </summary>
public sealed class ModbusDevicePacket
{
    public byte SlaveId { get; }
    public DeviceProfileType ProfileType { get; }
    public string DriverKey { get; }
    public string DriverIdentifier => DriverKey;
    public scada_demo_test.Domain.Enums.DeviceHardwareType HardwareType =>
        scada_demo_test.Domain.Drivers.SensorDriverCatalog.RequireByKey(DriverKey).HardwareType;
    public string ModelName { get; }
    private readonly byte[] _rawPayload;
    public byte[] RawPayload => (byte[])_rawPayload.Clone();
    public FingerprintConfidence ConfidenceScore => FingerprintConfidence.Verified;
    public byte FunctionCode { get; }
    public ushort StartRegister { get; }
    public ushort RegisterQuantity { get; }
    public bool ProofVerified { get; }
    public DateTime Timestamp { get; }

    internal ModbusDevicePacket(
        byte slaveId,
        DeviceProfileType profileType,
        string driverKey,
        string modelName,
        byte[] rawPayload,
        byte functionCode = 0x03,
        ushort startRegister = 0,
        ushort registerQuantity = 2,
        bool proofVerified = true,
        DateTime? timestampUtc = null)
    {
        if (slaveId is < 1 or > 247)
            throw new ArgumentOutOfRangeException(nameof(slaveId), slaveId, "Modbus RTU Slave ID must be between 1 and 247.");
        if (profileType == DeviceProfileType.Unknown)
            throw new ArgumentException("Cannot construct a verified ModbusDevicePacket with Unknown profile type.", nameof(profileType));
        if (string.IsNullOrWhiteSpace(driverKey))
            throw new ArgumentException("DriverKey is required on a verified ModbusDevicePacket.", nameof(driverKey));
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("ModelName is required on a verified ModbusDevicePacket.", nameof(modelName));
        if (rawPayload is null || rawPayload.Length == 0)
            throw new ArgumentException("RawPayload cannot be null or empty.", nameof(rawPayload));

        SlaveId = slaveId;
        ProfileType = profileType;
        DriverKey = driverKey;
        ModelName = modelName;
        // Defensive copy so external callers can never mutate an envelope's payload after Checkpost inspection.
        _rawPayload = (byte[])rawPayload.Clone();
        FunctionCode = functionCode;
        StartRegister = startRegister;
        RegisterQuantity = registerQuantity;
        ProofVerified = proofVerified;
        Timestamp = timestampUtc ?? DateTime.UtcNow;
    }

    public string RawHex => Convert.ToHexString(RawPayload);
}

/// <summary>
/// Raw, unverified response captured from the RS-485 / USR-W610 gateway during a bus scan
/// before Checkpost signature inspection.
/// </summary>
public sealed class RawScanResponse
{
    public byte SlaveId { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();
    public bool IsSuccess { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    // Optional register-window context captured during multi-window probing on the USR-W610 bridge.
    public DeviceProfileType HintedProfile { get; init; } = DeviceProfileType.Unknown;
    public byte FunctionCode { get; init; } = 0x03;
    public ushort StartRegister { get; init; }
    public ushort RegisterQuantity { get; init; }
    public bool ProofServed { get; init; }
    public IReadOnlyList<ScadaEngine.Core.Services.ScanWindowResponse> Evidence { get; init; } = Array.Empty<ScadaEngine.Core.Services.ScanWindowResponse>();
    public string? ModelName { get; init; }
}

/// <summary>
/// Detailed record for Bucket 1 (Checkpoint 1: DB / System Existence Guard).
/// </summary>
public sealed record AlreadyInSystemDevice(
    byte SlaveId,
    string? ExistingSensorName,
    string? ExistingDriverKey,
    string Message);

/// <summary>
/// Detailed record for Bucket 2 (Checkpoint 2: Current Scan Duplicate Guard).
/// Holds the count of colliding devices, their human-readable meter names, and operator instructions.
/// </summary>
public sealed record BlockedScanDuplicate(
    byte SlaveId,
    int SensorCount,
    string Message,
    List<string>? CollidingSensorNames = null);

/// <summary>
/// Engineering calculation output produced strictly from a verified <see cref="ModbusDevicePacket"/>
/// by its designated Driver "Ghar" calculation method.
/// </summary>
public sealed record DriverCalculationResult(
    byte SlaveId,
    DeviceProfileType ProfileType,
    string DriverKey,
    string ModelName,
    bool IsValid,
    double PrimaryValue,
    string PrimaryColumnName,
    string PrimaryUnit,
    double SecondaryValue,
    string SecondaryColumnName,
    string SecondaryUnit,
    string RawHexPayload,
    DateTime TimestampUtc,
    string? ErrorCode = null);

/// <summary>
/// Holds the 3 distinct result buckets produced by the Dual-Guard Scanner Pipeline:
///   1. <see cref="AlreadyInSystemDevices"/>  (Checkpoint 1: IDs already registered in DB/App)
///   2. <see cref="BlockedScanDuplicates"/>   (Checkpoint 2: IDs that responded multiple times / collided in current scan)
///   3. <see cref="UniqueFoundDevices"/>      (Found Box: Clean, verified <see cref="ModbusDevicePacket"/> envelopes)
/// </summary>
public sealed class ScanResultSummary
{
    /// <summary>
    /// Bucket 1: Slave IDs already registered in the database/system.
    /// UI Prompt: "Already added in system. Change hardware ID".
    /// </summary>
    public List<byte> AlreadyInSystemDevices { get; init; } = new();

    /// <summary>
    /// Rich details for Bucket 1 (Slave ID + UI prompt message).
    /// </summary>
    public List<AlreadyInSystemDevice> AlreadyInSystemDetails { get; init; } = new();

    /// <summary>
    /// Bucket 2: Slave IDs blocked during the current scan session because multiple responses/devices
    /// answered on the same Slave ID (`Count() > 1` in RawBucket).
    /// UI Prompt: "Bus conflict: Same ID responded multiple times".
    /// </summary>
    public List<byte> BlockedScanDuplicates { get; init; } = new();

    /// <summary>
    /// Rich details for Bucket 2 (Slave ID + collision count + UI prompt message).
    /// </summary>
    public List<BlockedScanDuplicate> BlockedDuplicateDetails { get; init; } = new();

    /// <summary>
    /// Bucket 3 ("Found Box"): Pure unique (`Count() == 1`), Checkpost-verified identity packets
    /// ready to be dispatched to their driver calculation ("Ghar") or mapped into the system.
    /// </summary>
    public List<ModbusDevicePacket> UniqueFoundDevices { get; init; } = new();

    /// <summary>
    /// Dispatched engineering values calculated for each packet in <see cref="UniqueFoundDevices"/>.
    /// </summary>
    public Dictionary<byte, DriverCalculationResult> DispatchedCalculations { get; init; } = new();

    /// <summary>
    /// Count of slave addresses that answered with unrecognizable noise/corrupted frames dropped at Checkpost.
    /// </summary>
    public int DroppedNoiseFrames { get; set; }
}
