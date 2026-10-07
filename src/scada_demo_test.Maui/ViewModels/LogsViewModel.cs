using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Maui.Models;
using scada_demo_test.Maui.Services;

namespace scada_demo_test.Maui.ViewModels;

public partial class LogsViewModel : BaseViewModel
{
    private readonly ITelemetryCoordinator _coordinator;

    public ObservableCollection<ModbusLogEntry> ModbusLogs => _coordinator.ModbusLogs;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private bool _isStreamPaused;

    public string PauseResumeButtonText => IsStreamPaused ? "▶ Resume Stream" : "⏸ Pause Stream";

    [ObservableProperty]
    private int _logCount;

    public LogsViewModel(ITelemetryCoordinator coordinator)
    {
        _coordinator = coordinator;
        Title = "Modbus Register Logs";

        _coordinator.OnTelemetryUpdated += UpdateLogCount;
        UpdateLogCount();
    }

    private void UpdateLogCount()
    {
        LogCount = ModbusLogs.Count;
        IsStreamPaused = _coordinator.IsStreamPaused;
        OnPropertyChanged(nameof(PauseResumeButtonText));
    }

    [RelayCommand]
    private void TogglePause()
    {
        _coordinator.IsStreamPaused = !_coordinator.IsStreamPaused;
        IsStreamPaused = _coordinator.IsStreamPaused;
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _coordinator.ClearLogs();
        LogCount = 0;
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        if (ModbusLogs.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== SCADA INDUSTRIAL MODBUS TELEMETRY LOG DUMP (value.txt) ===");
        sb.AppendLine($"Export Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("Timestamp | Device ID | Slave | Register | Metric | Value | Hex Frame | Status");
        sb.AppendLine("--------------------------------------------------------------------------------");

        foreach (var log in ModbusLogs.Take(25))
        {
            sb.AppendLine($"{log.FormattedTimestamp} | {log.DeviceId} | Slave {log.SlaveId} | {log.RegisterAddress} | {log.Metric} | {log.FormattedValue} | {log.RawHexPayload} | {log.Status}");
        }

        await Clipboard.Default.SetTextAsync(sb.ToString());

        if (Shell.Current != null)
        {
            await Shell.Current.DisplayAlert("📋 Copied to Clipboard", "Latest 25 Modbus register log frames copied to clipboard.", "OK");
        }
    }
}