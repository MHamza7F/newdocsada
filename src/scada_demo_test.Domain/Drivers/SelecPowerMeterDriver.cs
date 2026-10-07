namespace scada_demo_test.Domain.Drivers;

/// <summary>
/// Selec RI-F200-C 3-Phase Power/Energy Meter
/// ------------------------------------------
/// Function Code 0x04 (Read Input Registers), IEEE-754 float32, LowWordFirst ("FLOAT REVERSE WORD").
///   Window 0: start 42, qty 2 -> Total Active Power (kW)
///   Window 1: start 58, qty 2 -> Active Energy Totalizer (kWh)
///   CorroborationWindow: start 64, qty 10 (inside documented 74-register core block)
/// </summary>
public class SelecPowerMeterDriver : ISensorDriver
{
    private static readonly IReadOnlyList<SensorReadWindow> Windows = new[]
    {
        new SensorReadWindow(0x04, 42, 2), // Total active power (kW)
        new SensorReadWindow(0x04, 58, 2)  // Active energy totalizer (kWh)
    };

    private static readonly SensorReadWindow ProofWindow = new(0x04, 64, 2);

    // Identity/disproof: the meter's serial number (documented @0x2AC in the
    // register map). Every programmed meter has a non-zero serial; an echo /
    // zero-filler returns zeros for everything it serves. Verified live on the
    // real RI-F200-C: FC04@0x2AC -> 006E7E19 (non-zero), while the gateway's
    // internal filler returns 00000000 at the same address.
    private static readonly SensorReadWindow DisproofSerialWindow = new(0x04, 0x02AC, 2);

    public string DriverKey => "SELEC_POWER_METER";
    public string SimpleName => "selec_power";
    public string DisplayName => "Selec RI-F200-C 3-Phase Power/Energy Meter";
    public string Description => "Three-phase power & energy analyzer (FC04 input registers 42/58, low-word-first float32)";

    public int DefaultSlaveAddress => 5;
    public int DefaultPollIntervalSeconds => 5;
    public ushort StartRegister => 42;
    public ushort RegisterQuantity => 2;
    public byte FunctionCode => 0x04;

    public string UnitPrimary => "kW";
    public string UnitSecondary => "kWh";
    public string PrimaryColumnName => "InstantaneousFlowRate";
    public string SecondaryColumnName => "AccumulatedTotalizer";

    public IReadOnlyList<SensorReadWindow> ReadWindows => Windows;
    public SensorReadWindow? CorroborationWindow => ProofWindow;
    public SensorReadWindow? DisproofWindow => DisproofSerialWindow;

    public bool ValidateDisproof(byte[] rawPayload)
    {
        if (rawPayload is null || rawPayload.Length != DisproofSerialWindow.RegisterQuantity * 2) return false;
        return rawPayload.Any(b => b != 0); // a programmed meter always has a serial
    }

    public ParsedTelemetry ParseData(byte[] rawModbusPayload)
    {
        if (rawModbusPayload is null || rawModbusPayload.Length < 4)
        {
            return new ParsedTelemetry { Success = false, ErrorCode = "INVALID_PAYLOAD" };
        }

        var kw = ModbusValueCodec.ReadFloat32LowWordFirst(rawModbusPayload, 0);
        var kwh = rawModbusPayload.Length >= 8
            ? ModbusValueCodec.ReadFloat32LowWordFirst(rawModbusPayload, 4)
            : 0.0;

        return new ParsedTelemetry
        {
            Success = true,
            PrimaryValue = Math.Round(kw, 4),
            SecondaryValue = Math.Round(kwh, 3)
        };
    }

    public WindowTelemetry ParseWindow(int windowIndex, byte[] rawModbusPayload)
    {
        if (rawModbusPayload is null || rawModbusPayload.Length < 4)
        {
            return new WindowTelemetry();
        }

        var val = ModbusValueCodec.ReadFloat32LowWordFirst(rawModbusPayload, 0);
        return windowIndex switch
        {
            0 => new WindowTelemetry { PrimaryValue = Math.Round(val, 4) },
            1 => new WindowTelemetry { SecondaryValue = Math.Round(val, 3) },
            _ => new WindowTelemetry()
        };
    }

    public bool ValidatePayloadStructure(byte[] rawPayload) => rawPayload != null && rawPayload.Length == 8;

    public bool ValidateCorroboration(byte[] rawPayload)
    {
        if (rawPayload.Length != ProofWindow.RegisterQuantity * 2) return false;
        var value = ModbusValueCodec.ReadFloat32LowWordFirst(rawPayload, 0);
        return double.IsFinite(value) && Math.Abs(value) <= 1e12;
    }

    public bool ValidateValueBoundaries(byte[] rawPayload)
    {
        if (rawPayload == null || rawPayload.Length < 4) return false;
        var f1 = ModbusValueCodec.ReadFloat32LowWordFirst(rawPayload, 0);
        if (!double.IsFinite(f1) || Math.Abs(f1) > 1e7) return false;
        if (rawPayload.Length >= 8)
        {
            var f2 = ModbusValueCodec.ReadFloat32LowWordFirst(rawPayload, 4);
            if (!double.IsFinite(f2) || Math.Abs(f2) > 1e12) return false;
        }
        return true;
    }
}
