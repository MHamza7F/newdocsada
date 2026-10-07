using CommunityToolkit.Mvvm.ComponentModel;

namespace scada_demo_test.Maui.Models;

/// <summary>
/// Observable model representing a physical or simulated industrial flow meter.
/// Mirrors the domain attributes and live telemetry streams (FlowRate, Totalizer, Status).
/// </summary>
public partial class FlowMeterModel : ObservableObject
{
    [ObservableProperty]
    private string _deviceExternalId = string.Empty;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private double _flowRate;

    [ObservableProperty]
    private string _flowUnit = "L/min";

    [ObservableProperty]
    private double _totalizer;

    [ObservableProperty]
    private string _totalizerUnit = "L";

    [ObservableProperty]
    private bool _isOnline = true;

    [ObservableProperty]
    private string _status = "Online"; // Online, Offline, Warning

    [ObservableProperty]
    private DateTime _lastSeen = DateTime.UtcNow;

    [ObservableProperty]
    private int? _modbusSlaveId;

    [ObservableProperty]
    private string? _ipAddress;

    [ObservableProperty]
    private double _previousFlowRate;

    [ObservableProperty]
    private bool _isFlowIncreasing;

    /// <summary>
    /// Formatted flow rate display string.
    /// </summary>
    public string FormattedFlowRate => $"{FlowRate:0.0} {FlowUnit}";

    /// <summary>
    /// Formatted totalizer cumulative display string.
    /// </summary>
    public string FormattedTotalizer => $"{Totalizer:0.0} {TotalizerUnit}";

    /// <summary>
    /// Formatted heartbeat timestamp string.
    /// </summary>
    public string FormattedLastSeen => LastSeen.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>
    /// Visual status badge color for UI.
    /// </summary>
    public string StatusColor => IsOnline ? "#10B981" : "#64748B";

    /// <summary>
    /// Flow rate trend indicator icon or text.
    /// </summary>
    public string TrendIndicator => IsFlowIncreasing ? "▲" : "▼";

    public string TrendColor => IsFlowIncreasing ? "#06B6D4" : "#94A3B8";

    partial void OnFlowRateChanged(double oldValue, double newValue)
    {
        PreviousFlowRate = oldValue;
        IsFlowIncreasing = newValue >= oldValue;
        OnPropertyChanged(nameof(FormattedFlowRate));
        OnPropertyChanged(nameof(TrendIndicator));
        OnPropertyChanged(nameof(TrendColor));
    }

    partial void OnTotalizerChanged(double value)
    {
        OnPropertyChanged(nameof(FormattedTotalizer));
    }

    partial void OnIsOnlineChanged(bool value)
    {
        Status = value ? "Online" : "Offline";
        OnPropertyChanged(nameof(StatusColor));
    }

    partial void OnLastSeenChanged(DateTime value)
    {
        OnPropertyChanged(nameof(FormattedLastSeen));
    }
}
