using CommunityToolkit.Mvvm.ComponentModel;

namespace scada_demo_test.Maui.Models;

/// <summary>
/// Observable model for an industrial storage tank mirroring domain StorageTank entity.
/// </summary>
public partial class StorageTankModel : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _tankCode = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private double _capacityLiters = 50000;

    [ObservableProperty]
    private double _currentVolumeLiters = 32500;

    [ObservableProperty]
    private double _levelPercentage = 65.0;

    [ObservableProperty]
    private double _temperatureCelsius = 24.5;

    [ObservableProperty]
    private string _status = "Normal"; // Normal, Filling, Draining, HighAlert, LowAlert

    [ObservableProperty]
    private string _liquidType = "Industrial Water";

    [ObservableProperty]
    private double _inletFlowRate = 120.5;

    [ObservableProperty]
    private double _outletFlowRate = 95.2;

    [ObservableProperty]
    private DateTime _lastUpdatedAt = DateTime.UtcNow;

    /// <summary>
    /// Progress ratio for UI bars (0.0 to 1.0)
    /// </summary>
    public double LevelRatio => Math.Clamp(LevelPercentage / 100.0, 0.0, 1.0);

    /// <summary>
    /// Formatted level percentage.
    /// </summary>
    public string FormattedLevel => $"{LevelPercentage:0.0}%";

    /// <summary>
    /// Formatted current volume in Liters.
    /// </summary>
    public string FormattedVolume => $"{CurrentVolumeLiters:N0} L";

    /// <summary>
    /// Formatted capacity in Liters.
    /// </summary>
    public string FormattedCapacity => $"{CapacityLiters:N0} L";

    /// <summary>
    /// Formatted temperature readout.
    /// </summary>
    public string FormattedTemperature => $"{TemperatureCelsius:0.0} °C";

    /// <summary>
    /// Net flow delta (Inlet - Outlet)
    /// </summary>
    public double NetFlowRate => InletFlowRate - OutletFlowRate;

    public string FormattedNetFlow => NetFlowRate >= 0 ? $"+{NetFlowRate:0.0} L/m" : $"{NetFlowRate:0.0} L/m";

    /// <summary>
    /// Dynamic color indicator depending on fluid level and state.
    /// </summary>
    public string LevelColor
    {
        get
        {
            if (LevelPercentage >= 90) return "#EF4444"; // Red / High Alert
            if (LevelPercentage <= 15) return "#F59E0B"; // Amber / Low Alert
            return "#06B6D4"; // Cyan normal fluid
        }
    }

    partial void OnLevelPercentageChanged(double value)
    {
        OnPropertyChanged(nameof(LevelRatio));
        OnPropertyChanged(nameof(FormattedLevel));
        OnPropertyChanged(nameof(LevelColor));
    }

    partial void OnCurrentVolumeLitersChanged(double value)
    {
        OnPropertyChanged(nameof(FormattedVolume));
    }

    partial void OnCapacityLitersChanged(double value)
    {
        OnPropertyChanged(nameof(FormattedCapacity));
    }

    partial void OnTemperatureCelsiusChanged(double value)
    {
        OnPropertyChanged(nameof(FormattedTemperature));
    }

    partial void OnInletFlowRateChanged(double value)
    {
        OnPropertyChanged(nameof(NetFlowRate));
        OnPropertyChanged(nameof(FormattedNetFlow));
    }

    partial void OnOutletFlowRateChanged(double value)
    {
        OnPropertyChanged(nameof(NetFlowRate));
        OnPropertyChanged(nameof(FormattedNetFlow));
    }
}
