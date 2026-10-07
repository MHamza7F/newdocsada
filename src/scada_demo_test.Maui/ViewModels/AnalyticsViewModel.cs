using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Maui.Models;
using scada_demo_test.Maui.Services;

namespace scada_demo_test.Maui.ViewModels;

public partial class AnalyticsViewModel : BaseViewModel
{
    private readonly ITelemetryCoordinator _coordinator;

    public ObservableCollection<StorageTankModel> Tanks => _coordinator.Tanks;
    public ObservableCollection<SensorGridRow> SensorGrid { get; } = new();

    [ObservableProperty]
    private int _selectedTabIndex; // 0 = Tanks, 1 = Sensor Grid, 2 = Trend Stats

    [ObservableProperty]
    private double _totalStorageCapacity;

    [ObservableProperty]
    private double _totalStoredFluid;

    [ObservableProperty]
    private double _averagePlantLevel;

    [ObservableProperty]
    private string _formattedTotalStorage = string.Empty;

    [ObservableProperty]
    private string _formattedStoredFluid = string.Empty;

    [ObservableProperty]
    private string _formattedAvgLevel = string.Empty;

    public AnalyticsViewModel(ITelemetryCoordinator coordinator)
    {
        _coordinator = coordinator;
        Title = "Analytics & Storage";

        _coordinator.OnTelemetryUpdated += UpdateAnalytics;
        UpdateAnalytics();
    }

    private void UpdateAnalytics()
    {
        double cap = 0;
        double vol = 0;
        double levelSum = 0;

        foreach (var t in Tanks)
        {
            cap += t.CapacityLiters;
            vol += t.CurrentVolumeLiters;
            levelSum += t.LevelPercentage;
        }

        TotalStorageCapacity = cap;
        TotalStoredFluid = vol;
        AveragePlantLevel = Tanks.Count > 0 ? (levelSum / Tanks.Count) : 0;

        FormattedTotalStorage = $"{TotalStorageCapacity:N0} L";
        FormattedStoredFluid = $"{TotalStoredFluid:N0} L";
        FormattedAvgLevel = $"{AveragePlantLevel:0.0}%";

        RebuildSensorGrid();
    }

    private void RebuildSensorGrid()
    {
        SensorGrid.Clear();

        // Add meter rows
        foreach (var m in _coordinator.Meters)
        {
            SensorGrid.Add(new SensorGridRow
            {
                DeviceTag = m.DeviceExternalId,
                Parameter = $"{m.DeviceName} (Flow)",
                CurrentValue = m.FlowRate,
                Unit = m.FlowUnit,
                MinValue = 0.0,
                MaxValue = 100.0,
                Status = m.IsOnline ? "Normal" : "Fault",
                UpdateTime = m.FormattedLastSeen
            });

            SensorGrid.Add(new SensorGridRow
            {
                DeviceTag = m.DeviceExternalId,
                Parameter = $"{m.DeviceName} (Totalizer)",
                CurrentValue = m.Totalizer,
                Unit = m.TotalizerUnit,
                MinValue = 0.0,
                MaxValue = 999999.0,
                Status = m.IsOnline ? "Accumulating" : "Frozen",
                UpdateTime = m.FormattedLastSeen
            });
        }

        // Add tank rows
        foreach (var t in _coordinator.Tanks)
        {
            SensorGrid.Add(new SensorGridRow
            {
                DeviceTag = t.TankCode,
                Parameter = $"{t.Name} (Level)",
                CurrentValue = t.LevelPercentage,
                Unit = "%",
                MinValue = 10.0,
                MaxValue = 90.0,
                Status = t.Status,
                UpdateTime = t.LastUpdatedAt.ToLocalTime().ToString("HH:mm:ss")
            });
        }
    }

    [RelayCommand]
    private void SelectTab(int index)
    {
        SelectedTabIndex = index;
    }
}
