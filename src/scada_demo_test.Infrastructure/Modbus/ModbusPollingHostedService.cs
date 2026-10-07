using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using scada_demo_test.Application.DTOs;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Drivers;
using scada_demo_test.Domain.Enums;
using scada_demo_test.Domain.Interfaces;
using scada_demo_test.Infrastructure.RealTime;
using ScadaEngine.Core.Services;

namespace scada_demo_test.Infrastructure.Modbus;

// Background engine that keeps the platform alive and collecting data while the
// operators add devices/sensors from the UI: every cycle it scans the configured
// gateways, polls only the sensors whose poll interval is due, parses the register
// payload with the installed driver and streams the result into that sensor's
// isolated telemetry table.
//
// Offline tolerance: any connect/read/parse/DB failure marks the device or sensor
// OFFLINE and is swallowed - the loop never crashes, it simply retries on the next
// due tick.
public class ModbusPollingHostedService : BackgroundService
{
    private static readonly TimeSpan CycleDelay = TimeSpan.FromSeconds(1);

    // After a gateway connect failure we stop hammering the network for a while.
    private static readonly TimeSpan GatewayBackoff = TimeSpan.FromSeconds(30);

    // RS-485 half-duplex turn-around delay between two DIFFERENT register blocks of
    // the same sensor (the driver library's own InterReadDelayMs is 50).
    private const int InterWindowDelayMs = 60;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModbusPollingHostedService> _logger;
    private readonly FrozenDictionary<string, ISensorDriver> _drivers;
    private readonly ModbusTcpMaster _master = new();

    // In-memory scheduling state (worker-local; DB is the source of truth on restart).
    private readonly Dictionary<Guid, DateTime> _lastPollUtc = new();
    private readonly Dictionary<Guid, bool> _lastOnlineState = new();
    private readonly Dictionary<Guid, DateTime> _lastAttemptUtc = new();

    // Consecutive failed reachability probes for 0-sensor gateways: transient WiFi
    // blips must NOT flip a healthy gateway OFFLINE for a second. A device is only
    // declared offline after this many failures in a row.
    private const int ZeroSensorOfflineThreshold = 3;
    private readonly Dictionary<Guid, int> _zeroSensorProbeFailures = new();

    // Consecutive failed telemetry READS per sensor: a lone read blip (WiFi jitter on
    // the serial bridge) must not DC the sensor; it is only declared offline after this
    // many failures in a row, so healthy sensors stay ONLINE through one-off glitches.
    private const int SensorOfflineStreakThreshold = 3;
    private readonly Dictionary<Guid, int> _sensorFailStreak = new();

    // The CREATE TABLE DDL for a telemetry table only needs to run once per process.
    private readonly HashSet<string> _provisionedTables = new(StringComparer.Ordinal);

    // Only log the full stack for a broken cycle every 30s, otherwise a database
    // outage would flood the console with one giant trace per second.
    private DateTime _lastCycleErrorLoggedUtc = DateTime.MinValue;

    public ModbusPollingHostedService(
        IServiceScopeFactory scopeFactory,
        IEnumerable<ISensorDriver> drivers,
        ILogger<ModbusPollingHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _drivers = drivers.ToFrozenDictionary(d => d.DriverKey, d => d, StringComparer.OrdinalIgnoreCase);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Modbus IIoT polling worker started (drivers: {Drivers}).",
            string.Join(", ", _drivers.Keys));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (stoppingToken.IsCancellationRequested) break;

