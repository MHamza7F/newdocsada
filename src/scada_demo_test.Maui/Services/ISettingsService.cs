namespace scada_demo_test.Maui.Services;

public interface ISettingsService
{
    string ApiBaseUrl { get; set; }
    bool IsSimulationMode { get; set; }
    int PollingIntervalMs { get; set; }
    int SimulatedMeterCount { get; set; }
    bool AutoFailoverToSimulation { get; set; }
    event Action? OnSettingsChanged;
}
