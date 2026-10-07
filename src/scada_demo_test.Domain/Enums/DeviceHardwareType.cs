namespace scada_demo_test.Domain.Enums;

// Physical edge gateway / controller hardware hosting the Modbus or network interface.
public enum DeviceHardwareType
{
    // Norvi ESP32-based PLC / controller (Master on the RS-485 bus).
    NorviESP32 = 0,

    // USR-W610 Modbus RS485 to Wi-Fi/Ethernet gateway (transparent TCP bridge).
    UsrW610 = 1
}