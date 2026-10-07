using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Maui.Models;
using scada_demo_test.Maui.Services;

namespace scada_demo_test.Maui.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private readonly ITelemetryCoordinator _coordinator;
    private readonly ISettingsService _settings;

    public ObservableCollection<FlowMeterModel> Meters => _coordinator.Meters;
    public ObservableCollection<StorageTankModel> Tanks => _coordinator.Tanks;
    public PlantSummaryStats Summary => _coordinator.SummaryStats;

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private string _connectionBadgeColor = "#06B6D4";

    [ObservableProperty]
    private bool _isFallbackActive;

    [ObservableProperty]
    private bool _isStreamPaused;

    public string PauseResumeButtonText => IsStreamPaused ? "▶ Resume" : "⏸ Pause";

    [ObservableProperty]
    private string _filterQuery = string.Empty;

    public DashboardViewModel(ITelemetryCoordinator coordinator, ISettingsService settings)
    {
        _coordinator = coordinator;
        _settings = settings;
        Title = "Plant Telemetry Monitor";

        _coordinator.OnTelemetryUpdated += UpdateStatus;
        UpdateStatus();
        _coordinator.Start();
    }

    private void UpdateStatus()
    {
        ConnectionStatus = _coordinator.ConnectionStatusText;
        ConnectionBadgeColor = _coordinator.ConnectionBadgeColor;
        IsFallbackActive = _coordinator.IsFallbackActive;
        IsStreamPaused = _coordinator.IsStreamPaused;
        OnPropertyChanged(nameof(PauseResumeButtonText));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            await _coordinator.RefreshNowAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleStreamPause()
    {
        _coordinator.IsStreamPaused = !_coordinator.IsStreamPaused;
        IsStreamPaused = _coordinator.IsStreamPaused;
    }

    [RelayCommand]
    private async Task GenerateReportAsync(FlowMeterModel meter)
    {
        if (meter == null) return;

        string message = $"Generated Quick Shift Audit Report for {meter.DeviceName} ({meter.DeviceExternalId})\n\n" +
                         $"• Current Flow Rate: {meter.FormattedFlowRate}\n" +
                         $"• Cumulative Total: {meter.FormattedTotalizer}\n" +
                         $"• Node Status: {meter.Status}\n" +
                         $"• Heartbeat Timestamp: {meter.FormattedLastSeen}";

        if (Shell.Current != null)
        {
            await Shell.Current.DisplayAlert("📄 Meter Audit Report", message, "Dismiss");
        }
    }
}