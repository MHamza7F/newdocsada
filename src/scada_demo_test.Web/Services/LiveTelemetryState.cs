using Microsoft.AspNetCore.SignalR.Client;

namespace scada_demo_test.Web.Services;

// ONE SignalR connection per user session, shared by every page that needs
// live data (Monitoring, Summary, Charts) - instead of each page opening its
// own connection. Registered as Scoped so each browser tab gets its own
// instance, but within a tab, navigating between pages reuses the same one.
public class LiveTelemetryState : IAsyncDisposable
{
    public class MeterState
    {
        public string DeviceExternalId = string.Empty;
        public string DeviceName = string.Empty;
        public string SensorExternalId = string.Empty;
        public string SensorName = string.Empty;
        public double FlowRate;
        public string FlowUnit = string.Empty;
        public double Totalizer;
        public string TotalizerUnit = string.Empty;
        public bool IsOnline;
        public DateTime LastSeen;
        // Every driver metric reported for this sensor: metric column -> (value, unit, seen).
        // TemperatureC/HumidityRH, InstantaneousFlowRate/AccumulatedTotalizer, ...
        // A metric stops arriving as soon as the operator un-selects it via "Edit
        // Parameters", so entries are pruned when they go stale (max 60s).
        public Dictionary<string, (double Value, string? Unit, DateTime Seen)> Values = new();
    }

    private class LiveReading
    {
        public string DeviceExternalId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string? SensorExternalId { get; set; }
        public string? SensorName { get; set; }
        public string Metric { get; set; } = string.Empty;
        public double Value { get; set; }
        public string? Unit { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsOnline { get; set; } = true;
    }

    // Shape of the AlertIncident broadcast by the API hub ("ReceiveAlertIncident")
    // whenever an enabled alert rule's threshold is crossed on a live reading.
    public class AlertIncidentDto
    {
        public Guid Id { get; set; }
        public Guid? AlertRuleId { get; set; }
        public string DeviceExternalId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Metric { get; set; } = string.Empty;
        public double TriggerValue { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Severity { get; set; } = "Warning";
        public DateTime TriggeredAt { get; set; }
        public bool IsResolved { get; set; }
    }

    public Dictionary<string, MeterState> Meters { get; } = new();
    public bool IsConnected { get; private set; }
    public string ConnectionStatus { get; private set; } = "Connecting...";

    public event Action? OnChange;
    public event Action<AlertIncidentDto>? OnAlertIncident;

    private readonly IConfiguration _config;
    private readonly ITelemetryBroadcastBus _bus;
    private HubConnection? _hubConnection;
    private Task? _startTask;

    public LiveTelemetryState(IConfiguration config, ITelemetryBroadcastBus bus)
    {
        _config = config;
        _bus = bus;

        _bus.OnReading += HandleBusReading;
    }

    private void HandleBusReading(string deviceExternalId, string deviceName, string metric, double value, string? unit, bool isOnline)
    {
        if (!Meters.TryGetValue(deviceExternalId, out var meter))
        {
            meter = new MeterState { DeviceExternalId = deviceExternalId, DeviceName = deviceName };
            Meters[deviceExternalId] = meter;
        }

        ApplyReading(meter, metric, value, unit, isOnline);
        PruneStaleValues(meter);
    }

    private static void PruneStaleValues(MeterState meter)
    {
        // Un-selected parameters stop being broadcast; drop their stale entries so
        // a "Edit Parameters" change immediately reflects on the card's metric lines.
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        var stale = meter.Values.Where(kv => kv.Value.Seen < cutoff).Select(kv => kv.Key).ToList();
        foreach (var key in stale)
        {
            meter.Values.Remove(key);
        }
    }

    // A card in the Monitoring dashboard = ONE registered sensor. Fully-added
    // sensors stream their driver columns (TemperatureC/HumidityRH for AOSONG,
    // InstantaneousFlowRate/AccumulatedTotalizer for the flowmeters) via the API
    // SignalR hub - keyed on the sensor id so a gateway with several sensors
    // paints several cards, never one merged gateway card.
    private static void ApplyReading(MeterState meter, string metric, double value, string? unit, bool isOnline)
    {
        meter.Values[metric] = (value, unit, DateTime.UtcNow);

        // Summary/Charts still consume the device-level flow convenience fields.
        if (metric is "InstantaneousFlowRate" or "FlowRate" or "Flowrate" or "Flow")
        { meter.FlowRate = value; meter.FlowUnit = unit ?? string.Empty; }
        else if (metric is "AccumulatedTotalizer" or "Totalizer" or "Total")
        { meter.Totalizer = value; meter.TotalizerUnit = unit ?? string.Empty; }

        meter.IsOnline = isOnline;
        meter.LastSeen = DateTime.UtcNow;
    }

