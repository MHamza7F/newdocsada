using scada_demo_test.Domain.Enums;

namespace scada_demo_test.Domain.Drivers;

// A sensor driver is an isolated module that knows EVERYTHING needed to
// communicate with one family of slave instruments over Modbus: which registers
// to poll, how to convert the raw register payload into engineering values, and
// which physical columns its isolated telemetry table must have.
//
// Drivers are pure specifications + parsers - no I/O, no persistence, so they can
// be unit-tested and safely run inside the polling worker. The values only exist
// for the LOWER register payload bytes (after the MBAP header + function code +
// byte-count byte), i.e. the raw register data.
public interface ISensorDriver
{
    // Unique registry key stored in the Sensors.SensorTypeKey column.
    string DriverKey { get; }

    // Short human-friendly key used to build the isolated telemetry table name
    // (e.g. "aosong", "kaifeng").
    string SimpleName { get; }

    // Name shown in the UI dropdown.
    string DisplayName { get; }
    string Description { get; }

    // Modbus register map (the primary/identifying window).
    byte FunctionCode { get; }          // 0x03 = Read Holding Registers, 0x04 = Read Input Registers
    ushort StartRegister { get; }
    ushort RegisterQuantity { get; }

    // Engineering units.
    string UnitPrimary { get; }
    string UnitSecondary { get; }

    // Physical value columns for the isolated telemetry table.
    string PrimaryColumnName { get; }
    string SecondaryColumnName { get; }

    // Manufacturer defaults used to pre-fill the "Add Sensor" form.
    int DefaultSlaveAddress { get; }
    int DefaultPollIntervalSeconds { get; }

    // Converts the raw register payload (register data only) into values.
    ParsedTelemetry ParseData(byte[] rawRegisters);

    // ------------------------------------------------------------------
    // Multi-window support (defaults keep single-window drivers unchanged).
    // ------------------------------------------------------------------

    // One register block to read. Most drivers need exactly one; a driver whose
    // parameters are scattered across a register map that must NOT be read in one
    // wide request declares several small windows instead.
    IReadOnlyList<SensorReadWindow> ReadWindows =>
        new[] { new SensorReadWindow(FunctionCode, StartRegister, RegisterQuantity) };

    // Parses the raw payload for one of the driver's own ReadWindows. windowIndex
    // is the index into ReadWindows. The default delegates to ParseData so every
    // single-window driver keeps working without changes; a null PrimaryValue /
    // SecondaryValue means "this window does not feed that column" (deliberately
    // distinct from a genuine 0 reading).
    WindowTelemetry ParseWindow(int windowIndex, byte[] rawRegisters)
    {
        var parsed = ParseData(rawRegisters);
        return new WindowTelemetry
        {
            PrimaryValue = parsed.Success ? parsed.PrimaryValue : null,
            SecondaryValue = parsed.Success ? parsed.SecondaryValue : null,
            ErrorCode = parsed.ErrorCode
        };
    }

    // ------------------------------------------------------------------
    // STEP 1: Unified Sensor Fingerprint & Validation Interface
    // ------------------------------------------------------------------
    DeviceHardwareType HardwareType => DeviceHardwareType.UsrW610;
    int ExpectedByteLength => ReadWindows.Sum(window => window.RegisterQuantity * 2);
    bool ValidatePayloadStructure(byte[] rawPayload) => rawPayload != null && rawPayload.Length == ExpectedByteLength;
    bool ValidateCorroboration(byte[] rawPayload) => CorroborationWindow is { } window &&
        rawPayload.Length == window.RegisterQuantity * 2 && rawPayload.Length == 4 &&
        ModbusValueCodec.ReadFloat32HighWordFirst(rawPayload, 0) is var value &&
        double.IsFinite(value) && Math.Abs(value) <= 1e12;
    bool ValidateValueBoundaries(byte[] rawPayload)
    {
        if (rawPayload == null || rawPayload.Length < ExpectedByteLength) return false;
        var parsed = ParseData(rawPayload);
        return parsed.Success && double.IsFinite(parsed.PrimaryValue) && double.IsFinite(parsed.SecondaryValue);
    }

    // An OPTIONAL extra block that the SCANNER reads to corroborate the driver's
    // identity. It is deliberately NOT part of ReadWindows, so the polling worker
    // never spends an extra request on it. null = no corroboration available.
    SensorReadWindow? CorroborationWindow => null;

    // ------------------------------------------------------------------
    // Disproof window (scanner-only identity guard).
    // ------------------------------------------------------------------
    // An OPTIONAL block the SCANNER reads ONLY the first time data arrives from an
    // address. A REAL meter serves this window with at least one non-zero byte
    // (e.g. the Selec RI-F200-C serial number - a programmed meter never reads
    // 0x0000). A gateway echo/zero-filler slave returns zeros for every register
    // it serves, so it can never satisfy a non-zero disproof. This kills the
    // false "4 families colliding" classification the old out-of-map probe
    // produced. null = driver has no disproof window.
    SensorReadWindow? DisproofWindow => null;

    // Must return false when the disproof payload carries NO physical identity
    // (all zeros). Returning true keeps the candidate alive; false rejects it.
    bool ValidateDisproof(byte[] rawPayload) =>
        rawPayload is { Length: > 0 } && rawPayload.Any(b => b != 0);
}

// One Modbus register block request.
public record SensorReadWindow(byte FunctionCode, ushort StartRegister, ushort RegisterQuantity);

// Result of parsing one window of a driver's own register map. Nullable values
// mean "this window does not provide that column".
public class WindowTelemetry
{
    public double? PrimaryValue { get; set; }
    public double? SecondaryValue { get; set; }
    public string? ErrorCode { get; set; }
}

// Result of parsing one polled register payload.
public class ParsedTelemetry
{
    public bool Success { get; set; } = true;
    public double PrimaryValue { get; set; }   // Temperature or FlowRate (m³/h)
    public double SecondaryValue { get; set; } // Relative Humidity or Totalizer (m³)
    public string? ErrorCode { get; set; }
}
