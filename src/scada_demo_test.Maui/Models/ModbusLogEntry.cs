namespace scada_demo_test.Maui.Models;

/// <summary>
/// Represents a raw Modbus TCP / RTU telemetry packet log entry mimicking value.txt.
/// </summary>
public class ModbusLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string DeviceId { get; set; } = "norvi-slave-1";
    public int SlaveId { get; set; } = 1;
    public string FunctionCode { get; set; } = "03 Read Holding Registers";
    public int RegisterAddress { get; set; } = 40001;
    public string Metric { get; set; } = "FlowRate";
    public double Value { get; set; }
    public string Unit { get; set; } = "L/min";
    public string RawHexPayload { get; set; } = "[01 03 04 41 48 00 00 9B 42]";
    public string Status { get; set; } = "ACK / CRC OK";
    public bool IsError { get; set; }

    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    public string FormattedValue => $"{Value:0.0} {Unit}";
    public string StatusColor => IsError ? "#EF4444" : "#10B981";
}