    // Safe to call from every page's OnInitializedAsync - only connects once
    // per circuit, and every caller awaits the same in-flight task.
    public Task EnsureConnectedAsync()
    {
        _startTask ??= ConnectAsync();
        return _startTask;
    }

    private async Task ConnectAsync()
    {
        var apiBaseUrl = _config["ApiBaseUrl"] ?? "http://localhost:5080";

        // No Firebase snapshot: the cards fill ONLY from the API SignalR hub, which
        // broadcasts real telemetry polled from the registered gateways. The legacy
        // Firebase demo stream (Boiler Feed Water / Steam Line / ...) was purged.

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{apiBaseUrl}/hubs/telemetry")
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        _hubConnection.On<LiveReading>("ReceiveReading", reading =>
        {
            // One card per sensor; a legacy/plain device broadcast (no sensor id)
            // falls back to the gateway as the card key.
            var key = string.IsNullOrEmpty(reading.SensorExternalId)
                ? reading.DeviceExternalId
                : reading.SensorExternalId!;

            if (!Meters.TryGetValue(key, out var meter))
            {
                meter = new MeterState
                {
                    DeviceExternalId = reading.DeviceExternalId,
                    DeviceName = reading.DeviceName,
                    SensorExternalId = reading.SensorExternalId ?? string.Empty,
                    SensorName = string.IsNullOrEmpty(reading.SensorName)
                        ? reading.DeviceName
                        : reading.SensorName!
                };
                Meters[key] = meter;
            }

            ApplyReading(meter, reading.Metric, reading.Value, reading.Unit, reading.IsOnline);
            meter.SensorName = string.IsNullOrEmpty(meter.SensorName) ? reading.DeviceName : meter.SensorName;
            PruneStaleValues(meter);

            OnChange?.Invoke();
        });

        _hubConnection.On<AlertIncidentDto>("ReceiveAlertIncident", incident =>
        {
            OnAlertIncident?.Invoke(incident);
        });

        _hubConnection.Reconnecting += _ => { ConnectionStatus = "Reconnecting..."; IsConnected = false; OnChange?.Invoke(); return Task.CompletedTask; };
        _hubConnection.Reconnected += _ => { ConnectionStatus = "Connected (live)"; IsConnected = true; OnChange?.Invoke(); return Task.CompletedTask; };
        _hubConnection.Closed += async error =>
        {
            ConnectionStatus = "Disconnected";
            IsConnected = false;
            OnChange?.Invoke();
            await Task.Delay(3000);
            try
            {
                if (_hubConnection.State == HubConnectionState.Disconnected)
                {
                    await _hubConnection.StartAsync();
                    ConnectionStatus = "Connected (live)";
                    IsConnected = true;
                    OnChange?.Invoke();
                }
            }
            catch
            {
                // Ignored - will retry on next cycle
            }
        };

        try
        {
            await _hubConnection.StartAsync();
            ConnectionStatus = "Connected (live)";
            IsConnected = true;
        }
        catch
        {
            ConnectionStatus = "Connecting to API telemetry stream...";
            IsConnected = false;

            // Spawn background retry loop
            _ = Task.Run(async () =>
            {
                while (_hubConnection.State == HubConnectionState.Disconnected)
                {
                    await Task.Delay(3000);
                    try
                    {
                        await _hubConnection.StartAsync();
                        ConnectionStatus = "Connected (live)";
                        IsConnected = true;
                        OnChange?.Invoke();
                        break;
                    }
                    catch
                    {
                        // Will retry in next iteration
                    }
                }
            });
        }

        OnChange?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _bus.OnReading -= HandleBusReading;
        if (_hubConnection is not null)
            await _hubConnection.DisposeAsync();
    }
}
