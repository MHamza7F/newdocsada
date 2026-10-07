using System.Runtime.CompilerServices;
using ScadaEngine.Core.Drivers;
using ScadaEngine.Core.Models;

namespace ScadaEngine.Core.Services;

/// <summary>
/// STEP 3: Dual-Guard Scanner Service (<c>ModbusScannerService</c>).
/// Implements the strict Two-Stage Filter Pipeline BEFORE any device can enter the "Found Box":
/// <para>
///   [ SCANNER LOOP (Slave ID 1 to 247) ]
///                 |
///                 v
///   [ RS-485 / Gateway Raw Response ]
///                 |
///                 v
///   CHECKPOINT 1 (Guard 1 — DB Existence Guard):
///     Checks O(1) <c>HashSet&lt;byte&gt; _dbRegisteredSlaveIds</c>.
///     - IF EXISTS IN DB -&gt; Bucket 1: <see cref="ScanResultSummary.AlreadyInSystemDevices"/>
///       (UI Prompt: "Already added in system. Change hardware ID"). Never pushed to RawBucket.
///     - IF NOT IN DB    -&gt; Pushed to temporary <c>RawBucket</c>.
///                 |
///                 v
///   CHECKPOINT 2 (Guard 2 — Current Scan Duplicate Guard, BEFORE Found Box):
///     Groups <c>RawBucket</c> by <c>SlaveId</c>.
///     - IF <c>Count() &gt; 1</c> -&gt; Bucket 2: <see cref="ScanResultSummary.BlockedScanDuplicates"/>
///       (UI Prompt: "Bus conflict: Same ID responded multiple times"). Blocked from Found Box.
///     - IF <c>Count() == 1</c> -&gt; Sent to <see cref="CheckpostRouter.InspectAndTag(RawScanResponse)"/>.
///                 |
///                 v
///   FOUND BOX TRANSFER (Bucket 3):
///     Verified <see cref="ModbusDevicePacket"/> placed into <see cref="ScanResultSummary.UniqueFoundDevices"/>
///     and dispatched via <see cref="SensorDriverDispatcher.Dispatch"/> ("Ghar" calculation engine).
/// </para>
/// </summary>
public sealed class ModbusScannerService
{
    private readonly HashSet<byte> _dbRegisteredSlaveIds;
    private readonly Dictionary<byte, (string? SensorName, string? DriverKey)> _dbRegisteredMetadata;
    private readonly object _syncLock = new();

    /// <summary>
    /// Initializes a new Dual-Guard Scanner session with the Slave IDs currently registered
    /// in the database for this USR-W610 gateway.
    /// </summary>
    public ModbusScannerService(
        IEnumerable<int>? registeredSlaveIds = null,
        IReadOnlyDictionary<int, (string? SensorName, string? DriverKey)>? registeredMetadata = null)
    {
        _dbRegisteredSlaveIds = new HashSet<byte>();
        _dbRegisteredMetadata = new Dictionary<byte, (string? SensorName, string? DriverKey)>();

        if (registeredSlaveIds is not null)
        {
            foreach (var id in registeredSlaveIds)
            {
                if (id is >= 1 and <= 247)
                {
                    _dbRegisteredSlaveIds.Add((byte)id);
                }
            }
        }

        if (registeredMetadata is not null)
        {
            foreach (var kv in registeredMetadata)
            {
                if (kv.Key is >= 1 and <= 247)
                {
                    var b = (byte)kv.Key;
                    _dbRegisteredSlaveIds.Add(b);
                    _dbRegisteredMetadata[b] = kv.Value;
                }
            }
        }
    }

    /// <summary>
    /// O(1) check whether a Slave ID (1..247) is already registered in the database.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsRegisteredInDatabase(byte slaveId)
    {
        lock (_syncLock)
        {
            return _dbRegisteredSlaveIds.Contains(slaveId);
        }
    }

