using System.Collections.ObjectModel;
using scada_demo_test.Maui.Models;

namespace scada_demo_test.Maui.Services;

/// <summary>
/// Master telemetry coordinator and background polling engine.
/// Manages live data streams, fallback switching, observable collections, and plant statistics.
/// </summary>
public class TelemetryCoordinator : ITelemetryCoordinator, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IMockTelemetryService _mockService;
    private readonly IScadaApiService _apiService;

    private readonly IDispatcherTimer _timer;
    private readonly object _lock = new();
    private bool _isDisposed;
    private bool _isTicking;

    public ObservableCollection<FlowMeterModel> Meters { get; } = new();
    public ObservableCollection<StorageTankModel> Tanks { get; } = new();
    public ObservableCollection<ModbusLogEntry> ModbusLogs { get; } = new();
    public ObservableCollection<SystemAlertModel> Alerts { get; } = new();
    public PlantSummaryStats SummaryStats { get; } = new();

    public TelemetryConnectionState ConnectionState { get; private set; } = TelemetryConnectionState.SimulationMode;
    public string ConnectionStatusText { get; private set; } = "Simulation Mode Active";
    public string ConnectionBadgeColor { get; private set; } = "#F59E0B";
    public bool IsFallbackActive { get; private set; }
    public bool IsStreamPaused { get; set; }

    public event Action? OnTelemetryUpdated;

    public TelemetryCoordinator(
        ISettingsService settings,
        IMockTelemetryService mockService,
        IScadaApiService apiService)
    {
        _settings = settings;
        _mockService = mockService;
        _apiService = apiService;

        _settings.OnSettingsChanged += HandleSettingsChanged;

        // Initialize collections with realistic out-of-the-box data
        InitializeData();

        // Create UI-safe DispatcherTimer
        _timer = Application.Current?.Dispatcher.CreateTimer() ?? new FallbackTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(_settings.PollingIntervalMs);
        _timer.Tick += async (s, e) => await OnTimerTickAsync();
    }

    private void InitializeData()
    {
        var initialMeters = _mockService.GetInitialMeters(_settings.SimulatedMeterCount);
        Meters.Clear();
        foreach (var m in initialMeters) Meters.Add(m);

        var initialTanks = _mockService.GetInitialTanks();
        Tanks.Clear();
        foreach (var t in initialTanks) Tanks.Add(t);

        var initialAlerts = _mockService.GetInitialAlerts();
        Alerts.Clear();
        foreach (var a in initialAlerts) Alerts.Add(a);

        UpdatePlantSummary();
        UpdateConnectionState();
    }

    public void Start()
    {
        if (!_timer.IsRunning)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(_settings.PollingIntervalMs);
            _timer.Start();
        }
    }

    public void Stop()
    {
        if (_timer.IsRunning)
        {
            _timer.Stop();
        }
    }

    public async Task RefreshNowAsync()
    {
        await OnTimerTickAsync();
    }

    private async Task OnTimerTickAsync()
    {
        if (_isTicking || IsStreamPaused) return;

        try
        {
            _isTicking = true;
            double elapsed = _timer.Interval.TotalSeconds;

            if (_settings.IsSimulationMode)
            {
                // Pure simulation mode
                IsFallbackActive = false;
                ConnectionState = TelemetryConnectionState.SimulationMode;
                ConnectionStatusText = "Simulation Active (Hardware Offline)";
                ConnectionBadgeColor = "#06B6D4";

                RunSimulationTick(elapsed);
            }
            else
            {
                // Attempt live backend fetch
                var liveMeters = await _apiService.GetLatestDeviceSnapshotAsync();
                var liveTanks = await _apiService.GetTanksAsync();

                if (liveMeters != null && liveMeters.Count > 0)
                {
                    // Live data received successfully
                    IsFallbackActive = false;
                    ConnectionState = TelemetryConnectionState.LiveBackend;
                    ConnectionStatusText = "Connected to Live Backend";
                    ConnectionBadgeColor = "#10B981";

                    UpdateLiveMeters(liveMeters);
                    if (liveTanks != null && liveTanks.Count > 0)
                    {
                        UpdateLiveTanks(liveTanks);
                    }
                }
                else
                {
                    // Backend failed - check fallback policy
                    if (_settings.AutoFailoverToSimulation)
                    {
                        IsFallbackActive = true;
                        ConnectionState = TelemetryConnectionState.FallbackSimulation;
                        ConnectionStatusText = "API Unreachable - Fallback Simulation Active";
                        ConnectionBadgeColor = "#EF4444";

                        RunSimulationTick(elapsed);
                    }
                    else
                    {
                        ConnectionState = TelemetryConnectionState.Disconnected;
                        ConnectionStatusText = "Backend Disconnected";
                        ConnectionBadgeColor = "#64748B";

                        foreach (var m in Meters) m.IsOnline = false;
                    }
                }
            }

            // Append streaming Modbus register log
            var log = _mockService.GenerateNextModbusLog(Meters.ToList());
            ModbusLogs.Insert(0, log);
            while (ModbusLogs.Count > 50)
            {
                ModbusLogs.RemoveAt(ModbusLogs.Count - 1);
            }

            UpdatePlantSummary();
            OnTelemetryUpdated?.Invoke();
        }
        catch (Exception)
        {
            // Fail-safe protection
        }
        finally
        {
            _isTicking = false;
        }
    }

    private void RunSimulationTick(double elapsed)
    {
        _mockService.TickMeters(Meters.ToList(), elapsed);
        _mockService.TickTanks(Tanks.ToList(), elapsed);
    }

    private void UpdateLiveMeters(List<FlowMeterModel> incoming)
    {
        foreach (var inc in incoming)
        {
            var existing = Meters.FirstOrDefault(m => m.DeviceExternalId == inc.DeviceExternalId);
            if (existing != null)
            {
                existing.FlowRate = inc.FlowRate;
                existing.FlowUnit = inc.FlowUnit;
                existing.Totalizer = inc.Totalizer;
                existing.TotalizerUnit = inc.TotalizerUnit;
                existing.IsOnline = inc.IsOnline;
                existing.LastSeen = inc.LastSeen;
            }
            else
            {
                Meters.Add(inc);
            }
        }
    }

    private void UpdateLiveTanks(List<StorageTankModel> incoming)
    {
        foreach (var inc in incoming)
        {
            var existing = Tanks.FirstOrDefault(t => t.Id == inc.Id || t.TankCode == inc.TankCode);
            if (existing != null)
            {
                existing.CurrentVolumeLiters = inc.CurrentVolumeLiters;
                existing.LevelPercentage = inc.LevelPercentage;
                existing.TemperatureCelsius = inc.TemperatureCelsius;
                existing.Status = inc.Status;
                existing.InletFlowRate = inc.InletFlowRate;
                existing.OutletFlowRate = inc.OutletFlowRate;
                existing.LastUpdatedAt = inc.LastUpdatedAt;
            }
            else
            {
                Tanks.Add(inc);
            }
        }
    }

    private void UpdatePlantSummary()
    {
        double totalFlow = 0;
        double totalVolume = 0;
        int onlineCount = 0;

        foreach (var m in Meters)
        {
            if (m.IsOnline)
            {
                totalFlow += m.FlowRate;
                onlineCount++;
            }
            totalVolume += m.Totalizer;
        }

        double totalTankLevel = 0;
        if (Tanks.Count > 0)
        {
            foreach (var t in Tanks) totalTankLevel += t.LevelPercentage;
            SummaryStats.AverageTankLevel = totalTankLevel / Tanks.Count;
        }

        SummaryStats.TotalFlowRate = totalFlow;
        SummaryStats.TotalCumulativeVolume = totalVolume;
        SummaryStats.OnlineMeterCount = onlineCount;
        SummaryStats.TotalMeterCount = Meters.Count;
        SummaryStats.ActiveAlertCount = Alerts.Count(a => !a.IsAcknowledged);
    }

    private void UpdateConnectionState()
    {
        if (_settings.IsSimulationMode)
        {
            ConnectionState = TelemetryConnectionState.SimulationMode;
            ConnectionStatusText = "Simulation Active (Hardware Offline)";
            ConnectionBadgeColor = "#06B6D4";
        }
        else
        {
            ConnectionState = TelemetryConnectionState.Connecting;
            ConnectionStatusText = "Connecting to API...";
            ConnectionBadgeColor = "#F59E0B";
        }
    }

    private void HandleSettingsChanged()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(_settings.PollingIntervalMs);
        if (Meters.Count != _settings.SimulatedMeterCount && _settings.IsSimulationMode)
        {
            InitializeData();
        }
        UpdateConnectionState();
    }

    public void AcknowledgeAlert(Guid alertId)
    {
        var alert = Alerts.FirstOrDefault(a => a.Id == alertId);
        if (alert != null)
        {
            alert.IsAcknowledged = true;
            SummaryStats.ActiveAlertCount = Alerts.Count(a => !a.IsAcknowledged);
        }
    }

    public void ClearLogs()
    {
        ModbusLogs.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _settings.OnSettingsChanged -= HandleSettingsChanged;
        _timer.Stop();
    }

    // Fallback timer if Application.Current.Dispatcher is null in unit tests
    private class FallbackTimer : IDispatcherTimer
    {
        private readonly System.Timers.Timer _inner = new();
        public TimeSpan Interval
        {
            get => TimeSpan.FromMilliseconds(_inner.Interval);
            set => _inner.Interval = value.TotalMilliseconds;
        }
        public bool IsRunning => _inner.Enabled;

        // Added IsRepeating property to satisfy IDispatcherTimer in .NET 9
        public bool IsRepeating { get; set; } = true;

        public event EventHandler? Tick;

        public FallbackTimer()
        {
            _inner.Elapsed += (s, e) => Tick?.Invoke(this, EventArgs.Empty);
        }
        public void Start() => _inner.Start();
        public void Stop() => _inner.Stop();
    }
}