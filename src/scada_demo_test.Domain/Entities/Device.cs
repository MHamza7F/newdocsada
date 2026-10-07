using scada_demo_test.Domain.Enums;

namespace scada_demo_test.Domain.Entities;

// One entity for every PHYSICAL gateway / controller on the plant floor:
// a Norvi ESP32 (PLC/Controller) or a USR-W610 (RS485 -> Wi-Fi/Ethernet bridge).
// Sensors (slave meters on the RS-485 bus) are attached to these devices.
public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ExternalId { get; set; } = string.Empty;   // e.g. "DEV-01", "norvi-slave-1"
    public string Name { get; set; } = string.Empty;

    // DeviceType is legacy (used to mis-label meters as devices). From here on the
    // hardware class of the gateway is described by HardwareType.
    public DeviceType DeviceType { get; set; } = DeviceType.Gateway;
    public ProtocolType Protocol { get; set; } = ProtocolType.ModbusTcp;
    public DeviceStatus Status { get; set; } = DeviceStatus.Offline;

    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }

    public Guid? PlantZoneId { get; set; }
    public PlantZone? PlantZone { get; set; }

    // ---- IIoT gateway communication settings (user-driven, no hardcoding) ----
    public DeviceHardwareType HardwareType { get; set; } = DeviceHardwareType.NorviESP32;
    public string? IpAddress { get; set; }
    public int Port { get; set; } = 502;
    public int BaudRate { get; set; } = 9600;
    public ModbusParity Parity { get; set; } = ModbusParity.None;
    public int StopBits { get; set; } = 1;
    public int TimeoutMs { get; set; } = 2000;
    public int MaxSensorCapacity { get; set; } = 25;
    public bool IsOnline { get; set; }

    // Legacy Modbus slave id (only relevant for direct-RTU setups).
    public int? ModbusSlaveId { get; set; }

    // Optional metadata for the future 3D model (position on the digital twin)
    public double? PositionX { get; set; }
    public double? PositionY { get; set; }
    public double? PositionZ { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }

    public ICollection<SensorReading> Readings { get; set; } = new List<SensorReading>();

    // Attached slave sensors (meters / transducers on this gateway's bus).
    public ICollection<Sensor> Sensors { get; set; } = new List<Sensor>();
}