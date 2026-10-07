using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Services;
using ScadaDevice = scada_demo_test.Domain.Entities.Device;

namespace scada_demo_test.Maui.ViewModels;

public class DeviceItemModel
{
    public ScadaDevice Device { get; set; } = new();
    public string LiveMetricValue { get; set; } = "124.5 L/min";
    public string StatusBadgeColor { get; set; } = "#10B981";
    public string ProtocolText { get; set; } = "Modbus RTU";
    public string FormattedLastSeen => Device.LastSeenAt?.ToLocalTime().ToString("MMM dd, HH:mm:ss") ?? "Recently";
}

public partial class DevicesViewModel : BaseViewModel
{
    private readonly FirebaseScadaService _firebase;
    private List<ScadaDevice> _rawDevices = new();

    [ObservableProperty]
    private ObservableCollection<DeviceItemModel> _devices = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _onlineCount;

    [ObservableProperty]
    private int _totalDevicesCount;

    public DevicesViewModel(FirebaseScadaService firebase)
    {
        _firebase = firebase;
        Title = "Telemetry Devices & Sensors";
    }

    [RelayCommand]
    public async Task LoadDevicesAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            _rawDevices = await _firebase.GetDevicesAsync();
            var readings = await _firebase.GetRecentSensorReadingsAsync(200);

            ApplyFilter(readings);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Could not load devices: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter(List<SensorReading>? readings = null)
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _rawDevices
            : _rawDevices.Where(d =>
                d.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                d.ExternalId.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (d.IpAddress != null && d.IpAddress.Contains(SearchText, StringComparison.OrdinalIgnoreCase))).ToList();

        Devices.Clear();
        foreach (var dev in filtered)
        {
            var match = readings?.FirstOrDefault(r => r.DeviceId == dev.Id);
            string liveVal = match != null ? $"{match.Value:F1} {match.Unit ?? "L/min"}" : "125.4 L/min";

            Devices.Add(new DeviceItemModel
            {
                Device = dev,
                LiveMetricValue = liveVal,
                StatusBadgeColor = dev.Status == Domain.Enums.DeviceStatus.Online ? "#10B981" : "#EF4444",
                ProtocolText = dev.Protocol.ToString()
            });
        }

        TotalDevicesCount = _rawDevices.Count;
        OnlineCount = _rawDevices.Count(d => d.Status == Domain.Enums.DeviceStatus.Online);
    }
}
