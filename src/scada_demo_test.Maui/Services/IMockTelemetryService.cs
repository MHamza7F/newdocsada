using scada_demo_test.Maui.Models;

namespace scada_demo_test.Maui.Services;

public interface IMockTelemetryService
{
    List<FlowMeterModel> GetInitialMeters(int count);
    List<StorageTankModel> GetInitialTanks();
    List<SystemAlertModel> GetInitialAlerts();
    
    /// <summary>
    /// Updates the meters in-place with realistic sinusoidal + noise fluctuations and increments totalizers.
    /// </summary>
    void TickMeters(List<FlowMeterModel> meters, double elapsedSeconds);

    /// <summary>
    /// Updates storage tank levels with realistic filling/draining rates.
    /// </summary>
    void TickTanks(List<StorageTankModel> tanks, double elapsedSeconds);

    /// <summary>
    /// Generates a streaming Modbus TCP/RTU packet log entry mimicking value.txt.
    /// </summary>
    ModbusLogEntry GenerateNextModbusLog(List<FlowMeterModel> meters);
}
