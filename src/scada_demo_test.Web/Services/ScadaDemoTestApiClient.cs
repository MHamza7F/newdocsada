using System.Net.Http.Json;

namespace scada_demo_test.Web.Services;

public class ScadaDemoTestApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _scan;
    public ScadaDemoTestApiClient(HttpClient http, HttpClient scan)
    {
        _http = http;
        _scan = scan;
    }

    public void Dispose()
    {
        _http.Dispose();
        _scan.Dispose();
    }

    public string BaseUrl => _http.BaseAddress?.ToString()?.TrimEnd('/') ?? string.Empty;

    // ---- Devices ----
    public record DeviceDto(Guid Id, string ExternalId, string Name, string DeviceType, string Status, int? ModbusSlaveId, string? IpAddress, DateTime? LastSeenAt);
    public record ReadingPointDto(DateTime Timestamp, double Value, string? Unit);
    public record LatestSnapshotDto(Guid Id, string ExternalId, string Name, string DeviceType, string Status,
        int? ModbusSlaveId, string? IpAddress, DateTime? LastSeenAt,
        double FlowRate, string FlowUnit, double Totalizer, string TotalizerUnit);

    public record CreateDeviceRequest(string ExternalId, string Name, string DeviceType, string Protocol, int? ModbusSlaveId, string? IpAddress, Guid? SiteId);

    public async Task<List<DeviceDto>> GetDevicesAsync()
    {
        var devices = await _http.GetFromJsonAsync<List<GatewayDeviceDto>>("/api/devices")
            ?? throw new HttpRequestException("Empty devices API response.");
        return devices.Select(d => new DeviceDto(d.Id, d.ExternalId, d.Name, d.HardwareType,
            d.IsOnline ? "Online" : "Offline", null, d.IpAddress, d.LastSeenAt)).ToList();
    }

    public async Task<List<LatestSnapshotDto>> GetLatestSnapshotAsync()
    {
        return await _http.GetFromJsonAsync<List<LatestSnapshotDto>>("/api/readings/latest")
            ?? throw new HttpRequestException("Empty latest readings API response.");
    }

    public async Task<List<ReadingPointDto>> GetReadingsAsync(string externalId, string metric = "FlowRate", int count = 30)
    {
        var readings = await _http.GetFromJsonAsync<List<ReadingPointDto>>(
            $"/api/readings/{Uri.EscapeDataString(externalId)}?metric={Uri.EscapeDataString(metric)}&count={count}");
        return readings ?? throw new HttpRequestException("Empty readings API response.");
    }

    public async Task<(bool Success, string? Error)> CreateDeviceAsync(CreateDeviceRequest dto)
    {
        var res = await _http.PostAsJsonAsync("/api/devices", new CreateGatewayDeviceRequest(
            dto.Name, dto.DeviceType, dto.IpAddress, 502, 9600, "None", 1, 2000, dto.SiteId));
        if (res.IsSuccessStatusCode) return (true, null);
        return (false, await ReadErrorMessageAsync(res) ?? $"Create device failed ({res.StatusCode}).");
    }

    public async Task<(bool Success, string? Error)> UpdateDeviceAsync(Guid id, CreateDeviceRequest dto)
    {
        var res = await _http.PutAsJsonAsync($"/api/devices/{id}", new CreateGatewayDeviceRequest(
            dto.Name, dto.DeviceType, dto.IpAddress, 502, 9600, "None", 1, 2000, dto.SiteId));
        return res.IsSuccessStatusCode ? (true, null) : (false, await ReadErrorMessageAsync(res) ?? $"Update device failed ({res.StatusCode}).");
    }

    public async Task<(bool Success, string? Error)> DeleteDeviceAsync(Guid id)
    {
        var res = await _http.DeleteAsync($"/api/devices/{id}");
        return res.IsSuccessStatusCode ? (true, null) : (false, await ReadErrorMessageAsync(res) ?? $"Delete device failed ({res.StatusCode}).");
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
        var url = $"/api/reports/{Uri.EscapeDataString(externalId)}/history?metric={Uri.EscapeDataString(metric)}" +
            $"&from={Uri.EscapeDataString(from.ToUniversalTime().ToString("O"))}&to={Uri.EscapeDataString(to.ToUniversalTime().ToString("O"))}";
        return await _http.GetFromJsonAsync<HistorySeriesDto>(url)
            ?? throw new HttpRequestException("Empty history API response.");
    }

    // ---- Storage Tanks ----
    public record StorageTankDto(Guid Id, Guid? SiteId, string TankCode, string Name, double CapacityLiters,
        double CurrentVolumeLiters, double LevelPercentage, double TemperatureCelsius, string Status,
        string LiquidType, double InletFlowRate, double OutletFlowRate, DateTime LastUpdatedAt);

    public record CreateTankRequest(string TankCode, string Name, double CapacityLiters, string LiquidType, Guid? SiteId);
    public record UpdateTankRequest(string Name, double CapacityLiters, string LiquidType);

    public async Task<List<StorageTankDto>> GetTanksAsync()
    {
        return await _http.GetFromJsonAsync<List<StorageTankDto>>("/api/tanks")
            ?? throw new HttpRequestException("Empty tanks API response.");
    }

    public async Task<(bool Success, string? Error)> CreateTankAsync(CreateTankRequest dto)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("/api/tanks", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Create tank failed ({res.StatusCode})");
        }
        catch (HttpRequestException ex) when (IsAuthorizationFailure(ex)) { return (false, ex.Message); }
        catch (Exception ex) { return (false, $"Tanks API request failed: {ex.Message}"); }
    }

    public async Task<(bool Success, string? Error)> UpdateTankAsync(Guid id, UpdateTankRequest dto)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"/api/tanks/{id}", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Update tank failed ({res.StatusCode})");
        }
        catch (HttpRequestException ex) when (IsAuthorizationFailure(ex)) { return (false, ex.Message); }
        catch (Exception ex) { return (false, $"Tanks API request failed: {ex.Message}"); }
    }

    public async Task<(bool Success, string? Error)> DeleteTankAsync(Guid id)
    {
        try
        {
            var res = await _http.DeleteAsync($"/api/tanks/{id}");
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Delete tank failed ({res.StatusCode})");
        }
        catch (HttpRequestException ex) when (IsAuthorizationFailure(ex)) { return (false, ex.Message); }
        catch (Exception ex) { return (false, $"Tanks API request failed: {ex.Message}"); }
    }

    // ---- Alerts (API-backed) ----
    public record AlertIncidentDto(Guid Id, Guid? AlertRuleId, string DeviceExternalId, string DeviceName,
        string Metric, double TriggerValue, string Message, string Severity, DateTime TriggeredAt,
        DateTime? AcknowledgedAt, string? AcknowledgedBy, bool IsResolved, DateTime? ResolvedAt);

    public record AlertRuleDto(Guid Id, Guid? DeviceId, string DeviceExternalId, string Metric,
        string Condition, double ThresholdValue, string Severity, bool IsEnabled, string? NotificationEmail,
        string? NotificationPhone, DateTime CreatedAt);

    public async Task<List<AlertIncidentDto>> GetAlertIncidentsAsync(int limit = 50)
    {
        return await _http.GetFromJsonAsync<List<AlertIncidentDto>>($"/api/alerts/incidents?limit={limit}")
            ?? throw new HttpRequestException("Empty alert incidents API response.");
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
        return await _http.GetFromJsonAsync<List<AlertRuleDto>>("/api/alerts/rules")
            ?? throw new HttpRequestException("Empty alert rules API response.");
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
        var q = $"/api/audit-logs?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) q += $"&search={Uri.EscapeDataString(search)}";
        return await _http.GetFromJsonAsync<AuditLogsPagedDto>(q)
            ?? throw new HttpRequestException("Empty audit logs API response.");
    }

    // ---- Sites (Multi-Plant) ----
    public record SiteDto(Guid Id, string Code, string Name, string Location, string? Description, DateTime CreatedAt);

    public async Task<List<SiteDto>> GetSitesAsync()
    {
        return await _http.GetFromJsonAsync<List<SiteDto>>("/api/sites")
            ?? throw new HttpRequestException("Empty sites API response.");
    }

    public async Task<(bool Success, string? Error)> CreateSiteAsync(SiteDto dto)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("/api/sites", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Create site failed ({res.StatusCode})");
        }
        catch (HttpRequestException ex) when (IsAuthorizationFailure(ex)) { return (false, ex.Message); }
        catch (Exception ex) { return (false, $"Sites API request failed: {ex.Message}"); }
    }

    // ---- Firmware OTA ----
    public record FirmwareReleaseDto(Guid Id, string Version, string Description, string HardwareTarget,
        string BinaryFileName, long FileSizeBytes, string ChecksumSha256, bool IsActive, DateTime CreatedAt, string? ReleaseNotes);

    public record UploadFirmwareRequest(string Version, string Description, string HardwareTarget, string BinaryFileName, long FileSizeBytes, string ReleaseNotes);
    public record RolloutRequest(Guid ReleaseId, string TargetDeviceExternalId);

    public async Task<List<FirmwareReleaseDto>> GetFirmwareReleasesAsync()
    {
        return await _http.GetFromJsonAsync<List<FirmwareReleaseDto>>("/api/firmware")
            ?? throw new HttpRequestException("Empty firmware API response.");
    }

    public async Task<(bool Success, string? Error)> UploadFirmwareAsync(UploadFirmwareRequest req)
    {
        using var res = await _http.PostAsJsonAsync("/api/firmware", req);
        return res.IsSuccessStatusCode ? (true, null) :
            (false, await ReadErrorMessageAsync(res) ?? $"Firmware upload failed ({res.StatusCode}).");
    }

    public async Task<(bool Success, string? Error)> RolloutFirmwareAsync(RolloutRequest req)
    {
        return (false, "Firmware rollout is not supported by the API.");
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
            using var response = await _http.PostAsJsonAsync("/api/auth/login", new { email, password, rememberMe });
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

    public async Task LogoutAsync(string accessToken, string refreshToken)
    {
        // Local sign-out must still complete when the API is offline.
        try
        {
            using var response = await _http.PostAsJsonAsync("/api/auth/logout", new { accessToken, refreshToken });
        }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) { }
    }

    private static bool IsAuthorizationFailure(HttpRequestException exception) =>
        exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        // Denials are terminal even when the API returns an empty/non-JSON body.
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            return $"API {(int)response.StatusCode} ({response.StatusCode}) — access denied.";

        try
        {
            var raw = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var err = System.Text.Json.JsonSerializer.Deserialize<ErrorPayload>(raw, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return err?.message ?? err?.error ?? raw;
        }
        catch { return null; }
    }

    // ---- IIoT Device & Sensor Orchestration (API-backed) ----

    public record GatewayDeviceDto(Guid Id, string ExternalId, string Name, string HardwareType,
        string? IpAddress, int Port, int BaudRate, string Parity, int StopBits, int TimeoutMs,
        int MaxSensorCapacity, int SensorCount, bool IsOnline, DateTime? LastSeenAt, DateTime? CreatedAt,
        Guid? SiteId = null, string ProvisionedVia = "Norvi");

    public record CreateGatewayDeviceRequest(string Name, string HardwareType, string? IpAddress,
        int? Port, int? BaudRate, string? Parity, int? StopBits, int? TimeoutMs, Guid? SiteId,
        string? ProvisionedVia = null);

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
        return await _http.GetFromJsonAsync<List<GatewayDeviceDto>>("/api/devices")
            ?? throw new HttpRequestException("Empty devices API response.");
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
        return await _http.GetFromJsonAsync<List<SensorLibraryDto>>("/api/sensors/libraries")
            ?? throw new HttpRequestException("Empty sensor libraries API response.");
    }

    public async Task<List<AttachedSensorDto>> GetSensorsAsync(Guid? deviceId = null)
    {
        var url = deviceId is null ? "/api/sensors" : $"/api/sensors?deviceId={deviceId}";
        return await _http.GetFromJsonAsync<List<AttachedSensorDto>>(url)
            ?? throw new HttpRequestException("Empty sensors API response.");
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
        return await _http.GetFromJsonAsync<List<SensorReadingDto>>($"/api/sensors/{id}/telemetry?count={count}")
            ?? throw new HttpRequestException("Empty telemetry API response.");
    }

    public async Task<List<SensorReadingDto>> GetSensorTelemetryRangeAsync(Guid id, DateTime fromLocal, DateTime toLocal, int limit = 2000)
    {
        var from = Uri.EscapeDataString(fromLocal.ToUniversalTime().ToString("O"));
        var to = Uri.EscapeDataString(toLocal.ToUniversalTime().ToString("O"));
        return await _http.GetFromJsonAsync<List<SensorReadingDto>>($"/api/sensors/{id}/telemetry?count={limit}&from={from}&to={to}")
            ?? throw new HttpRequestException("Empty telemetry API response.");
    }

    public async Task<byte[]?> GetSensorTelemetryPdfAsync(Guid id, DateTime fromLocal, DateTime toLocal)
    {
        var from = Uri.EscapeDataString(fromLocal.ToUniversalTime().ToString("O"));
        var to = Uri.EscapeDataString(toLocal.ToUniversalTime().ToString("O"));
        using var response = await _http.GetAsync($"/api/sensors/{id}/telemetry/export-pdf?from={from}&to={to}");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await ReadErrorMessageAsync(response) ?? $"Telemetry export failed ({response.StatusCode}).");
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<GatewayScanResultDto?> ScanGatewayBusAsync(
        Guid deviceId,
        int probeTimeoutMs = 200,
        int startAddress = 1,
        int endAddress = 247,
        int passes = 0,
        bool deepDuplicateCheck = true)
    {
        var body = new { probeTimeoutMs, startAddress, endAddress, passes, deepDuplicateCheck };
        using var response = await _scan.PostAsJsonAsync($"/api/devices/{deviceId}/scan", body);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await ReadErrorMessageAsync(response) ?? $"Gateway scan failed ({response.StatusCode}).");
        var result = System.Text.Json.JsonSerializer.Deserialize<GatewayScanResultDto>(raw,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? throw new HttpRequestException("Empty gateway scan API response.");
    }

    public record ReachabilityResultDto(bool reachable, bool online, int? latencyMs, string message);

    public async Task<ReachabilityResultDto?> CheckReachabilityAsync(Guid deviceId)
    {
        return await _http.GetFromJsonAsync<ReachabilityResultDto>($"/api/devices/{deviceId}/reachability")
            ?? throw new HttpRequestException("Empty reachability API response.");
    }

    private sealed class ScanBusErrorDto
    {
        public string? message { get; set; }
        public GatewayScanResultDto? scan { get; set; }
    }

    // ---- Users (API-only) ----
    public record UserDto(Guid Id, string FirstName, string LastName, string Email, string? PhoneNumber,
        string RoleName, bool IsHardcodedSuperAdmin, DateTime CreatedAt);
    public record CreateUserRequest(string FirstName, string LastName, string Email, string? PhoneNumber, string Password, string RoleName);
    public record UpdateUserRequest(string FirstName, string LastName, string? PhoneNumber, string RoleName, string? NewPassword);

    public async Task<List<UserDto>> GetUsersAsync()
    {
        return await _http.GetFromJsonAsync<List<UserDto>>("/api/users")
            ?? throw new HttpRequestException("Empty users API response.");
    }

    public async Task<(bool Success, string? Error)> CreateUserAsync(CreateUserRequest dto)
    {
        try
        {
            using var res = await _http.PostAsJsonAsync("/api/users", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Failed ({res.StatusCode})");
        }
        catch (Exception ex)
        {
            return (false, $"Users API request failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> UpdateUserAsync(Guid id, UpdateUserRequest dto)
    {
        try
        {
            using var res = await _http.PutAsJsonAsync($"/api/users/{id}", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Failed ({res.StatusCode})");
        }
        catch (Exception ex)
        {
            return (false, $"Users API request failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> DeleteUserAsync(Guid id)
    {
        try
        {
            using var res = await _http.DeleteAsync($"/api/users/{id}");
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Failed ({res.StatusCode})");
        }
        catch (Exception ex)
        {
            return (false, $"Users API request failed: {ex.Message}");
        }
    }

    // ---- Roles / Permissions (API-only) ----
    public record RoleDto(Guid Id, string Name, string? Description, bool IsSuperAdminRole, int UserCount, Dictionary<string, bool> Permissions);
    public record CreateRoleRequest(string Name, string? Description);
    public record UpdateRolePermissionsRequest(Dictionary<string, bool> Permissions);
    public record PermissionsCatalogDto(List<string> Tabs, List<string> Actions);

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        return await _http.GetFromJsonAsync<List<RoleDto>>("/api/roles")
            ?? throw new HttpRequestException("Empty roles API response.");
    }

    public async Task<PermissionsCatalogDto> GetPermissionsCatalogAsync()
    {
        return await _http.GetFromJsonAsync<PermissionsCatalogDto>("/api/roles/permissions-catalog")
            ?? throw new HttpRequestException("Empty permissions catalog API response.");
    }

    public async Task<(bool Success, string? Error)> CreateRoleAsync(CreateRoleRequest dto)
    {
        try
        {
            using var res = await _http.PostAsJsonAsync("/api/roles", dto);
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Failed ({res.StatusCode})");
        }
        catch (Exception ex)
        {
            return (false, $"Roles API request failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> UpdateRolePermissionsAsync(Guid roleId, Dictionary<string, bool> permissions)
    {
        try
        {
            using var res = await _http.PutAsJsonAsync($"/api/roles/{roleId}/permissions", new UpdateRolePermissionsRequest(permissions));
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Failed ({res.StatusCode})");
        }
        catch (Exception ex)
        {
            return (false, $"Roles API request failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> DeleteRoleAsync(Guid id)
    {
        try
        {
            using var res = await _http.DeleteAsync($"/api/roles/{id}");
            if (res.IsSuccessStatusCode) return (true, null);
            var err = await ReadErrorMessageAsync(res);
            return (false, err ?? $"Failed ({res.StatusCode})");
        }
        catch (Exception ex)
        {
            return (false, $"Roles API request failed: {ex.Message}");
        }
    }
}
