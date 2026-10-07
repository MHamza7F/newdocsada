using System.Collections.ObjectModel;
using scada_demo_test.Maui.Models;

namespace scada_demo_test.Maui.Services;

public enum TelemetryConnectionState
{
    LiveBackend,
    SimulationMode,
    FallbackSimulation,
    Connecting,
    Disconnected
}

public interface ITelemetryCoordinator
{
    ObservableCollection<FlowMeterModel> Meters { get; }
    ObservableCollection<StorageTankModel> Tanks { get; }
    ObservableCollection<ModbusLogEntry> ModbusLogs { get; }
    ObservableCollection<SystemAlertModel> Alerts { get; }
    PlantSummaryStats SummaryStats { get; }

    TelemetryConnectionState ConnectionState { get; }
    string ConnectionStatusText { get; }
    string ConnectionBadgeColor { get; }
    bool IsFallbackActive { get; }
    bool IsStreamPaused { get; set; }

    event Action? OnTelemetryUpdated;

    void Start();
    void Stop();
    Task RefreshNowAsync();
    void AcknowledgeAlert(Guid alertId);
    void ClearLogs();
}
