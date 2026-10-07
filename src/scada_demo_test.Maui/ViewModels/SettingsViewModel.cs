using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Maui.Services;

namespace scada_demo_test.Maui.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settings;
    private readonly IScadaApiService _apiService;
    private readonly ITelemetryCoordinator _coordinator;

    [ObservableProperty]
    private string _apiBaseUrl = string.Empty;

    [ObservableProperty]
    private bool _isSimulationMode;

    [ObservableProperty]
    private int _pollingIntervalMs;

    [ObservableProperty]
    private int _simulatedMeterCount;

    [ObservableProperty]
    private bool _autoFailoverToSimulation;

    [ObservableProperty]
    private string _connectionTestResult = string.Empty;

    public bool HasTestResult => !string.IsNullOrEmpty(ConnectionTestResult);

    [ObservableProperty]
    private string _testResultColor = "#94A3B8";

    public SettingsViewModel(
        ISettingsService settings,
        IScadaApiService apiService,
        ITelemetryCoordinator coordinator)
    {
        _settings = settings;
        _apiService = apiService;
        _coordinator = coordinator;
        Title = "System Configuration";

        LoadSettings();
    }

    private void LoadSettings()
    {
        ApiBaseUrl = _settings.ApiBaseUrl;
        IsSimulationMode = _settings.IsSimulationMode;
        PollingIntervalMs = _settings.PollingIntervalMs;
        SimulatedMeterCount = _settings.SimulatedMeterCount;
        AutoFailoverToSimulation = _settings.AutoFailoverToSimulation;
    }

    partial void OnApiBaseUrlChanged(string value) => _settings.ApiBaseUrl = value;
    partial void OnIsSimulationModeChanged(bool value) => _settings.IsSimulationMode = value;
    partial void OnPollingIntervalMsChanged(int value) => _settings.PollingIntervalMs = value;
    partial void OnSimulatedMeterCountChanged(int value) => _settings.SimulatedMeterCount = value;
    partial void OnAutoFailoverToSimulationChanged(bool value) => _settings.AutoFailoverToSimulation = value;
    partial void OnConnectionTestResultChanged(string value) => OnPropertyChanged(nameof(HasTestResult));

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        ConnectionTestResult = "Pinging API endpoint...";
        TestResultColor = "#F59E0B";

        try
        {
            var (success, msg, latency) = await _apiService.TestConnectionAsync();
            if (success)
            {
                ConnectionTestResult = $"✓ Success: {msg} ({latency}ms)";
                TestResultColor = "#10B981";
            }
            else
            {
                ConnectionTestResult = $"✗ {msg} ({latency}ms)";
                TestResultColor = "#EF4444";
            }
        }
        catch (Exception ex)
        {
            ConnectionTestResult = $"✗ Error: {ex.Message}";
            TestResultColor = "#EF4444";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        ApiBaseUrl = "http://192.168.1.15:5080";
        IsSimulationMode = true;
        PollingIntervalMs = 1500;
        SimulatedMeterCount = 6;
        AutoFailoverToSimulation = true;
        ConnectionTestResult = "Settings reset to default industrial simulation mode.";
        TestResultColor = "#06B6D4";
    }
}