                // Never let one bad poll cycle kill the service.
                if (DateTime.UtcNow - _lastCycleErrorLoggedUtc > TimeSpan.FromSeconds(30))
                {
                    _logger.LogError(ex, "Modbus polling cycle failed; continuing.");
                    _lastCycleErrorLoggedUtc = DateTime.UtcNow;
                }
                else
                {
                    _logger.LogDebug(ex, "Modbus polling cycle failed again (declining to log the full trace); continuing.");
                }
            }

            try { await Task.Delay(CycleDelay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var deviceRepo = sp.GetRequiredService<IDeviceRepository>();
        var sensorRepository = sp.GetRequiredService<ISensorRepository>();
        var telemetryRepo = sp.GetRequiredService<ISensorTelemetryRepository>();
        var broadcaster = sp.GetRequiredService<ITelemetryBroadcaster>();
        var alerts = sp.GetRequiredService<AlertService>();

        var devices = await deviceRepo.GetAllAsync(ct);

        foreach (var device in devices)
        {
            if (ModbusScanCoordinator.IsScanning(device.Id))
            {
                // Yield the physical Modbus TCP socket completely while the operator is scanning the bus.
                continue;
            }

            try
            {
                var sensors = await sensorRepository.GetByDeviceIdAsync(device.Id, ct);
            var activeSensors = sensors.Where(s => s.IsActive).ToList();

            // Watchdog: a device that stopped reporting data for longer than its timeout
            // (>=60s, generous so transient stalls/drops never flip it) goes offline.
            // Only the first cycle past the timeout is a transition, so exactly one
            // offline row per sensor is written.
            var timeout = ComputeWatchdogTimeout(activeSensors);
            var lastSeenFresh = device.LastSeenAt.HasValue &&
                (DateTime.UtcNow - device.LastSeenAt.Value) <= timeout;

            if (device.IsOnline && !lastSeenFresh)
            {
                _logger.LogInformation("Device '{Device}' ({DeviceId}) went offline: no data for {Elapsed}.",
                    device.Name, device.Id, DateTime.UtcNow - (device.LastSeenAt ?? DateTime.UtcNow));
                await MarkDeviceAsync(deviceRepo, device, isOnline: false, ct);
                foreach (var sensor in activeSensors)
                {
                    var driver = ResolveDriver(sensor);
                    if (driver is not null)
                    {
                        await BroadcastSensorAsync(broadcaster, device, sensor, driver, 0, 0, online: false, ct);
                    }
                    await RecordWatchdogOfflineAsync(telemetryRepo, sensor, driver, "NO_DATA_TIMEOUT", ct);
                    await MarkSensorAsync(sensorRepository, sensor, isOnline: false, ct, "NO_DATA_TIMEOUT");
                    _lastOnlineState[sensor.Id] = false;
                }
                continue;
            }

            // Norvi ESP32 devices are Modbus RTU MASTERS that push their readings
            // over HTTP - there is no Modbus TCP server to poll, and a TCP connect
            // always fails. Their online state is driven by the LastSeenAt watchdog
            // above, never by Modbus TCP.
            if (string.IsNullOrWhiteSpace(device.IpAddress) ||
                device.HardwareType == DeviceHardwareType.NorviESP32)
            {
                continue;
            }

            using var busLease = await ModbusScanCoordinator.AcquireBusAsync(device.IpAddress, device.Port, ct);
            if (ModbusScanCoordinator.IsScanning(device.Id)) continue;

            var dueSensors = activeSensors.Where(IsDue).ToList();
            if (dueSensors.Count == 0)
            {
                if (activeSensors.Count > 0)
                {
                    // Sensors exist but none are due THIS tick. Their next due poll
                    // refreshes the watchdog clock itself - NEVER probe-connect here.
                    // A per-second connect churn on a serial bridge (USR-W610) fails
                    // sporadically and was flipping a healthy gateway OFFLINE after a
                    // handful of transient drops while the real polls kept succeeding.
                    continue;
                }

                // Yield socket if an active bus scan is running on this gateway
                if (ModbusScanCoordinator.IsScanning(device.Id))
                {
                    continue;
                }

                // A gateway with no registered sensors yet still reports ONLINE/OFFLINE
                // from pure TCP reachability: probe-connect every 10 seconds (never hammer
                // the USR-W610 serial bridge every second with TCP churn) and refresh
                // LastSeenAt so the watchdog never kills a reachable gateway.
                var now = DateTime.UtcNow;
                var seenStale = device.LastSeenAt == null || (now - device.LastSeenAt.Value).TotalSeconds >= 10;
                if (device.IsOnline && !seenStale)
                {
                    continue;
                }

                try
                {
                    using var probe = await _master.OpenAsync(device.IpAddress, device.Port, device.TimeoutMs, ct);
                    _zeroSensorProbeFailures[device.Id] = 0;
                    device.IsOnline = true;
                    device.Status = DeviceStatus.Online;
                    device.LastSeenAt = now;
                    await deviceRepo.UpdateAsync(device, ct);
                }
                catch
                {
                    // Only declare OFFLINE after consecutive failures - a lone probe
                    // can drop (WiFi jitter) without the gateway actually being down.
                    int fails = (_zeroSensorProbeFailures.TryGetValue(device.Id, out var f) ? f : 0) + 1;
                    _zeroSensorProbeFailures[device.Id] = fails;
                    if (fails >= ZeroSensorOfflineThreshold && device.IsOnline)
                    {
                        _zeroSensorProbeFailures[device.Id] = 0;
                        await MarkDeviceAsync(deviceRepo, device, isOnline: false, ct);
                    }
                }
                continue;
            }

            // A known-dead gateway is not re-attempted every second; give it a
            // backoff window so the worker does not stall behind connect timeouts.
            if (IsBackingOff(device.Id))
            {
                if (!lastSeenFresh)
                {
                    await MarkDeviceAsync(deviceRepo, device, isOnline: false, ct);
                    foreach (var sensor in dueSensors)
                    {
                        await RecordOfflineAsync(telemetryRepo, sensor, ResolveDriver(sensor), "GATEWAY_OFFLINE", ct);
                        await MarkSensorAsync(sensorRepository, sensor, isOnline: false, ct, "GATEWAY_OFFLINE");
                        _lastOnlineState[sensor.Id] = false;
                    }
                }
                continue;
            }
            var anyConnected = false;

            foreach (var sensor in dueSensors)
            {
                if (sensor.SlaveAddress is < 1 or > 247)
                {
                    await RecordOfflineAsync(telemetryRepo, sensor, ResolveDriver(sensor), "INVALID_SLAVE_ADDRESS", ct);
                    await MarkSensorAsync(sensorRepository, sensor, isOnline: false, ct, "INVALID_SLAVE_ADDRESS");
                    _lastPollUtc[sensor.Id] = DateTime.UtcNow;
                    _lastOnlineState[sensor.Id] = false;
                    continue;
                }

                if (!_drivers.TryGetValue(sensor.SensorTypeKey, out var driver))
                {
                    await RecordOfflineAsync(telemetryRepo, sensor, null, "UNKNOWN_DRIVER", ct);
                    await MarkSensorAsync(sensorRepository, sensor, isOnline: false, ct, "UNKNOWN_DRIVER");
                    _lastPollUtc[sensor.Id] = DateTime.UtcNow;
                    _lastOnlineState[sensor.Id] = false;
                    continue;
                }

                try
                {
                    // MODBUS LAYER (unreachable meter = real failure): read on a fresh
                    // session with a generous budget (the device's configured 2000ms is
                    // too tight for serial bridges), and retry ONCE on a brand-new
                    // connection before counting the read as a failure - but only when
                    // the first attempt failed FAST. A slow serial bridge that misses
                    // its read timeout will just time out again, so a retry would only
                    // double the failure cost (~10s) and race the offline streak.
                    byte[] payload;
                    var readBudgetMs = ReadBudgetMs(device);
                    var windows = driver.ReadWindows;
                    var rawAll = new List<byte>();

                    for (int w = 0; w < windows.Count; w++)
                    {
                        // RS-485 is half-duplex: never issue one register block right
                        // after another - the replies collide and are lost. Each window
                        // goes on its own fresh connection (a serial bridge desyncs on a
                        // reused session), and the existing "retry only if the first
                        // attempt failed FAST" rule still applies per window.
                        if (w > 0) await Task.Delay(InterWindowDelayMs, ct);
                        var windowPayload = await ReadWindowWithRetryAsync(
                            device, (byte)sensor.SlaveAddress, windows[w], readBudgetMs,
                            () => anyConnected = true, ct);
                        rawAll.AddRange(windowPayload);
                    }

                    payload = rawAll.ToArray();

                    // Architectural Rule #1 & #2: Wrap raw payload in a Checkpost-verified
                    // ModbusDevicePacket envelope and dispatch through the O(1) "Ghar" engine.
                    var expectedProfile = CheckpostRouter.ResolveProfileByDriverKey(driver.DriverKey);
                    var envelope = CheckpostRouter.InspectAndTag(
                        (byte)sensor.SlaveAddress,
                        payload,
                        expectedProfile,
                        driver.FunctionCode,
                        driver.StartRegister,
                        driver.RegisterQuantity);
                    if (envelope is null)
                    {
                        throw new ModbusException("CheckpostRouter rejected corrupted/noise Modbus frame.");
                    }
                    var gharResult = SensorDriverCatalog.DispatchPacket(envelope);
                    if (!gharResult.IsValid)
                        throw new ModbusException(gharResult.ErrorCode ?? "Driver rejected verified packet.");

                    var primary = gharResult.PrimaryValue * sensor.CalibrationMultiplier;
                    var secondary = gharResult.SecondaryValue * sensor.CalibrationMultiplier;
                    if (!double.IsFinite(primary) || !double.IsFinite(secondary))
                        throw new ModbusException("Calibration produced a non-finite reading.");
                    var tableName = sensor.TelemetryTableName ?? TelemetryTableNaming.Build(driver.SimpleName, sensor.Id);

                    // The meter answered and parsed fine: clear the failure streak NOW.
                    _sensorFailStreak[sensor.Id] = 0;
                    await MarkSensorAsync(sensorRepository, sensor, isOnline: true, ct);

                    // DB LAYER (storage hiccup = just a delay, never a DC): a transient
                    // insert failure must NOT take the sensor offline - the meter is fine,
                    // we simply retry the write next poll.
                    var cols = SensorColumnSet.For(driver, sensor.MetricFields);

                    try
                    {
                        await EnsureTableAsync(telemetryRepo, tableName, driver, cols, ct);
                        await telemetryRepo.InsertAsync(tableName, driver, cols, payload, primary, secondary, online: true, errorCode: null, ct);
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogDebug(dbEx, "Telemetry write failed for '{Sensor}' on '{Device}'; sensor stays ONLINE, retrying next poll: {Message}",
                            sensor.Name, device.Name, dbEx.Message);
                    }

                    // SignalR live stream: only sensors fully registered + polled paint
                    // Monitoring cards.
                    await BroadcastSensorAsync(broadcaster, device, sensor, driver, primary, secondary, online: true, ct);

                    // Rule engine: evaluate the operator's threshold rules against this
                    // real reading. Only the SELECTED columns are fed (unselected
                    // parameters produce no alerts). Fails silently - an alert hiccup
                    // must never affect the poll.
                    try
                    {
                        if (cols.HasPrimary)
                            await alerts.EvaluateReadingAsync(sensor.UniqueSensorId, sensor.Name, driver.PrimaryColumnName, primary, ct);
                        if (cols.HasSecondary)
                            await alerts.EvaluateReadingAsync(sensor.UniqueSensorId, sensor.Name, driver.SecondaryColumnName, secondary, ct);
                    }
                    catch (Exception alertEx)
                    {
                        _logger.LogDebug(alertEx, "Alert evaluation failed for '{Sensor}': {Message}", sensor.Name, alertEx.Message);
                    }

                    _lastPollUtc[sensor.Id] = DateTime.UtcNow;
                    _lastOnlineState[sensor.Id] = true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Sensor '{Sensor}' on '{Device}' poll failed: {Message}",
                        sensor.Name, device.Name, ex.Message);

                    // Small streak tolerance: a single bus jitter blip must not DC the
                    // sensor; a sustained failure (>= SensorOfflineStreakThreshold
                    // consecutive reads) flips it offline with exactly one offline row.
                    int streak = (_sensorFailStreak.TryGetValue(sensor.Id, out var s) ? s : 0) + 1;
                    _sensorFailStreak[sensor.Id] = streak;
                    _lastPollUtc[sensor.Id] = DateTime.UtcNow;
                    if (streak >= SensorOfflineStreakThreshold)
                    {
                        await BroadcastSensorAsync(broadcaster, device, sensor, driver, 0, 0, online: false, ct);
                        await RecordOfflineAsync(telemetryRepo, sensor, driver, ex.Message, ct);
                        await MarkSensorAsync(sensorRepository, sensor, isOnline: false, ct, Truncate(ex.Message));
                        _lastOnlineState[sensor.Id] = false;
                    }
                }
            }

            // Device ONLINE = TCP reachable (any session opened) OR its last data is
            // fresh. A single meter read failure never takes the whole gateway down
            // with it; the watchdog handles genuinely silent devices instead.
            if (anyConnected)
            {
                // Live gateway: never back off from a healthy device - it is polled
                // again next cycle per its sensors' poll intervals.
                _lastAttemptUtc.Remove(device.Id);
                await MarkDeviceAsync(deviceRepo, device, isOnline: true, ct);
            }
            else if (!lastSeenFresh)
            {
                // Not reachable AND its last data pushed/polled long ago -> offline.
                // Start the backoff window HERE (a dead gateway is not re-attempted
                // every second, only after GatewayBackoff elapses).
                _lastAttemptUtc[device.Id] = DateTime.UtcNow;
                await MarkDeviceAsync(deviceRepo, device, isOnline: false, ct);
                foreach (var sensor in dueSensors)
                {
                    var driver = ResolveDriver(sensor);
                    if (driver is not null)
                    {
                        await BroadcastSensorAsync(broadcaster, device, sensor, driver, 0, 0, online: false, ct);
                    }
                    await RecordOfflineAsync(telemetryRepo, sensor, driver, "GATEWAY_OFFLINE", ct);
                    await MarkSensorAsync(sensorRepository, sensor, isOnline: false, ct, "GATEWAY_OFFLINE");
                    _lastOnlineState[sensor.Id] = false;
                }
            }
            // else: unreachable but data still fresh -> keep the current state; the
            // watchdog flips it offline only once the data actually goes stale.
            }
            catch (Exception deviceEx)
            {
                // One bad gateway (DB hiccup etc.) must never stall the other gateways
                // in this cycle or crash anything: log and move on; the outer worker
                // loop simply runs the next tick.
                _logger.LogDebug(deviceEx, "Device '{Device}' poll cycle failed; continuing.", device.Name);
            }
        }
    }

    private bool IsBackingOff(Guid deviceId) =>
        _lastAttemptUtc.TryGetValue(deviceId, out var lastAttempt) &&
        (DateTime.UtcNow - lastAttempt) < GatewayBackoff;

    private ISensorDriver? ResolveDriver(Domain.Entities.Sensor sensor) =>
        _drivers.TryGetValue(sensor.SensorTypeKey, out var driver) ? driver : null;

    // Streams a sensor reading to the SignalR live hub. The Monitoring tab paints a
    // per-SENSOR card from these (keyed on SensorExternalId); Summary/Charts keep
    // using the device-level flow values. Encapsulated so a SignalR hiccup can
    // never fail a poll cycle.
    private async Task BroadcastSensorAsync(
        ITelemetryBroadcaster broadcaster,
        Domain.Entities.Device device,
        Domain.Entities.Sensor sensor,
        ISensorDriver driver,
        double primary,
        double secondary,
        bool online,
        CancellationToken ct)
    {
        if (device.ExternalId is null) return;
        try
        {
            var now = DateTime.UtcNow;
            var columns = SensorColumnSet.For(driver, sensor.MetricFields);

            if (columns.HasPrimary)
            {
                await SendLiveAsync(broadcaster, device, sensor, driver.PrimaryColumnName, driver.UnitPrimary,
                    online ? primary : 0, online, now, ct);
            }
            if (columns.HasSecondary)
            {
                await SendLiveAsync(broadcaster, device, sensor, driver.SecondaryColumnName, driver.UnitSecondary,
                    online ? secondary : 0, online, now, ct);
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget stream: never let a dead SignalR client abort the poller.
            _logger.LogDebug(ex, "Live broadcast failed for driver '{DriverKey}' on '{Device}': {Message}",
                driver.DriverKey, device.Name, ex.Message);
        }
    }

    private async Task SendLiveAsync(
        ITelemetryBroadcaster broadcaster,
        Domain.Entities.Device device,
        Domain.Entities.Sensor sensor,
        string metric,
        string? unit,
        double value,
        bool online,
        DateTime now,
        CancellationToken ct)
    {
        await broadcaster.BroadcastReadingAsync(new LiveReadingDto
        {
            DeviceExternalId = device.ExternalId,
            DeviceName = device.Name,
            SensorExternalId = sensor.UniqueSensorId,
            SensorName = sensor.Name,
            Metric = metric,
            Value = value,
            Unit = online ? unit : null,
            Timestamp = now,
            IsOnline = online
        }, ct);
    }

    private async Task EnsureTableAsync(
        ISensorTelemetryRepository telemetryRepo,
        string tableName,
        ISensorDriver driver,
        SensorColumnSet columns,
        CancellationToken ct)
    {
        // Provision once per (table + parameter selection) - an "Edit Parameters"
        // save changes the signature and re-runs the idempotent ADD COLUMN.
        var key = tableName + "|" + columns.Signature;
        if (_provisionedTables.Add(key))
        {
            await telemetryRepo.EnsureTelemetryTableAsync(tableName, driver, columns, ct);
        }
    }

    private bool IsDue(Domain.Entities.Sensor sensor)
    {
        if (!_lastPollUtc.TryGetValue(sensor.Id, out var last)) return true;
        return (DateTime.UtcNow - last).TotalSeconds >= Math.Max(1, sensor.PollIntervalSeconds);
    }

    // How long a device must stay COMPLETELY silent before the watchdog flips it
    // offline. This is deliberately generous (min 60s): a transient connection drop
    // or a slow serial-bus stall must never take a healthy gateway offline, only a
    // genuinely dead one (powered off / pulled off the LAN). Polls succeed every
    // few seconds, so the clock is refreshed well before the timeout can fire on a
    // working gateway.
    private static TimeSpan ComputeWatchdogTimeout(IReadOnlyList<Domain.Entities.Sensor> sensors)
    {
        var maxInterval = sensors.Count == 0 ? 60 : sensors.Max(s => Math.Max(1, s.PollIntervalSeconds)) * 4;
        return TimeSpan.FromSeconds(Math.Max(60, maxInterval));
    }

    // A read/connect budget generous enough for serial-to-ethernet bridges whose
    // RS-485 transactions intermittently stall for a few seconds; the device's
    // configured 2s timeout is too tight for that hardware and caused spurious
    // offline flapping. Applied to connect + read + the one reconnect retry.
    private static int ReadBudgetMs(Domain.Entities.Device device) => Math.Max(5000, device.TimeoutMs);

    // Reads ONE register window on a FRESH connection, retrying once only when the
    // first attempt failed FAST (a genuine slow-bridge timeout will just time out
    // again, so a retry would only double the failure cost).
    private async Task<byte[]> ReadWindowWithRetryAsync(
        Domain.Entities.Device device,
        byte slave,
        SensorReadWindow window,
        int budgetMs,
        Action onConnected,
        CancellationToken ct)
    {
        var firstSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var session = await _master.OpenAsync(device.IpAddress!, device.Port, budgetMs, ct);
            onConnected();
            return await ReadWindowAsync(session, slave, window, budgetMs, ct);
        }
        catch when (firstSw.ElapsedMilliseconds < budgetMs / 2)
        {
            using var retry = await _master.OpenAsync(device.IpAddress!, device.Port, budgetMs, ct);
            onConnected();
            return await ReadWindowAsync(retry, slave, window, budgetMs, ct);
        }
    }

    private static Task<byte[]> ReadWindowAsync(
        ModbusTcpSession session,
        byte slave,
        SensorReadWindow window,
        int timeoutMs,
        CancellationToken ct)
        => window.FunctionCode == 0x04
            ? session.ReadInputRegistersAsync(slave, window.StartRegister, window.RegisterQuantity, ct, timeoutMs)
            : session.ReadHoldingRegistersAsync(slave, window.StartRegister, window.RegisterQuantity, ct, timeoutMs);

    private async Task RecordWatchdogOfflineAsync(
        ISensorTelemetryRepository telemetryRepo,
        Domain.Entities.Sensor sensor,
        ISensorDriver? driver,
        string errorMessage,
        CancellationToken ct)
    {
        // Only write the offline row once, on a real online -> offline transition.
        if (!sensor.IsOnline) return;
        if (driver is null) return; // no driver -> no table to write into

        var tableName = sensor.TelemetryTableName ?? TelemetryTableNaming.Build(driver.SimpleName, sensor.Id);
        var columns = SensorColumnSet.For(driver, sensor.MetricFields);
        try
        {
            await EnsureTableAsync(telemetryRepo, tableName, driver, columns, ct);
            await telemetryRepo.InsertAsync(tableName, driver, columns, Array.Empty<byte>(), 0, 0, online: false, errorCode: Truncate(errorMessage), ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not record watchdog offline telemetry for '{Sensor}'.", sensor.Name);
        }
    }

    private static async Task MarkDeviceAsync(
        IDeviceRepository deviceRepo,
        Domain.Entities.Device device,
        bool isOnline,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var changed = device.IsOnline != isOnline;

        device.IsOnline = isOnline;
        if (isOnline)
        {
            device.Status = DeviceStatus.Online;
            // ALWAYS refresh the watchdog clock on a successful poll (the write is
            // throttled to ~1 per pump by the poll cadence). Only a genuinely silent
            // device can ever outlive the 30s+ watchdog timeout now.
            if (device.LastSeenAt != now)
            {
                device.LastSeenAt = now;
                changed = true;
            }
        }
        else
        {
            device.Status = DeviceStatus.Offline;
        }

        if (changed)
        {
            await deviceRepo.UpdateAsync(device, ct);
        }
    }

    private static async Task MarkSensorAsync(
        ISensorRepository sensorRepo,
        Domain.Entities.Sensor sensor,
        bool isOnline,
        CancellationToken ct,
        string? errorCode = null)
    {
        if (sensor.IsOnline == isOnline) return;
        sensor.IsOnline = isOnline;
        await sensorRepo.UpdateAsync(sensor, ct);
    }

    private async Task RecordOfflineAsync(
        ISensorTelemetryRepository telemetryRepo,
        Domain.Entities.Sensor sensor,
        ISensorDriver? driver,
        string errorMessage,
        CancellationToken ct)
    {
        // Only write a "went offline" telemetry row on a transition (online -> offline).
        // Steady-state offline retries would otherwise flood the table every poll tick.
        if (_lastOnlineState.TryGetValue(sensor.Id, out var wasOnline) && !wasOnline) return;
        if (driver is null) return; // no driver -> no table to write into

        var tableName = sensor.TelemetryTableName ?? TelemetryTableNaming.Build(driver.SimpleName, sensor.Id);
        var columns = SensorColumnSet.For(driver, sensor.MetricFields);
        try
        {
            await EnsureTableAsync(telemetryRepo, tableName, driver, columns, ct);
            await telemetryRepo.InsertAsync(tableName, driver, columns, Array.Empty<byte>(), 0, 0, online: false, errorCode: Truncate(errorMessage), ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not record offline telemetry for '{Sensor}'.", sensor.Name);
        }
    }

    private static string Truncate(string? value, int max = 120) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : (value.Length <= max ? value : value[..max]);
}
