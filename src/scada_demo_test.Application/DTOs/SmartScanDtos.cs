namespace scada_demo_test.Application.DTOs;

// Contract for the Smart & Fast RS-485 bus scan (POST /api/devices/{id}/scan).
// The scanner is handed everything it needs (gateway IP/port, timeouts, the
// address range to probe and the list of slave addresses already registered)
// so it stays decoupled from EF and never touches the database - discovery
// happens purely over Modbus + the Dual-Guard In-Memory Pipeline.
//
// Passes <= 0 means one full pass over the explicit address range. Retry passes
// enrich one address's evidence, not the count of physical meters. Quick scans
// explicitly request 1..20. DeepDuplicateCheck remains wire-compatible, but can
// never disable mandatory fingerprint validation or the batch duplicate guard.
public record SmartScanRequest(
    Guid DeviceId,
    string DeviceName,
    string GatewayIp,
    int GatewayPort,
    int ConnectTimeoutMs,
    int ProbeTimeoutMs,
    IReadOnlyList<int> SkipSlaveAddresses,
    int StartAddress = 1,
    int EndAddress = 247,
    int Passes = 0,
    bool DeepDuplicateCheck = true,
    IReadOnlyDictionary<int, string>? RegisteredSlaveNames = null);

// One possible driver match for a slave address, with the live values that
// driver's own identification window decoded and whether that driver could
// corroborate itself (its own proof block was served with data).
public record ScannedCandidateDto(
    string DriverKey,
    string DisplayName,
    ushort StartRegister,
    ushort RegisterQuantity,
    double? LivePrimary,
    double? LiveSecondary,
    bool ProofServed,
    bool BestGuess);

// Bucket 3 ("Found Box" / UniqueFoundDevices):
// One meter discovered during the scan, verified by Dual-Guard + CheckpostRouter
// and calculated via SensorDriverDispatcher ("Ghar" methods).
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
    string Status, // "identified" | "unknown"
    bool IsAmbiguous = false,
    IReadOnlyList<ScannedCandidateDto>? Candidates = null,
    string? ProfileType = null,
    string? RawHexPayload = null);

// Bucket 1 (Guard 1 - DB Existence Guard / AlreadyInSystemDevices):
// Slave address already registered in the database/system.
public record AlreadyInSystemSlaveDto(
    int SlaveAddress,
    string? ExistingSensorName,
    string Message);

// Bucket 2 (Guard 2 - Current Scan Duplicate Guard / BlockedScanDuplicates):
// Two or more meters appear to share ONE slave address during the scan session.
// Excluded from Found Box and reported here instead.
public record DuplicateSlaveIdConflictDto(
    int SlaveAddress,
    int SensorCount,
    string? DriverName,
    string Message,
    List<string>? CollidingSensorNames = null);

public record SmartScanResultDto(
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
    IReadOnlyList<ScannedMeterDto> Found,
    IReadOnlyList<int> SkippedSlaves,
    IReadOnlyList<int>? AmbiguousSlaves = null,
    IReadOnlyList<DuplicateSlaveIdConflictDto>? DuplicateIdConflicts = null,
    IReadOnlyList<AlreadyInSystemSlaveDto>? AlreadyInSystemSensors = null);
