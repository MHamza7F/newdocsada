using scada_demo_test.Domain.Enums;

namespace scada_demo_test.Domain.Drivers;

// =====================================================================
// Driver 3 : Kaifeng IEMFL Electromagnetic (Water) Flowmeter
// ---------------------------------------------------------------------
// Default Slave ID   : 0x02
// Function Code      : 0x03 (Read Holding Registers)
// Windows            : Totalizer  @ 0x005A (90) qty 2  -> regs 90-91
//                      Flow Rate  @ 0x0062 (98) qty 2  -> regs 98-99
// Corroboration      : Proof      @ 0x005C (92) qty 2  -> regs 92-93 (live, non-zero)
// Serial framing     : 9600 baud, 8 data bits, No parity, 1 stop bit
// Conversion         : IEEE-754 32-bit float pairs (high-word-first)
//
// The register map scatters the two values, and a single WIDE read over the gap
// corrupts replies on a shared RS-485 bus, so the two values are read as two
// small requests (ReadWindows).
// =====================================================================
public class ElectromagneticFlowmeterDriver : ISensorDriver
{
    public string DriverKey => "KAIFENG_EM_FLOWMETER";
    public string SimpleName => "kaifeng_em";
    public string DisplayName => "Kaifeng IEMFL Electromagnetic Flowmeter (Water)";
    public string Description => "Electromagnetic water flow meter, IEEE-754 32-bit floats for Flow Rate and Totalizer (register map per ModbusMeterLibs electromagnetic profile).";

    public byte FunctionCode => 0x03;
    public ushort StartRegister => 0x005A; // 90 - totalizer identification window
    public ushort RegisterQuantity => 2;

    public string UnitPrimary => "m³/h";
    public string UnitSecondary => "m³";
    public string PrimaryColumnName => "InstantaneousFlowRate";
    public string SecondaryColumnName => "AccumulatedTotalizer";

    public int DefaultSlaveAddress => 0x02;
    public int DefaultPollIntervalSeconds => 3;

    public IReadOnlyList<SensorReadWindow> ReadWindows => new[]
    {
        new SensorReadWindow(0x03, 90, 2), // Totalizer (m³) regs 90-91
        new SensorReadWindow(0x03, 98, 2)  // Flow Rate (m³/h) regs 98-99
    };

    // The proof block @92 is a live, non-zero reading that no other driver serves,
    // used by the scanner to corroborate this family's identity.
    public SensorReadWindow? CorroborationWindow => new SensorReadWindow(0x03, 92, 2);

    public ParsedTelemetry ParseData(byte[] rawRegisters)
    {
        if (rawRegisters == null || rawRegisters.Length < 4)
        {
            return new ParsedTelemetry { Success = false, ErrorCode = "INVALID_PAYLOAD" };
        }

        var totalizer = ModbusValueCodec.ToFloat32(rawRegisters, 0);
        return new ParsedTelemetry
        {
            Success = true,
            SecondaryValue = Math.Round(totalizer, 2)
        };
    }

    public WindowTelemetry ParseWindow(int windowIndex, byte[] rawRegisters)
    {
        if (rawRegisters == null || rawRegisters.Length < 4)
        {
            return new WindowTelemetry { ErrorCode = "INVALID_PAYLOAD" };
        }

        return windowIndex == 0
            ? new WindowTelemetry { SecondaryValue = Math.Round(ModbusValueCodec.ToFloat32(rawRegisters, 0), 2) } // totalizer
            : new WindowTelemetry { PrimaryValue = Math.Round(ModbusValueCodec.ToFloat32(rawRegisters, 0), 2) };   // flow rate
    }

    public bool ValidatePayloadStructure(byte[] rawPayload) => rawPayload != null && rawPayload.Length == 8;

    public bool ValidateValueBoundaries(byte[] rawPayload)
    {
        if (rawPayload == null || rawPayload.Length < 4) return false;
        var f1 = ModbusValueCodec.ToFloat32(rawPayload, 0);
        if (!double.IsFinite(f1) || Math.Abs(f1) > 1e12) return false;
        if (rawPayload.Length >= 8)
        {
            var f2 = ModbusValueCodec.ToFloat32(rawPayload, 4);
            if (!double.IsFinite(f2) || Math.Abs(f2) > 1e12) return false;
        }
        return true;
    }
}
