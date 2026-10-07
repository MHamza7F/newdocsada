using CommunityToolkit.Mvvm.ComponentModel;

namespace scada_demo_test.Maui.Models;

/// <summary>
/// Aggregated plant-wide telemetry statistics for the Dashboard KPI cards.
/// </summary>
public partial class PlantSummaryStats : ObservableObject
{
    [ObservableProperty]
    private double _totalFlowRate;

    [ObservableProperty]
    private double _totalCumulativeVolume;

    [ObservableProperty]
    private double _averageTankLevel;

    [ObservableProperty]
    private int _onlineMeterCount;

    [ObservableProperty]
    private int _totalMeterCount;

    [ObservableProperty]
    private int _activeAlertCount;

    public string FormattedTotalFlow => $"{TotalFlowRate:0.0} L/min";
    public string FormattedTotalVolume => $"{TotalCumulativeVolume:N0} L";
    public string FormattedAvgTankLevel => $"{AverageTankLevel:0.0}%";
    public string FormattedMeterStatus => $"{OnlineMeterCount} / {TotalMeterCount} Online";

    partial void OnTotalFlowRateChanged(double value) => OnPropertyChanged(nameof(FormattedTotalFlow));
    partial void OnTotalCumulativeVolumeChanged(double value) => OnPropertyChanged(nameof(FormattedTotalVolume));
    partial void OnAverageTankLevelChanged(double value) => OnPropertyChanged(nameof(FormattedAvgTankLevel));
    partial void OnOnlineMeterCountChanged(int value) => OnPropertyChanged(nameof(FormattedMeterStatus));
    partial void OnTotalMeterCountChanged(int value) => OnPropertyChanged(nameof(FormattedMeterStatus));
}

/// <summary>
/// Row item for the Data and Analytics tabular sensor grid.
/// </summary>
public class SensorGridRow
{
    public string DeviceTag { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
    public double CurrentValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public string Status { get; set; } = "Normal";
    public string UpdateTime { get; set; } = string.Empty;

    public string FormattedCurrent => $"{CurrentValue:0.0} {Unit}";
    public string FormattedRange => $"{MinValue:0.0} - {MaxValue:0.0} {Unit}";
}