    /// <summary>
    /// Executes the complete Dual-Guard Pipeline over raw responses collected from the
    /// RS-485 bus scan (1..247) and populates the 3 result buckets in <see cref="ScanResultSummary"/>.
    /// Thread-safe and purely in-memory (microsecond execution).
    /// </summary>
    public ScanResultSummary ExecuteDualGuardPipeline(
        IEnumerable<RawScanResponse> incomingBusResponses,
        bool includeAllRegisteredInBucket1 = true)
    {
        ArgumentNullException.ThrowIfNull(incomingBusResponses);

        var summary = new ScanResultSummary();
        var rawBucket = new List<RawScanResponse>(capacity: 32);
        var alreadyInSystemSeen = new HashSet<byte>();

        lock (_syncLock)
        {
            // If pre-registered DB slaves exist on this gateway, ensure they are represented
            // in Bucket 1 (AlreadyInSystemDevices) so the operator always sees which IDs are taken.
            if (includeAllRegisteredInBucket1)
            {
                foreach (var dbSlaveId in _dbRegisteredSlaveIds.OrderBy(x => x))
                {
                    AddToBucket1AlreadyInSystem(summary, alreadyInSystemSeen, dbSlaveId, respondedOnBus: false);
                }
            }

            // =========================================================================
            // STAGE 1 — CHECKPOINT 1 (GUARD 1: DB / System Existence Check)
            // =========================================================================
            foreach (var response in incomingBusResponses)
            {
                if (response is null || !response.IsSuccess || response.SlaveId is < 1 or > 247)
                    continue;

                if (_dbRegisteredSlaveIds.Contains(response.SlaveId))
                {
                    // GUARD 1 TRIGGERED: Slave ID already exists in DB/System.
                    // Place in Bucket 1 (AlreadyInSystemDevices) and DO NOT push to RawBucket!
                    AddToBucket1AlreadyInSystem(summary, alreadyInSystemSeen, response.SlaveId, respondedOnBus: true);
                    continue;
                }

                // Slave ID is NOT in DB -> push to temporary RawBucket for Guard 2 evaluation.
                rawBucket.Add(response);
            }
        }

        // =========================================================================
        // STAGE 2 — CHECKPOINT 2 (GUARD 2: Current Scan Duplicate Guard BEFORE Found Box)
        // =========================================================================
        var groupedBySlaveId = rawBucket
            .GroupBy(r => r.SlaveId)
            .OrderBy(g => g.Key);

        foreach (var slaveGroup in groupedBySlaveId)
        {
            byte slaveId = slaveGroup.Key;
            int responseCount = slaveGroup.Count();

            if (responseCount > 1)
            {
                // GUARD 2 TRIGGERED: Multiple sensors/frames responded on the same Slave ID
                // during the current scan session. Block ALL duplicates and move Slave ID
                // to Bucket 2 (BlockedScanDuplicates). Never allow into Found Box!
                var collidingNames = slaveGroup
                    .Select(r => r.ModelName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct()
                    .ToList();

                if (collidingNames.Count == 0)
                {
                    collidingNames = slaveGroup
                        .Select(r => CheckpostRouter.GetDescriptor(r.HintedProfile)?.ModelName ?? r.HintedProfile.ToString())
                        .Where(n => !string.IsNullOrWhiteSpace(n) && n != "Unknown")
                        .Distinct()
                        .ToList();
                }

                var displayCollidingNames = collidingNames.ToList();
                var msg = $"Your {responseCount} sensors are using the same Slave ID {slaveId}. Two meters cannot share one ID - give each meter a different Slave ID (change all but one), then scan again.";

                summary.BlockedScanDuplicates.Add(slaveId);
                summary.BlockedDuplicateDetails.Add(new BlockedScanDuplicate(
                    SlaveId: slaveId,
                    SensorCount: responseCount,
                    Message: msg,
                    CollidingSensorNames: displayCollidingNames));
                continue;
            }

            // =========================================================================
            // STAGE 3 — PURE UNIQUE DEVICE (Count() == 1) -> CHECKPOST ROUTER & FOUND BOX
            // =========================================================================
            RawScanResponse uniqueResponse = slaveGroup.First();
            ModbusDevicePacket? verifiedPacket = CheckpostRouter.InspectAndTag(uniqueResponse);

            if (verifiedPacket is null)
            {
                // Signature matching failed -> drop frame as RS-485 bus noise / corrupted data.
                summary.DroppedNoiseFrames++;
                continue;
            }

            // STEP 4: Dispatch verified ModbusDevicePacket to its designated Driver "Ghar" calculation.
            DriverCalculationResult calculation = SensorDriverDispatcher.Dispatch(verifiedPacket);
            if (!calculation.IsValid)
            {
                summary.DroppedNoiseFrames++;
                continue;
            }

            // FOUND BOX TRANSFER (Bucket 3):
            summary.UniqueFoundDevices.Add(verifiedPacket);
            summary.DispatchedCalculations[verifiedPacket.SlaveId] = calculation;
        }

        return summary;
    }

    private void AddToBucket1AlreadyInSystem(
        ScanResultSummary summary,
        HashSet<byte> seenSet,
        byte slaveId,
        bool respondedOnBus)
    {
        _dbRegisteredMetadata.TryGetValue(slaveId, out var meta);
        string sensorLabel = !string.IsNullOrWhiteSpace(meta.SensorName)
            ? $" ('{meta.SensorName}')"
            : string.Empty;

        string message = respondedOnBus
            ? $"Slave ID {slaveId}{sensorLabel} is already added in system and responded on the bus. Change hardware ID if connecting a new sensor."
            : $"Slave ID {slaveId}{sensorLabel} is already added in system (auto-skipped). Change hardware ID if connecting a new sensor.";

        if (seenSet.Add(slaveId))
        {
            summary.AlreadyInSystemDevices.Add(slaveId);
            summary.AlreadyInSystemDetails.Add(new AlreadyInSystemDevice(
                SlaveId: slaveId,
                ExistingSensorName: meta.SensorName,
                ExistingDriverKey: meta.DriverKey,
                Message: message));
        }
        else if (respondedOnBus)
        {
            // Upgrade message if it also actively responded during the scan.
            var idx = summary.AlreadyInSystemDetails.FindIndex(x => x.SlaveId == slaveId);
            if (idx >= 0)
            {
                summary.AlreadyInSystemDetails[idx] = new AlreadyInSystemDevice(
                    SlaveId: slaveId,
                    ExistingSensorName: meta.SensorName,
                    ExistingDriverKey: meta.DriverKey,
                    Message: message);
            }
        }
    }
}
