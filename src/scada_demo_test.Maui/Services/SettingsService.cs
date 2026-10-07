namespace scada_demo_test.Maui.Services;

/// <summary>
/// Persists and manages mobile app configuration using Microsoft.Maui.Storage.Preferences.
/// </summary>
public class SettingsService : ISettingsService
{
    private const string ApiBaseUrlKey = "scada_api_base_url";
    private const string IsSimulationModeKey = "scada_is_sim_mode";
    private const string PollingIntervalKey = "scada_polling_interval_ms";
    private const string SimulatedMeterCountKey = "scada_sim_meter_count";
    private const string AutoFailoverKey = "scada_auto_failover";

    public event Action? OnSettingsChanged;

    public string ApiBaseUrl
    {
        get => Preferences.Default.Get(ApiBaseUrlKey, "http://192.168.1.15:5080");
        set
        {
            if (Preferences.Default.Get(ApiBaseUrlKey, string.Empty) != value)
            {
                Preferences.Default.Set(ApiBaseUrlKey, value?.TrimEnd('/') ?? string.Empty);
                OnSettingsChanged?.Invoke();
            }
        }
    }

    public bool IsSimulationMode
    {
        get => Preferences.Default.Get(IsSimulationModeKey, true); // Default to true since hardware is offline
        set
        {
            if (Preferences.Default.Get(IsSimulationModeKey, true) != value)
            {
                Preferences.Default.Set(IsSimulationModeKey, value);
                OnSettingsChanged?.Invoke();
            }
        }
    }

    public int PollingIntervalMs
    {
        get => Preferences.Default.Get(PollingIntervalKey, 1500);
        set
        {
            if (Preferences.Default.Get(PollingIntervalKey, 1500) != value)
            {
                Preferences.Default.Set(PollingIntervalKey, Math.Clamp(value, 500, 10000));
                OnSettingsChanged?.Invoke();
            }
        }
    }

    public int SimulatedMeterCount
    {
        get => Preferences.Default.Get(SimulatedMeterCountKey, 6);
        set
        {
            if (Preferences.Default.Get(SimulatedMeterCountKey, 6) != value)
            {
                Preferences.Default.Set(SimulatedMeterCountKey, Math.Clamp(value, 1, 14));
                OnSettingsChanged?.Invoke();
            }
        }
    }

    public bool AutoFailoverToSimulation
    {
        get => Preferences.Default.Get(AutoFailoverKey, true);
        set
        {
            if (Preferences.Default.Get(AutoFailoverKey, true) != value)
            {
                Preferences.Default.Set(AutoFailoverKey, value);
                OnSettingsChanged?.Invoke();
            }
        }
    }
}
