namespace scada_demo_test.Domain.Enums;

public enum ProtocolType
{
    ModbusRtu,
    ModbusTcp,
    Mqtt,
    OpcUa   // future-proofing for PLC/SCADA integration
}
