using scada_demo_test.Domain.Enums;

namespace scada_demo_test.Domain.Drivers;

// =====================================================================
// Driver 1 : Aosong AQ3485 / AQ3485Y - Temperature & Humidity sensor
// ---------------------------------------------------------------------
// Default Slave ID   : 0x01
// Function Code      : 0x03 (Read Holding Registers)
// Start Register     : 0x0000
// Register Quantity  : 0x0002 (2 registers = 4 bytes)
// Serial framing     : 9600 baud, 8 data bits, No parity, 1 stop bit
// Conversion         : Register[0] unsigned 16-bit / 10 -> Humidity %RH  (LIVE ORDER)
//                      Register[1] signed 16-bit / 10   -> Temp °C
// NOTE: on the real bus register 0 carries HUMIDITY and register 1 carries
// TEMPERATURE (verified with a raw FC03 @0 qty2 probe: [471,294] -> 47.1 %RH /
// 29.4 °C). An earlier build had these swapped, which showed the ambient humidity
// as a bogus 45-77 "°C" temperature.
// =====================================================================
public class AosongAQ3485Driver : ISensorDriver
{
    public string DriverKey => "AOSONG_AQ3485";
    public string SimpleName => "aosong";
    public string DisplayName => "Aosong AQ3485/Y (Temperature & Humidity)";
    public string Description => "Digital temperature & relative humidity transmitter, 2 holding registers, 0.1 resolution.";

    public byte FunctionCode => 0x03;
    public ushort StartRegister => 0x0000;
    public ushort RegisterQuantity => 2;

    public string UnitPrimary => "°C";
    public string UnitSecondary => "%RH";
    public string PrimaryColumnName => "TemperatureC";
    public string SecondaryColumnName => "HumidityRH";

    public int DefaultSlaveAddress => 0x01;
    public int DefaultPollIntervalSeconds => 5;

    public ParsedTelemetry ParseData(byte[] rawRegisters)
    {
        if (rawRegisters == null || rawRegisters.Length < 4)
        {
            return new ParsedTelemetry { Success = false, ErrorCode = "INVALID_PAYLOAD" };
        }

        // Big-Endian Modbus register decoding. Register 0 = humidity, register 1 =
        // temperature (the real bus order - see the header note).
        ushort rawHum = (ushort)((rawRegisters[0] << 8) | rawRegisters[1]);
        short rawTemp = (short)((rawRegisters[2] << 8) | rawRegisters[3]);

        return new ParsedTelemetry
        {
            Success = true,
            PrimaryValue = Math.Round(rawTemp / 10.0, 1),
            SecondaryValue = Math.Round(rawHum / 10.0, 1)
        };
    }

    public bool ValidatePayloadStructure(byte[] rawPayload) => rawPayload != null && rawPayload.Length == 4;

    public bool ValidateValueBoundaries(byte[] rawPayload)
    {
        if (rawPayload == null || rawPayload.Length < 4) return false;
        ushort rawHum = (ushort)((rawPayload[0] << 8) | rawPayload[1]);
        short rawTemp = (short)((rawPayload[2] << 8) | rawPayload[3]);
        double hum = rawHum / 10.0;
        double temp = rawTemp / 10.0;
        // 0.0 %RH AND 0.0 °C together are physically impossible for air, and this
        // exact all-zero payload is what unprogrammed registers (e.g. a Kaifeng EM
        // serving zeros at registers 0-1) return - it once shadow-matched this driver
        // and beat a REAL meter into a fake duplicate conflict. Reject it.
        if (rawHum == 0 && rawTemp == 0) return false;
        return hum >= 0.0 && hum <= 100.0 && temp >= -40.0 && temp <= 85.0;
    }
}
