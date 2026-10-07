using scada_demo_test.Application.DTOs;

namespace scada_demo_test.Application.Interfaces;

// Smart & Fast bus scan: pings every Modbus slave address 1..255 on a gateway's
// RS-485 bus (short per-probe timeout, already-registered slaves auto-skipped) and
// identifies the meter type on the fly from its register signature - the raw
// candidate list is returned to the UI with nothing persisted. The operator then
// picks a result and maps it into a real Sensor (which provisions the telemetry
// table) in one click.
public interface ISmartScanService
{
    Task<SmartScanResultDto> ScanAsync(SmartScanRequest request, CancellationToken ct = default);
}