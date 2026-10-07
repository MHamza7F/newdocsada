using System.Net.Http.Json;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Enums;
using scada_demo_test.Domain.Services;

namespace scada_demo_test.Web.Services;

public class ScadaDemoTestApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _scan;
    private readonly FirebaseScadaService _firebase;

    public ScadaDemoTestApiClient(HttpClient http, HttpClient scan, FirebaseScadaService firebase)
    {
        _http = http;
        _scan = scan;
        _firebase = firebase;
    }

    public void Dispose()
    {
        _http.Dispose();
        _scan.Dispose();
    }

    public string BaseUrl => _firebase.DatabaseUrl;

    // ---- Devices ----
    public record DeviceDto(Guid Id, string ExternalId, string Name, string DeviceType, string Status, int? ModbusSlaveId, string? IpAddress, DateTime? LastSeenAt);
    public record ReadingPointDto(DateTime Timestamp, double Value, string? Unit);
    public record LatestSnapshotDto(Guid Id, string ExternalId, string Name, string DeviceType, string Status,
        int? ModbusSlaveId, string? IpAddress, DateTime? LastSeenAt,
        double FlowRate, string FlowUnit, double Totalizer, string TotalizerUnit);

    public record CreateDeviceRequest(string ExternalId, string Name, string DeviceType, string Protocol, int? ModbusSlaveId, string? IpAddress, Guid? SiteId);

    public async Task<List<DeviceDto>> GetDevicesAsync()
    {
        try
        {
            var devices = await _firebase.GetDevicesAsync();
            return devices.Select(d => new DeviceDto(
                d.Id,
                d.ExternalId,
                d.Name,
                d.DeviceType.ToString(),
                d.Status.ToString(),
                d.ModbusSlaveId,
                d.IpAddress,
                d.LastSeenAt
            )).ToList();
        }
        catch { return new(); }
    }

    public async Task<List<LatestSnapshotDto>> GetLatestSnapshotAsync()
    {
        try
        {
            var devices = await _firebase.GetDevicesAsync();
            var readings = await _firebase.GetRecentSensorReadingsAsync(200);

            var list = new List<LatestSnapshotDto>();
            foreach (var d in devices)
            {
                var flowReading = readings.FirstOrDefault(r => r.DeviceId == d.Id && r.Metric == "FlowRate");
                var totalizerReading = readings.FirstOrDefault(r => r.DeviceId == d.Id && r.Metric == "Totalizer");

                double flowRate = flowReading?.Value ?? 0;
                string flowUnit = flowReading?.Unit ?? "";
                double totalizer = totalizerReading?.Value ?? 0;
                string totalizerUnit = totalizerReading?.Unit ?? "";

                list.Add(new LatestSnapshotDto(
                    d.Id,
                    d.ExternalId,
                    d.Name,
                    d.DeviceType.ToString(),
                    d.Status.ToString(),
                    d.ModbusSlaveId,
                    d.IpAddress,
                    d.LastSeenAt ?? DateTime.UtcNow,
                    flowRate,
                    flowUnit,
                    totalizer,
                    totalizerUnit
                ));
            }
            return list;
        }
        catch { return new(); }
    }

    public async Task<List<ReadingPointDto>> GetReadingsAsync(string externalId, string metric = "FlowRate", int count = 30)
    {
        try
        {
            var readings = await _firebase.GetDeviceReadingsAsync(externalId, metric, count);
            return readings.Select(r => new ReadingPointDto(r.Timestamp, r.Value, r.Unit)).ToList();
        }
        catch { return new(); }
    }

    public async Task<(bool Success, string? Error)> CreateDeviceAsync(CreateDeviceRequest dto)
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            ExternalId = dto.ExternalId,
            Name = dto.Name,
            DeviceType = Enum.TryParse<DeviceType>(dto.DeviceType, true, out var dt) ? dt : DeviceType.FlowMeter,
            Protocol = Enum.TryParse<ProtocolType>(dto.Protocol, true, out var pt) ? pt : ProtocolType.ModbusRtu,
            Status = DeviceStatus.Online,
            ModbusSlaveId = dto.ModbusSlaveId,
            IpAddress = dto.IpAddress,
            SiteId = dto.SiteId,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        var res = await _firebase.CreateDeviceAsync(device);
        if (res.Success)
        {
            _ = _firebase.LogAuditActionAsync("Device.Create", "Device", device.Id.ToString(), $"Created device {device.Name} ({device.ExternalId})");
        }
        return res;
    }

    public async Task<(bool Success, string? Error)> UpdateDeviceAsync(Guid id, CreateDeviceRequest dto)
    {
        var devices = await _firebase.GetDevicesAsync();
        var existing = devices.FirstOrDefault(d => d.Id == id);
        if (existing == null) return (false, "Device not found.");

        existing.Name = dto.Name;
        existing.ExternalId = dto.ExternalId;
        existing.ModbusSlaveId = dto.ModbusSlaveId;
        existing.IpAddress = dto.IpAddress;
        existing.SiteId = dto.SiteId;

        var res = await _firebase.UpdateDeviceAsync(existing);
        if (res.Success)
        {
            _ = _firebase.LogAuditActionAsync("Device.Update", "Device", id.ToString(), $"Updated device {existing.Name}");
        }
        return res;
    }

    public async Task<(bool Success, string? Error)> DeleteDeviceAsync(Guid id)
    {
        var res = await _firebase.DeleteDeviceAsync(id);
        if (res.Success)
        {
            _ = _firebase.LogAuditActionAsync("Device.Delete", "Device", id.ToString(), $"Deleted device with ID {id}");
        }
        return res;
    }

    // ---- History & Reports ----
    public record HistorySeriesDto(
        Guid DeviceId,
        string DeviceExternalId,
        string Metric,
        string Unit,
        string Resolution,
        bool IsCumulative,
        DateTime PeriodStart,
        DateTime PeriodEnd,
        List<HistoryPointDto> Points);

    public record HistoryPointDto(DateTime Timestamp, double Value, int SampleCount, double? Min, double? Max);

    public async Task<HistorySeriesDto?> GetHistoryAsync(string externalId, string metric, DateTime from, DateTime to)
    {
        try
        {
            var readings = await GetReadingsAsync(externalId, metric, 40);
            var points = readings.Select(r => new HistoryPointDto(r.Timestamp, r.Value, 1, r.Value * 0.95, r.Value * 1.05)).ToList();

            return new HistorySeriesDto(
                Guid.NewGuid(),
                externalId,
                metric,
                "m³/h",
                "Raw",
                false,
                from,
                to,
                points
            );
        }
        catch { return null; }
    }

    // ---- Storage Tanks ----
    public record StorageTankDto(Guid Id, Guid? SiteId, string TankCode, string Name, double CapacityLiters,
        double CurrentVolumeLiters, double LevelPercentage, double TemperatureCelsius, string Status,
        string LiquidType, double InletFlowRate, double OutletFlowRate, DateTime LastUpdatedAt);

    public record CreateTankRequest(string TankCode, string Name, double CapacityLiters, string LiquidType, Guid? SiteId);
    public record UpdateTankRequest(string Name, double CapacityLiters, string LiquidType);

    public async Task<List<StorageTankDto>> GetTanksAsync()
    {
        try
        {
            var apiTanks = await _http.GetFromJsonAsync<List<StorageTankDto>>("/api/tanks");
            if (apiTanks is { Count: > 0 }) return apiTanks;
        }
        catch { }

        try
        {
            var tanks = await _firebase.GetStorageTanksAsync();
            return tanks.Select(t => new StorageTankDto(
                t.Id,
                t.SiteId,
                t.TankCode,
                t.Name,
                t.CapacityLiters,
                t.CurrentVolumeLiters,
                t.LevelPercentage,
                t.TemperatureCelsius,
                t.Status,
                t.LiquidType,
                t.InletFlowRate,
                t.OutletFlowRate,
                t.LastUpdatedAt
            )).ToList();
        }
        catch { return new(); }
    }

    public async Task<(bool Success, string? Error)> CreateTankAsync(CreateTankRequest dto)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("/api/tanks", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Create tank failed ({res.StatusCode})");
        }
        catch
        {
            var tank = new StorageTank
            {
                Id = Guid.NewGuid(),
                SiteId = dto.SiteId,
                TankCode = dto.TankCode,
                Name = dto.Name,
                CapacityLiters = dto.CapacityLiters,
                CurrentVolumeLiters = dto.CapacityLiters * 0.7,
                LevelPercentage = 70.0,
                LiquidType = dto.LiquidType,
                TemperatureCelsius = 24.0,
                Status = "Normal",
                InletFlowRate = 120.0,
                OutletFlowRate = 100.0,
                LastUpdatedAt = DateTime.UtcNow
            };
            var res = await _firebase.UpdateStorageTankAsync(tank);
            if (res.Success)
            {
                _ = _firebase.LogAuditActionAsync("Tank.Create", "StorageTank", tank.Id.ToString(), $"Created tank {tank.Name} ({tank.TankCode})");
            }
            return res;
        }
    }

    public async Task<(bool Success, string? Error)> UpdateTankAsync(Guid id, UpdateTankRequest dto)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"/api/tanks/{id}", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Update tank failed ({res.StatusCode})");
        }
        catch
        {
            var tanks = await _firebase.GetStorageTanksAsync();
            var existing = tanks.FirstOrDefault(t => t.Id == id);
            if (existing == null) return (false, "Tank not found.");

            existing.Name = dto.Name;
            existing.CapacityLiters = dto.CapacityLiters;
            existing.LiquidType = dto.LiquidType;
            existing.LastUpdatedAt = DateTime.UtcNow;

            var res = await _firebase.UpdateStorageTankAsync(existing);
            if (res.Success)
            {
                _ = _firebase.LogAuditActionAsync("Tank.Update", "StorageTank", id.ToString(), $"Updated tank {existing.Name}");
            }
            return res;
        }
    }

    public async Task<(bool Success, string? Error)> DeleteTankAsync(Guid id)
    {
        try
        {
            var res = await _http.DeleteAsync($"/api/tanks/{id}");
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Delete tank failed ({res.StatusCode})");
        }
        catch
        {
            var res = await _firebase.DeleteStorageTankAsync(id);
            if (res.Success)
            {
                _ = _firebase.LogAuditActionAsync("Tank.Delete", "StorageTank", id.ToString(), $"Deleted tank {id}");
            }
            return res;
        }
    }

    // ---- Alerts (API / PostgreSQL-backed, NOT Firebase) ----
    public record AlertIncidentDto(Guid Id, Guid? AlertRuleId, string DeviceExternalId, string DeviceName,
        string Metric, double TriggerValue, string Message, string Severity, DateTime TriggeredAt,
        DateTime? AcknowledgedAt, string? AcknowledgedBy, bool IsResolved, DateTime? ResolvedAt);

    public record AlertRuleDto(Guid Id, Guid? DeviceId, string DeviceExternalId, string Metric,
        string Condition, double ThresholdValue, string Severity, bool IsEnabled, string? NotificationEmail,
        string? NotificationPhone, DateTime CreatedAt);

    public async Task<List<AlertIncidentDto>> GetAlertIncidentsAsync(int limit = 50)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<AlertIncidentDto>>($"/api/alerts/incidents?limit={limit}");
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<(bool Success, string? Error)> AcknowledgeAlertAsync(Guid id)
    {
        try
        {
            var response = await _http.PostAsync($"/api/alerts/acknowledge/{id}", null);
            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, $"API {(int)response.StatusCode} — acknowledge failed.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string? Error)> DeleteAlertIncidentAsync(Guid id)
    {
        try
        {
            var response = await _http.DeleteAsync($"/api/alerts/incidents/{id}");
            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, $"API {(int)response.StatusCode} — delete failed.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<List<AlertRuleDto>> GetAlertRulesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<AlertRuleDto>>("/api/alerts/rules");
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<(bool Success, string? Error)> CreateAlertRuleAsync(AlertRuleDto dto)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/alerts/rules", new
            {
                dto.DeviceId,
                DeviceExternalId = string.IsNullOrWhiteSpace(dto.DeviceExternalId) ? "ALL" : dto.DeviceExternalId,
                dto.Metric,
                dto.Condition,
                dto.ThresholdValue,
                dto.Severity,
                dto.IsEnabled,
                dto.NotificationEmail,
                dto.NotificationPhone
            });
            var body = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode
                ? (true, body)
                : (false, $"API {(int)response.StatusCode} — {body}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string? Error)> DeleteAlertRuleAsync(Guid id)
    {
        try
        {
            var response = await _http.DeleteAsync($"/api/alerts/rules/{id}");
            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, $"API {(int)response.StatusCode} — delete failed.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ---- Audit Logs ----
    public record AuditLogDto(Guid Id, Guid? UserId, string? UserEmail, string? UserName, string Action,
        string EntityName, string? EntityId, string? Details, string? IpAddress, DateTime Timestamp);

    public record AuditLogsPagedDto(int Total, int Page, int PageSize, List<AuditLogDto> Items);

    public async Task<AuditLogsPagedDto> GetAuditLogsAsync(string? search = null, int page = 1, int pageSize = 50)
    {
        try
        {
            var q = $"/api/audit-logs?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(search)) q += $"&search={Uri.EscapeDataString(search)}";
            var apiResult = await _http.GetFromJsonAsync<AuditLogsPagedDto>(q);
            if (apiResult is { Items.Count: > 0 } || apiResult?.Total > 0) return apiResult;
        }
        catch { }

        try
        {
            var all = await _firebase.GetAuditLogsAsync(200);
            if (!string.IsNullOrEmpty(search))
            {
                all = all.Where(a =>
                    (a.Action != null && a.Action.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (a.Details != null && a.Details.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (a.UserName != null && a.UserName.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (a.UserEmail != null && a.UserEmail.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
            }

            var total = all.Count;
            var items = all.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(a => new AuditLogDto(
                    a.Id,
                    a.UserId,
                    a.UserEmail,
                    a.UserName,
                    a.Action,
                    a.EntityName,
                    a.EntityId,
                    a.Details,
                    a.IpAddress,
                    a.Timestamp
                )).ToList();

            return new AuditLogsPagedDto(total, page, pageSize, items);
        }
        catch { return new(0, page, pageSize, new()); }
    }

    // ---- Sites (Multi-Plant) ----
    public record SiteDto(Guid Id, string Code, string Name, string Location, string? Description, DateTime CreatedAt);

    public async Task<List<SiteDto>> GetSitesAsync()
    {
        try
        {
            var apiSites = await _http.GetFromJsonAsync<List<SiteDto>>("/api/sites");
            if (apiSites is { Count: > 0 }) return apiSites;
        }
        catch { }

        try
        {
            var sites = await _firebase.GetSitesAsync();
            return sites.Select(s => new SiteDto(
                s.Id,
                s.Code,
                s.Name,
                s.Location,
                s.Description,
                s.CreatedAt
            )).ToList();
        }
        catch { return new(); }
    }

    public async Task<(bool Success, string? Error)> CreateSiteAsync(SiteDto dto)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("/api/sites", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Create site failed ({res.StatusCode})");
        }
        catch
        {
            var site = new Site
            {
                Id = dto.Id != Guid.Empty ? dto.Id : Guid.NewGuid(),
                Code = dto.Code,
                Name = dto.Name,
                Location = dto.Location,
                Description = dto.Description,
                CreatedAt = DateTime.UtcNow
            };
            var res = await _firebase.CreateSiteAsync(site);
            if (res.Success)
            {
                _ = _firebase.LogAuditActionAsync("Site.Create", "Site", site.Id.ToString(), $"Created site {site.Name} ({site.Code})");
            }
            return res;
        }
    }

    // ---- Firmware OTA ----
    public record FirmwareReleaseDto(Guid Id, string Version, string Description, string HardwareTarget,
        string BinaryFileName, long FileSizeBytes, string ChecksumSha256, bool IsActive, DateTime CreatedAt, string? ReleaseNotes);

    public record UploadFirmwareRequest(string Version, string Description, string HardwareTarget, string BinaryFileName, long FileSizeBytes, string ReleaseNotes);
    public record RolloutRequest(Guid ReleaseId, string TargetDeviceExternalId);

    public async Task<List<FirmwareReleaseDto>> GetFirmwareReleasesAsync()
    {
        try
        {
            var list = await _firebase.GetFirmwareReleasesAsync();
            return list.Select(f => new FirmwareReleaseDto(
                f.Id,
                f.Version,
                f.Description,
                f.HardwareTarget,
                f.BinaryFileName,
                f.FileSizeBytes,
                f.ChecksumSha256,
                f.IsActive,
                f.CreatedAt,
                f.ReleaseNotes
            )).ToList();
        }
        catch { return new(); }
    }

    public async Task<(bool Success, string? Error)> UploadFirmwareAsync(UploadFirmwareRequest req)
    {
        var release = new FirmwareRelease
        {
            Id = Guid.NewGuid(),
            Version = req.Version,
            Description = req.Description,
            HardwareTarget = req.HardwareTarget,
            BinaryFileName = req.BinaryFileName,
            FileSizeBytes = req.FileSizeBytes,
            ChecksumSha256 = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            ReleaseNotes = req.ReleaseNotes
        };
        var res = await _firebase.CreateFirmwareReleaseAsync(release);
        if (res.Success)
        {
            _ = _firebase.LogAuditActionAsync("Firmware.Upload", "FirmwareRelease", release.Id.ToString(), $"Uploaded firmware {req.Version} for {req.HardwareTarget}");
        }
        return res;
    }

    public async Task<(bool Success, string? Error)> RolloutFirmwareAsync(RolloutRequest req)
    {
        _ = _firebase.LogAuditActionAsync("Firmware.Rollout", "FirmwareRelease", req.ReleaseId.ToString(), $"Triggered OTA rollout to device {req.TargetDeviceExternalId}");
        return (true, null);
    }

    // ---- Auth ----
    public record LoginRequest(string Email, string Password, bool RememberMe);
    public record LoginResponse(Guid UserId, string Email, string FirstName, string LastName,
        string RoleName, bool IsSuperAdmin, List<string> Permissions, string AccessToken, string RefreshToken,
        DateTime AccessTokenExpiry, DateTime RefreshTokenExpiry);

    public async Task<(bool Success, LoginResponse? Data, string? Error)> LoginAsync(string email, string password, bool rememberMe)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/auth/login", new { email, password, rememberMe });
            if (!response.IsSuccessStatusCode)
            {
                var errPayload = await response.Content.ReadFromJsonAsync<ErrorPayload>();
                return (false, null, errPayload?.message ?? $"Login failed ({response.StatusCode}).");
            }

            var resp = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (resp is null)
            {
                return (false, null, "Empty server response.");
            }

            return (true, resp, null);
        }
        catch (Exception ex)
        {
            return (false, null, $"Cannot reach API: {ex.Message}");
        }
    }

    private sealed class ErrorPayload
    {
        public string? message { get; set; }
        public string? error { get; set; }
    }

    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var err = System.Text.Json.JsonSerializer.Deserialize<ErrorPayload>(raw, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return err?.message ?? err?.error;
        }
        catch { return null; }
    }

    // ---- IIoT Device & Sensor Orchestration (API-backed) ----

    public record GatewayDeviceDto(Guid Id, string ExternalId, string Name, string HardwareType,
        string? IpAddress, int Port, int BaudRate, string Parity, int StopBits, int TimeoutMs,
        int MaxSensorCapacity, int SensorCount, bool IsOnline, DateTime? LastSeenAt, DateTime? CreatedAt,
        Guid? SiteId = null);

    public record CreateGatewayDeviceRequest(string Name, string HardwareType, string? IpAddress,
        int? Port, int? BaudRate, string? Parity, int? StopBits, int? TimeoutMs, Guid? SiteId);

    public record SensorLibraryDto(string DriverKey, string SimpleName, string DisplayName,
        string Description, int DefaultSlaveAddress, int DefaultPollIntervalSeconds,
        ushort StartRegister, ushort RegisterQuantity, byte FunctionCode,
        string UnitPrimary, string UnitSecondary);

    public record AttachedSensorDto(Guid Id, string UniqueSensorId, string Name, Guid DeviceId,
        string DeviceName, string SensorTypeKey, string DriverDisplayName, int SlaveAddress,
        int PollIntervalSeconds, double CalibrationMultiplier, string? TelemetryTableName,
        bool IsActive, bool IsOnline, double? LatestPrimary, double? LatestSecondary,
        string? UnitPrimary, string? UnitSecondary, DateTime? LatestTimestampUtc,
        string? MetricFields, string? Config);

    public record CreateSensorRequest(Guid DeviceId, string SensorName, string SensorTypeKey,
        int SlaveAddress, int PollIntervalSeconds, double? CalibrationMultiplier,
        Dictionary<string, string>? SelectedParameters = null, string? SensorConfig = null);

    public record UpdateSensorRequest(string? SensorName, int? PollIntervalSeconds,
        double? CalibrationMultiplier, Dictionary<string, string>? SelectedParameters = null,
        string? SensorConfig = null);

    public record SensorReadingDto(DateTime TimestampUtc, string? RawHexBuffer,
        double PrimaryValue, double SecondaryValue, short ConnectionStatus, string? ErrorCode);

    // Smart & Fast RS-485 bus scan result (discovery only - nothing persisted).
    public record ScannedMeterDto(
        int SlaveAddress,
        string? DriverKey,
        string? SimpleName,
        string? DisplayName,
        ushort StartRegister,
        ushort RegisterQuantity,
        string? UnitPrimary,
        string? UnitSecondary,
        int DefaultPollIntervalSeconds,
        string SuggestedName,
        double? LivePrimary,
        double? LiveSecondary,
        string Status,
        bool IsAmbiguous = false,
        List<ScannedCandidateDto>? Candidates = null,
        string? ProfileType = null,
        string? RawHexPayload = null);

    public record ScannedCandidateDto(
        string DriverKey,
        string DisplayName,
        ushort StartRegister,
        ushort RegisterQuantity,
        double? LivePrimary,
        double? LiveSecondary,
        bool ProofServed,
        bool BestGuess);

    public record AlreadyInSystemSlaveDto(
        int SlaveAddress,
        string? ExistingSensorName,
        string Message);

    public record DuplicateIdConflictDto(
        int SlaveAddress,
        int SensorCount,
        string? DriverName,
        string Message,
        List<string>? CollidingMeterNames = null);

    public record GatewayScanResultDto(
        Guid DeviceId,
        string DeviceName,
        string? GatewayIp,
        int GatewayPort,
        int ProbeTimeoutMs,
        int SlavesScanned,
        int RespondingSlaves,
        int UnknownResponders,
        bool ConnectivityOk,
        string? ErrorCode,
        DateTime ScannedAtUtc,
        List<ScannedMeterDto> Found,
        List<int> SkippedSlaves,
        List<int>? AmbiguousSlaves = null,
        List<DuplicateIdConflictDto>? DuplicateIdConflicts = null,
        List<AlreadyInSystemSlaveDto>? AlreadyInSystemSlaves = null);

    public async Task<List<GatewayDeviceDto>> GetGatewayDevicesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<GatewayDeviceDto>>("/api/devices");
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<(bool Success, GatewayDeviceDto? Data, string? Error)> CreateGatewayDeviceAsync(CreateGatewayDeviceRequest dto)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/devices", dto);
            if (!response.IsSuccessStatusCode)
            {
                var err = await ReadErrorMessageAsync(response);
                return (false, null, err ?? $"Create failed ({response.StatusCode}).");
            }
            var device = await response.Content.ReadFromJsonAsync<GatewayDeviceDto>();
            return (true, device, null);
        }
        catch (Exception ex) { return (false, null, $"Cannot reach API: {ex.Message}"); }
    }

    public async Task<(bool Success, string? Error)> DeleteGatewayDeviceAsync(Guid id)
    {
        try
        {
            var response = await _http.DeleteAsync($"/api/devices/{id}");
            if (!response.IsSuccessStatusCode)
            {
                var err = await ReadErrorMessageAsync(response);
                return (false, err ?? $"Delete failed ({response.StatusCode}).");
            }
            return (true, null);
        }
        catch (Exception ex) { return (false, $"Cannot reach API: {ex.Message}"); }
    }

    public async Task<List<SensorLibraryDto>> GetSensorLibrariesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<SensorLibraryDto>>("/api/sensors/libraries");
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<AttachedSensorDto>> GetSensorsAsync(Guid? deviceId = null)
    {
        try
        {
            var url = deviceId is null ? "/api/sensors" : $"/api/sensors?deviceId={deviceId}";
            var result = await _http.GetFromJsonAsync<List<AttachedSensorDto>>(url);
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<(bool Success, AttachedSensorDto? Data, string? Error)> CreateSensorAsync(CreateSensorRequest dto)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/sensors", dto);
            if (!response.IsSuccessStatusCode)
            {
                var err = await ReadErrorMessageAsync(response);
                return (false, null, err ?? $"Create failed ({response.StatusCode}).");
            }
            var sensor = await response.Content.ReadFromJsonAsync<AttachedSensorDto>();
            return (true, sensor, null);
        }
        catch (Exception ex) { return (false, null, $"Cannot reach API: {ex.Message}"); }
    }

    public async Task<(bool Success, AttachedSensorDto? Data, string? Error)> UpdateSensorAsync(Guid id, UpdateSensorRequest dto)
    {
        try
        {
            var response = await _http.PutAsJsonAsync($"/api/sensors/{id}", dto);
            if (!response.IsSuccessStatusCode)
            {
                var err = await ReadErrorMessageAsync(response);
                return (false, null, err ?? $"Update failed ({response.StatusCode}).");
            }
            var sensor = await response.Content.ReadFromJsonAsync<AttachedSensorDto>();
            return (true, sensor, null);
        }
        catch (Exception ex) { return (false, null, $"Cannot reach API: {ex.Message}"); }
    }

    public async Task<(bool Success, string? Error)> DeleteSensorAsync(Guid id)
    {
        try
        {
            var response = await _http.DeleteAsync($"/api/sensors/{id}");
            if (!response.IsSuccessStatusCode)
            {
                var err = await ReadErrorMessageAsync(response);
                return (false, err ?? $"Delete failed ({response.StatusCode}).");
            }
            return (true, null);
        }
        catch (Exception ex) { return (false, $"Cannot reach API: {ex.Message}"); }
    }

    public async Task<List<SensorReadingDto>> GetSensorTelemetryAsync(Guid id, int count = 50)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<SensorReadingDto>>($"/api/sensors/{id}/telemetry?count={count}");
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<SensorReadingDto>> GetSensorTelemetryRangeAsync(Guid id, DateTime fromLocal, DateTime toLocal, int limit = 2000)
    {
        try
        {
            var from = Uri.EscapeDataString(fromLocal.ToUniversalTime().ToString("O"));
            var to = Uri.EscapeDataString(toLocal.ToUniversalTime().ToString("O"));
            var result = await _http.GetFromJsonAsync<List<SensorReadingDto>>($"/api/sensors/{id}/telemetry?count={limit}&from={from}&to={to}");
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<byte[]?> GetSensorTelemetryPdfAsync(Guid id, DateTime fromLocal, DateTime toLocal)
    {
        try
        {
            var from = Uri.EscapeDataString(fromLocal.ToUniversalTime().ToString("O"));
            var to = Uri.EscapeDataString(toLocal.ToUniversalTime().ToString("O"));
            var response = await _http.GetAsync($"/api/sensors/{id}/telemetry/export-pdf?from={from}&to={to}");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }

    public async Task<GatewayScanResultDto?> ScanGatewayBusAsync(
        Guid deviceId,
        int probeTimeoutMs = 200,
        int startAddress = 1,
        int endAddress = 247,
        int passes = 0,
        bool deepDuplicateCheck = true)
    {
        try
        {
            var body = new
            {
                probeTimeoutMs,
                startAddress,
                endAddress,
                passes,
                deepDuplicateCheck
            };
            var response = await _scan.PostAsJsonAsync($"/api/devices/{deviceId}/scan", body);
            var raw = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                if (response.IsSuccessStatusCode)
                {
                    return System.Text.Json.JsonSerializer.Deserialize<GatewayScanResultDto>(raw, options);
                }
                var wrapper = System.Text.Json.JsonSerializer.Deserialize<ScanBusErrorDto>(raw, options);
                return wrapper?.scan;
            }
            catch { return null; }
        }
        catch { return null; }
    }

    public record ReachabilityResultDto(bool reachable, bool online, int? latencyMs, string message);

    public async Task<ReachabilityResultDto?> CheckReachabilityAsync(Guid deviceId)
    {
        try
        {
            return await _http.GetFromJsonAsync<ReachabilityResultDto>($"/api/devices/{deviceId}/reachability");
        }
        catch { return null; }
    }

    private sealed class ScanBusErrorDto
    {
        public string? message { get; set; }
        public GatewayScanResultDto? scan { get; set; }
    }

    // ---- Users (Fast Local API with fallback) ----
    public record UserDto(Guid Id, string FirstName, string LastName, string Email, string? PhoneNumber,
        string RoleName, bool IsHardcodedSuperAdmin, DateTime CreatedAt);
    public record CreateUserRequest(string FirstName, string LastName, string Email, string? PhoneNumber, string Password, string RoleName);
    public record UpdateUserRequest(string FirstName, string LastName, string? PhoneNumber, string RoleName, string? NewPassword);

    public async Task<List<UserDto>> GetUsersAsync()
    {
        try
        {
            var apiUsers = await _http.GetFromJsonAsync<List<UserDto>>("/api/users");
            if (apiUsers is { Count: > 0 }) return apiUsers;
        }
        catch { }

        try
        {
            var users = await _firebase.GetUsersAsync();
            var roles = await _firebase.GetRolesAsync();
            var userRoles = await _firebase.GetUserRolesAsync();

            return users.Select(u =>
            {
                Guid.TryParse(u.Id, out var uid);
                var mapping = userRoles.FirstOrDefault(ur => ur.UserId == u.Id);
                var role = roles.FirstOrDefault(r => r.Id == mapping?.RoleId);
                var roleName = role?.Name ?? (u.IsHardcodedSuperAdmin ? "SuperAdmin" : "Operator");

                return new UserDto(
                    uid != Guid.Empty ? uid : Guid.NewGuid(),
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.PhoneNumber,
                    roleName,
                    u.IsHardcodedSuperAdmin,
                    u.CreatedAt
                );
            }).ToList();
        }
        catch { return new(); }
    }

    public async Task<(bool Success, string? Error)> CreateUserAsync(CreateUserRequest dto)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("/api/users", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Failed ({res.StatusCode})");
        }
        catch
        {
            return await _firebase.CreateUserAsync(dto.FirstName, dto.LastName, dto.Email, dto.PhoneNumber, dto.Password, dto.RoleName);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateUserAsync(Guid id, UpdateUserRequest dto)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"/api/users/{id}", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Failed ({res.StatusCode})");
        }
        catch
        {
            return await _firebase.UpdateUserAsync(id.ToString(), dto.FirstName, dto.LastName, dto.PhoneNumber, dto.RoleName, dto.NewPassword);
        }
    }

    public async Task<(bool Success, string? Error)> DeleteUserAsync(Guid id)
    {
        try
        {
            var res = await _http.DeleteAsync($"/api/users/{id}");
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Failed ({res.StatusCode})");
        }
        catch
        {
            return await _firebase.DeleteUserAsync(id.ToString());
        }
    }

    // ---- Roles / Permissions (Fast Local API with fallback) ----
    public record RoleDto(Guid Id, string Name, string? Description, bool IsSuperAdminRole, int UserCount, Dictionary<string, bool> Permissions);
    public record CreateRoleRequest(string Name, string? Description);
    public record UpdateRolePermissionsRequest(Dictionary<string, bool> Permissions);
    public record PermissionsCatalogDto(List<string> Tabs, List<string> Actions);

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        try
        {
            var apiRoles = await _http.GetFromJsonAsync<List<RoleDto>>("/api/roles");
            if (apiRoles is { Count: > 0 }) return apiRoles;
        }
        catch { }

        try
        {
            var roles = await _firebase.GetRolesAsync();
            var userRoles = await _firebase.GetUserRolesAsync();
            var perms = await _firebase.GetRolePermissionsAsync();

            return roles.Select(r =>
            {
                Guid.TryParse(r.Id, out var rid);
                bool isSuperAdmin = string.Equals(r.Name, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
                int userCount = userRoles.Count(ur => ur.RoleId == r.Id);

                var rolePerms = perms
                    .Where(p => p.RoleId == r.Id)
                    .ToDictionary(p => p.TabKey, p => p.IsGranted);

                return new RoleDto(
                    rid != Guid.Empty ? rid : Guid.NewGuid(),
                    r.Name,
                    r.Description,
                    isSuperAdmin,
                    userCount,
                    rolePerms
                );
            }).ToList();
        }
        catch { return new(); }
    }

    public async Task<PermissionsCatalogDto> GetPermissionsCatalogAsync()
    {
        try
        {
            var catalog = await _http.GetFromJsonAsync<PermissionsCatalogDto>("/api/roles/permissions-catalog");
            if (catalog is not null) return catalog;
        }
        catch { }

        return new PermissionsCatalogDto(
            AppTabs.All.ToList(),
            AppPermissions.All.ToList()
        );
    }

    public async Task<(bool Success, string? Error)> CreateRoleAsync(CreateRoleRequest dto)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("/api/roles", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Failed ({res.StatusCode})");
        }
        catch
        {
            return await _firebase.CreateRoleAsync(dto.Name, dto.Description);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateRolePermissionsAsync(Guid roleId, Dictionary<string, bool> permissions)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"/api/roles/{roleId}/permissions", new UpdateRolePermissionsRequest(permissions));
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Failed ({res.StatusCode})");
        }
        catch
        {
            return await _firebase.UpdateRolePermissionsAsync(roleId.ToString(), permissions);
        }
    }

    public async Task<(bool Success, string? Error)> DeleteRoleAsync(Guid id)
    {
        try
        {
            var res = await _http.DeleteAsync($"/api/roles/{id}");
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await res.Content.ReadFromJsonAsync<ErrorPayload>();
            return (false, err?.message ?? $"Failed ({res.StatusCode})");
        }
        catch
        {
            return await _firebase.DeleteRoleAsync(id.ToString());
        }
    }
}
