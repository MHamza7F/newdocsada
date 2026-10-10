using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using scada_demo_test.Application.DTOs;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Drivers;
using ScadaEngine.Core.Drivers;
using ScadaEngine.Core.Models;
using ScadaEngine.Core.Services;

namespace scada_demo_test.Infrastructure.Modbus;

// =====================================================================
// Smart & Fast RS-485 Bus Scanner + Dual-Guard & Checkpost Pipeline
// ---------------------------------------------------------------------
// Sweeps Modbus slave addresses over the USR-W610 gateway and feeds all
// raw responses through the 3-Bucket Dual-Guard Architecture:
//   Bucket 1 (Guard 1 - DB Existence Guard):
//     AlreadyInSystemDevices / AlreadyInSystemSlaves ("Already added in system. Change hardware ID")
//   Bucket 2 (Guard 2 - Current Scan Duplicate Guard):
//     BlockedScanDuplicates / DuplicateIdConflicts ("Bus conflict: Same ID responded multiple times")
//   Bucket 3 (CheckpostRouter -> SensorDriverDispatcher "Ghar" -> Found Box):
//     UniqueFoundDevices / Found (Verified ModbusDevicePacket envelopes only)
// =====================================================================
public sealed class ModbusScanner : ISmartScanService
{
    private const byte MinAddress = 1;
    private const byte MaxAddress = 247;

    // Quick = slaves 1..20 (where plant meters actually live). Full = the whole
    // bus; the tier boundary keeps the two tiers disjoint so EVERY address is
    // probed exactly once and "however many are connected, all are found" holds.
    private const int QuickScanEndAddress = 20;
    private const int ScanPassesQuick = 1;
    private const int ScanPassesFull = 1;
    private const int AddressRefineAttempts = 1;

    // One bad address must never abort the sweep; only a sustained run of
    // socket-level failures (a genuinely dead bridge) ends it.
    private const int UnreachableFailureStreak = 12;

    // A family qualifies as "colliding" only with this many strong votes, and a
    // duplicate is declared only when >= 2 distinct families qualify.
    private const int ConflictVoteThreshold = 2;

    // Foreign-window collision hunt (deep mode only): a foreign block that is
    // answered a FEW PERCENT of the time means another device is racing this
    // address for the same request. 0% (nobody serves it) and ~100% (the
    // address's own meter or a zero-filler owns it) are both NOT witnesses.
    private const int ForeignCollisionProbeBudget = 300;
    private const int ForeignCollisionReadsPerConnection = 40;
    private const int ForeignCollisionWindowJumpMs = 20;
    private const int ForeignCollisionReadTimeoutMs = 120;
    private const int ForeignCollisionEarlyWitnessSamples = 25;
    private const int ForeignCollisionGiveUpSamples = 40;

    // A driver about to be thrown away by the degeneracy guard gets its proof
    // block re-read this many times before rejection - a transport hiccup must
    // not lose a genuine match.
    private const int DegeneracyProofAttempts = 10;
    private const int InterWindowDelayMs = 60;
    private const int MaxProbeTimeoutMs = 1000;

    private readonly IReadOnlyList<ISensorDriver> _drivers = SensorDriverCatalog.Drivers;
    private readonly ILogger<ModbusScanner>? _logger;

    public ModbusScanner(ILogger<ModbusScanner>? logger = null) => _logger = logger;

    // =================================================================
    // Public entry point
    // =================================================================
    public async Task<SmartScanResultDto> ScanAsync(SmartScanRequest request, CancellationToken ct = default)
    {
        var probeTimeout = request.ProbeTimeoutMs <= 0 ? 200 : Math.Clamp(request.ProbeTimeoutMs, 10, MaxProbeTimeoutMs);
        var connectMs = Math.Clamp(request.ConnectTimeoutMs, 100, 10000);

        // STEP 3 Pre-Scan Initialization: Load existing DB-registered Slave IDs into O(1) HashSet<byte>
        var regMeta = request.RegisteredSlaveNames?.ToDictionary(
            kv => kv.Key,
            kv => ((string?)kv.Value, (string?)null));
        var dualGuardService = new ModbusScannerService(request.SkipSlaveAddresses, regMeta);

        var reqStart = Math.Clamp(request.StartAddress, MinAddress, MaxAddress);
        var reqEnd = Math.Clamp(
            request.EndAddress < request.StartAddress ? request.StartAddress : request.EndAddress,
            MinAddress, MaxAddress);
        var tiers = new[] { (Start: reqStart, End: reqEnd, Passes: Math.Clamp(request.Passes, 1, 3)) };

        var states = new Dictionary<int, AddressState>();
        var skipped = new List<int>();
        var incomingRawResponses = new List<RawScanResponse>();
        int totalProbed = 0;
        bool connectivityOk = true;
        string? errorCode = null;
        int failureStreak = 0;
        bool abort = false;

        using var scanLease = await ModbusScanCoordinator.AcquireScanAsync(
            request.DeviceId, request.GatewayIp, request.GatewayPort, ct);
        var master = new ModbusTcpMaster();
        using var sessionHolder = new SessionHolder(master, request.GatewayIp, request.GatewayPort, connectMs);
        try
        {
            foreach (var (tierStart, tierEnd, passes) in tiers)
            {
                for (int pass = 0; pass < passes && !abort; pass++)
                {
                    for (int slave = tierStart; slave <= tierEnd && !abort; slave++)
                    {
                        // =========================================================
                        // CHECKPOINT 1 (GUARD 1: DB / System Existence Check)
                        // =========================================================
                        if (dualGuardService.IsRegisteredInDatabase((byte)slave))
                        {
                            if (!skipped.Contains(slave)) skipped.Add(slave);
                            continue;
                        }

                        totalProbed++;
                        SlaveProbe probe;
                        try
                        {
                            probe = await ProbeSlaveAsync(sessionHolder, (byte)slave, probeTimeout, ct);
                        }
                        catch (Exception ex) when (ex is TimeoutException or IOException or SocketException or ObjectDisposedException)
                        {
                            sessionHolder.Invalidate();
                            continue;
                        }

                        if (probe.ConnectionUnsafe)
                        {
                            sessionHolder.Invalidate();
                            if (++failureStreak >= UnreachableFailureStreak)
                            {
                                connectivityOk = false;
                                errorCode = "GATEWAY_UNREACHABLE";
                                abort = true;
                                states.Clear();
                            }
                            continue;
                        }

                        failureStreak = 0;
                        MergeOutcome(states, slave, probe);
                        if (probe.GotData)
                        {
                            connectivityOk = true;
                        }
                    }
                }
            }

            // Targeted refine of ONLY responding addresses for accuracy without stressing the USR-W610 bridge.
            if (!abort && AddressRefineAttempts > 0)
            {
                var responders = states.Where(kv => kv.Value.Responded).Select(kv => kv.Key).ToList();
                for (int attempt = 0; attempt < AddressRefineAttempts && !abort; attempt++)
                {
                    foreach (var slave in responders)
                    {
                        if (!states.TryGetValue(slave, out var st) || !st.Responded) continue;
                        try
                        {
                            var probe = await ProbeSlaveAsync(sessionHolder, (byte)slave, probeTimeout, ct);
                            if (probe.ConnectionUnsafe) continue;
                            MergeOutcome(states, slave, probe);
                        }
                        catch (Exception ex) when (ex is TimeoutException or IOException or SocketException or ObjectDisposedException)
                        {
                            // best-effort refine pass
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            connectivityOk = false;
            errorCode = "SCAN_CANCELLED";
            states.Clear();
        }


        // =====================================================================
        // Feed non-DB responding addresses into the Dual-Guard RawBucket
        // & CheckpostRouter -> SensorDriverDispatcher ("Ghar") -> Found Box
        // =====================================================================
        var unknownItems = new List<ScannedMeterDto>();
        int responding = 0;
        int unknown = 0;

        incomingRawResponses.AddRange(sessionHolder.RepeatedResponses);
        foreach (var (slave, st) in states.OrderBy(kv => kv.Key))
        {
            if (sessionHolder.UnsafeSlaves.Contains((byte)slave))
            {
                unknown++;
                _logger?.LogWarning("Slave {SlaveId} excluded after invalid response framing; inspect wiring and duplicate addresses.", slave);
                continue;
            }
            if (!st.Responded) continue;
            responding++;
            if (st.IsPhantom)
            {
                // The gateway's internal echo/zero-filler slave: answers out-of-map
                // registers with zeros, so EVERY driver family "matches". Report it as
                // an unknown responder - never as meters, never as a duplicate.
                unknown++;
                _logger?.LogWarning(
                    "Slave {SlaveId} is the gateway's internal echo/zero-filler slave (answers registers outside every meter's map); reported as unknown.",
                    slave);
                continue;
            }
            if (st.Candidates.Count == 0 && st.IdentityAckFamilies.Count > 0)
            {
                // Identity-ack WITHOUT any fingerprint candidate: the meter missed every
                // ident-window probe (slow bridge + zero values at rest) but positively
                // acknowledged its identity register. Read the driver's own windows once
                // more so the operator gets the meter with real (possibly zero) values;
                // if the retries also miss, still surface the meter as identified - its
                // identity register is physical evidence, not noise.
                foreach (var famKey in st.IdentityAckFamilies)
                {
                    var drv = DriverOf(famKey);
                    if (drv is null) continue;
                    var ident = drv.ReadWindows[0];
                    var (k2, pl2) = await ReadBlockAsync(sessionHolder, (byte)slave, ident, Math.Min(1000, Math.Max(600, probeTimeout * 3)), ct);
                    var evidence = new List<ScanWindowResponse>();
                    if (k2 == ReadKind.Data)
                    {
                        evidence.Add(new ScanWindowResponse(ident, pl2));
                        for (int wi = 1; wi < drv.ReadWindows.Count; wi++)
                        {
                            await Task.Delay(InterWindowDelayMs, ct);
                            var (kw2, pw2) = await ReadBlockAsync(sessionHolder, (byte)slave, drv.ReadWindows[wi], 600, ct);
                            if (kw2 == ReadKind.Data) evidence.Add(new ScanWindowResponse(drv.ReadWindows[wi], pw2));
                        }
                    }
                    else
                    {
                        // Retry also missed: synthesize a zero-payload evidence set matching
                        // the driver's windows so fingerprint validation sees a compliant
                        // meter at rest. The identity register is the deciding evidence.
                        foreach (var w in drv.ReadWindows)
                            evidence.Add(new ScanWindowResponse(w, new byte[w.RegisterQuantity * 2]));
                    }

                    var (kd, pd) = await ReadBlockAsync(sessionHolder, (byte)slave, drv.DisproofWindow!, 600, ct);
                    if (kd == ReadKind.Data && drv.ValidateDisproof(pd))
                    {
                        evidence.Add(new ScanWindowResponse(drv.DisproofWindow!, pd));
                    var combined = evidence.SelectMany(e => e.Payload).ToArray();
                    st.Candidates.Add(new CandidateEntry(
                        new CandidateRank(ident.RegisterQuantity, 0, evidence.Sum(e => e.Payload.Any(b => b != 0) ? 1 : 0), 1, IndexOfDriver(famKey)),
                        new ScannedCandidateDto(famKey, drv.DisplayName, ident.StartRegister, ident.RegisterQuantity,
                            null, null, true, true),
                        combined, evidence, IdentityProven: true));
                    }
                }
            }

            var validCandidates = st.Candidates
                .Where(c => DriverOf(c.Dto.DriverKey) is not null)
                .OrderBy(c => c.Rank)
                .ToList();

            // Degenerate zero-match guard: a meter that serves unprogrammed 0x0000
            // registers lets SEVERAL driver families validate at once (0.0 %RH / 0.0 °C
            // fits the Aosong map, 0.0 m³ fits the flow maps - verified live: the Kaifeng
            // EM at slave 9 returned zeros at registers 0-1 and a phantom "Aosong" shadow
            // beat a REAL meter into a fake duplicate conflict). Evidence carrying at
            // least one non-zero byte is a real measurement; an all-zero candidate only
            // wins when it is the sole match. A candidate whose identity register
            // (DisproofWindow serial) proved NON-ZERO is a physically programmed meter
            // that simply reads zero at rest (e.g. a Selec energy meter at 0 kW / 0 kWh)
            // - it is never a shadow, so it always keeps its seat at the conflict table
            // and a second real meter sharing one Slave ID can never hide behind a zero
            // reading.
            //
            // Furthermore, an all-zero candidate on a DIFFERENT function code cannot be
            // a shadow of a non-zero candidate on another FC (e.g. Kaifeng EM FC03 vs
            // Selec energy FC04 at rest). They may be two distinct physical meters.
            var decisive = validCandidates;
            if (validCandidates.Count > 1)
            {
                var strong = validCandidates
                    .Where(c => c.IdentityProven || c.Evidence.Any(w => w.Payload.Any(b => b != 0)))
                    .ToList();
                if (strong.Count >= 1)
                {
                    // Check if any weak candidates share the SAME function code as a strong candidate.
                    // Those are likely shadows and can be dropped.
                    // Candidates on DIFFERENT function codes are independent physical meters.
                    var fcOfStrong = new HashSet<byte>(
                        strong.Select(c => DriverOf(c.Dto.DriverKey)).OfType<ISensorDriver>().Select(d => d.FunctionCode));
                    var weakSameFc = validCandidates
                        .Where(c => !strong.Contains(c) && DriverOf(c.Dto.DriverKey) is { } d && fcOfStrong.Contains(d.FunctionCode))
                        .ToList();
                    var weakDiffFc = validCandidates
                        .Where(c => !strong.Contains(c) && DriverOf(c.Dto.DriverKey) is { } d && !fcOfStrong.Contains(d.FunctionCode))
                        .ToList();

                    // NEW FIX: If we have exactly 1 strong candidate with IDENTITY PROVEN
                    // (non-zero serial/disproof), and weak candidates are just cross-FC bleed
                    // (same meter responding on wrong FC), treat as UNIQUE - not conflict.
                    // This happens when energy meters answer on both FC3 and FC4.
                    bool hasIdentityProven = strong.Any(c => c.IdentityProven);
                    bool allWeakAreCrossFcBleed = weakDiffFc.Count > 0 &&
                        !weakDiffFc.Any(w => w.Evidence.Any(e => e.Payload.Any(b => b != 0)));

                    if (hasIdentityProven && allWeakAreCrossFcBleed)
                    {
                        // Only 1 real meter - weak matches are just cross-FC bleed
                        decisive = strong;
                        _logger?.LogInformation(
                            "scan slave {Slave}: 1 proven candidate with identity, weak matches are cross-FC bleed - treating as UNIQUE",
                            slave);
                    }
                    else if (weakSameFc.Count > 0 && weakDiffFc.Count == 0)
                    {
                        // All weak candidates are same-FC shadows - drop them.
                        decisive = strong;
                    }
                    else
                    {
                        // Keep everything - at least one weak candidate is on a different FC with real data
                        decisive = validCandidates;
                    }
                }
            }

            // Address-level SECOND-RESPONDER WITNESSES: families that served real
            // (non-zero) data on their own windows here but never completed a
            // fingerprint. A proven candidate can never also be a witness (the witness
            // is only recorded on the non-proven path), so the two sets are disjoint.
            var witnesses = st.WitnessFamilies
                .Where(f => DriverOf(f) is not null)
                .OrderBy(IndexOfDriver)
                .ToList();

            int responders = decisive.Count + witnesses.Count;
            // If a cross-FC probe returned non-zero Data, another physical meter exists on this
            // Slave ID even though its own ident read failed (e.g. Selec FC04@42 WindowInvalid).
            // Treat it as a second responder so the real meter doesn't walk into the Found Box alone.
            //
            // FALSE ALARM DETECTION: if the cross-FC signal came from a driver whose OWN ident
            // returned ALL ZEROS (e.g. AOSONG FC3@0 = zeros), then the cross-FC "data" on the
            // other function code is almost certainly the PROVEN candidate's FC3 response
            // bleeding into the probe — NOT a second meter. For example:
            //   - AOSONG (FC3@0) reads zeros → ident fails fingerprint
            //   - AOSONG cross-FC probe (FC4@0) returns non-zero = KAIFENG's FC3 data leaked
            //   - KAIFENG (FC3@90) reads non-zero → fingerprint succeeds
            // This is ONE meter (KAIFENG), not two. Do NOT increment responders.
            //
            // GENUINE case: the cross-FC source driver has non-zero ident (real meter on that FC),
            // or the cross-FC source is a driver with failed ident (e.g. Selec FC04@42 WindowInvalid
            // but FC03@42 served data = a real Selec meter sharing the address with another device).
            string? crossFcSource = st.CrossFcSourceDriver;
            bool isCrossFcFalseAlarm = crossFcSource != null && st.ZeroIdentCrossFcDrivers.Contains(crossFcSource);
            if (st.CrossFcDataDetected && !isCrossFcFalseAlarm)
            {
                responders++;
                _logger?.LogInformation(
                    "scan slave {Slave}: cross-FC data detected - incrementing responders to {Responders}",
                    slave, responders);
            }
            else if (st.CrossFcDataDetected && isCrossFcFalseAlarm)
            {
                _logger?.LogInformation(
                    "scan slave {Slave}: cross-FC data detected from {Driver} (zero-ident bleed) - NOT incrementing responders",
                    slave, crossFcSource);
            }
            if (responders == 0) { unknown++; continue; }

            if (responders >= 2)
            {
                // Check if this is a FALSE POSITIVE: single meter responding on multiple FCs
                // If we have only 1 proven candidate and the "extra" responders are just
                // cross-FC bleed (same meter answering on FC3 and FC4), don't treat as conflict.
                bool isFalsePositive = decisive.Count == 1 && witnesses.Count == 0 && st.CrossFcDataDetected && isCrossFcFalseAlarm;

                if (!isFalsePositive)
                {
                    // GENUINE DUPLICATE CONFLICT: >= 2 distinct physical meter families responded
                    // Independently on the SAME slave address.
                    // Emit one frame per responder so Guard 2 blocks the WHOLE slave from the
                    // Found Box and the operator gets the duplicate-ID alert.
                    var conflictDrivers = new List<string>(decisive.Select(c => c.Dto.DriverKey));
                    conflictDrivers.AddRange(witnesses);
                    if (st.CrossFcDataDetected && !decisive.Any())
                        conflictDrivers.Add("(cross-FC second responder)");
                    _logger?.LogWarning(
                        "scan slave {Slave}: conflicting discovery profiles ({Count}): {Meters}",
                        slave, responders, string.Join(", ", conflictDrivers));

                    foreach (var cand in decisive)
                    {
                        var drv = DriverOf(cand.Dto.DriverKey)!;
                        incomingRawResponses.Add(new RawScanResponse
                        {
                            SlaveId = (byte)slave,
                            Payload = cand.RawPayload,
                            Evidence = cand.Evidence,
                            IsSuccess = true,
                            HintedProfile = CheckpostRouter.ResolveProfileByDriverKey(drv.DriverKey),
                            FunctionCode = drv.FunctionCode,
                            StartRegister = cand.Dto.StartRegister,
                            RegisterQuantity = cand.Dto.RegisterQuantity,
                            ProofServed = cand.Dto.ProofServed,
                            ModelName = drv.DisplayName
                        });
                    }

                    // When cross-FC data is detected but no witness family was recorded,
                    // emit a synthetic frame so Guard 2 sees >= 2 frames for this slave
                    // and blocks it. Without this, a single decisive candidate + cross-FC
                    // data would produce only 1 frame and slip through as "unique".
                    // EXCEPTION: skip when cross-FC signal is a false alarm from zero-ident bleed.
                    // NEW: Also skip when there's exactly 1 proven candidate and the cross-FC
                    //      source is a DIFFERENT driver (means same meter responding on multiple FCs).
                    bool skipSyntheticFrame = isCrossFcFalseAlarm ||
                        (decisive.Count == 1 && crossFcSource != null &&
                         !string.Equals(decisive[0].Dto.DriverKey, crossFcSource, StringComparison.OrdinalIgnoreCase));
                    if (st.CrossFcDataDetected && witnesses.Count == 0 && decisive.Count > 0 && !skipSyntheticFrame)
                    {
                        // Use the first decisive candidate's FC to create a distinct frame
                        var drv = DriverOf(decisive[0].Dto.DriverKey)!;
                        var otherFc = (byte)(drv.FunctionCode == 0x03 ? 0x04 : 0x03);
                        incomingRawResponses.Add(new RawScanResponse
                        {
                            SlaveId = (byte)slave,
                            Payload = Array.Empty<byte>(),
                            Evidence = Array.Empty<ScanWindowResponse>(),
                            IsSuccess = true,
                            HintedProfile = CheckpostRouter.ResolveProfileByDriverKey(drv.DriverKey),
                            FunctionCode = otherFc,
                            StartRegister = drv.StartRegister,
                            RegisterQuantity = drv.RegisterQuantity,
                            ProofServed = false,
                            ModelName = drv.DisplayName
                        });
                    }

                    foreach (var famKey in witnesses)
                    {
                        var drv = DriverOf(famKey)!;
                        incomingRawResponses.Add(new RawScanResponse
                        {
                            SlaveId = (byte)slave,
                            Payload = Array.Empty<byte>(),
                            Evidence = Array.Empty<ScanWindowResponse>(),
                            IsSuccess = true,
                            HintedProfile = CheckpostRouter.ResolveProfileByDriverKey(drv.DriverKey),
                            FunctionCode = drv.FunctionCode,
                            StartRegister = drv.StartRegister,
                            RegisterQuantity = drv.RegisterQuantity,
                            ProofServed = false,
                            ModelName = drv.DisplayName
                        });
                    }
                }
            }
            else if (decisive.Count == 1)
            {
                // UNIQUE PHYSICAL METER: exactly one driver verified for this slave address
                var winner = decisive[0];
                var drv = DriverOf(winner.Dto.DriverKey)!;
                incomingRawResponses.Add(new RawScanResponse
                {
                    SlaveId = (byte)slave,
                    Payload = winner.RawPayload,
                    Evidence = winner.Evidence,
                    IsSuccess = true,
                    HintedProfile = CheckpostRouter.ResolveProfileByDriverKey(drv.DriverKey),
                    FunctionCode = drv.FunctionCode,
                    StartRegister = winner.Dto.StartRegister,
                    RegisterQuantity = winner.Dto.RegisterQuantity,
                    ProofServed = winner.Dto.ProofServed,
                    ModelName = drv.DisplayName
                });
            }
            else
            {
                // A single partial responder with no verified neighbour: report it as an
                // unknown responder - it never enters the Found Box on partial evidence.
                unknown++;
                _logger?.LogWarning(
                    "scan slave {Slave}: {Meters} answered with real data but no family completed its fingerprint; reported as unknown.",
                    slave, string.Join(", ", witnesses));
            }
        }

        // =====================================================================
        // Execute Dual-Guard Pipeline + CheckpostRouter + DriverDispatcher
        // =====================================================================
        ScanResultSummary summary = dualGuardService.ExecuteDualGuardPipeline(
            incomingRawResponses,
            includeAllRegisteredInBucket1: true);

        foreach (var conflict in summary.BlockedDuplicateDetails)
            _logger?.LogWarning("Slave {SlaveId} blocked: {Message}", conflict.SlaveId, conflict.Message);

        var found = new List<ScannedMeterDto>(summary.UniqueFoundDevices.Count + unknownItems.Count);
        foreach (var packet in summary.UniqueFoundDevices)
        {
            var desc = CheckpostRouter.GetDescriptor(packet.ProfileType);
            summary.DispatchedCalculations.TryGetValue(packet.SlaveId, out var calc);

            // Look up the candidate's live primary/secondary (in case multi-window secondary was merged)
            double? livePri = calc?.PrimaryValue;
            double? liveSec = calc?.SecondaryValue;


            found.Add(new ScannedMeterDto(
                SlaveAddress: packet.SlaveId,
                DriverKey: packet.DriverKey,
                SimpleName: desc?.SimpleName ?? packet.DriverKey.ToLowerInvariant(),
                DisplayName: packet.ModelName,
                StartRegister: packet.StartRegister,
                RegisterQuantity: packet.RegisterQuantity,
                UnitPrimary: desc?.UnitPrimary ?? calc?.PrimaryUnit,
                UnitSecondary: desc?.UnitSecondary ?? calc?.SecondaryUnit,
                DefaultPollIntervalSeconds: desc?.DefaultPollIntervalSeconds ?? 5,
                SuggestedName: $"{packet.ModelName} (Slave {packet.SlaveId})",
                LivePrimary: livePri,
                LiveSecondary: liveSec,
                Status: "identified",
                IsAmbiguous: false,
                Candidates: null,
                ProfileType: packet.ProfileType.ToString(),
                RawHexPayload: packet.RawHex));
        }

        // Append any raw unknown responders so diagnostics remain complete
        found.AddRange(unknownItems);

        var conflicts = summary.BlockedDuplicateDetails
            .Select(b => new DuplicateSlaveIdConflictDto(
                b.SlaveId,
                b.SensorCount,
                null,
                b.Message,
                b.CollidingSensorNames))
            .ToList();

        var alreadyInSystemList = summary.AlreadyInSystemDetails
            .Select(a => new AlreadyInSystemSlaveDto(
                a.SlaveId,
                a.ExistingSensorName,
                a.Message))
            .ToList();

        return new SmartScanResultDto(
            request.DeviceId,
            request.DeviceName,
            request.GatewayIp,
            request.GatewayPort,
            probeTimeout,
            totalProbed,
            responding,
            unknown + summary.DroppedNoiseFrames,
            connectivityOk,
            errorCode,
            DateTime.UtcNow,
            found,
            summary.AlreadyInSystemDevices.Select(b => (int)b).ToList(),
            Array.Empty<int>(),
            conflicts,
            alreadyInSystemList);
    }

    // =================================================================
    // Persistent Session Holder for Modbus TCP Gateway Communication
    // =================================================================
    private sealed class SessionHolder : IDisposable
    {
        private readonly ModbusTcpMaster _master;
        private readonly string _ip;
        private readonly int _port;
        private readonly int _connectTimeoutMs;
        private ModbusTcpSession? _session;
        public HashSet<byte> UnsafeSlaves { get; } = new();
        public List<RawScanResponse> RepeatedResponses { get; } = new();

        public SessionHolder(ModbusTcpMaster master, string ip, int port, int connectTimeoutMs)
        {
            _master = master;
            _ip = ip;
            _port = port;
            _connectTimeoutMs = connectTimeoutMs;
        }

        public async Task<ModbusTcpSession> GetSessionAsync(CancellationToken ct)
        {
            if (_session != null) return _session;
            try
            {
                _session = await _master.OpenAsync(_ip, _port, _connectTimeoutMs, ct);
                return _session;
            }
            catch (Exception ex) when (ex is ModbusConnectException or SocketException or IOException)
            {
                await Task.Delay(100, ct);
                _session = await _master.OpenAsync(_ip, _port, _connectTimeoutMs, ct);
                return _session;
            }
        }

        public void Invalidate()
        {
            try { _session?.Dispose(); } catch { }
            _session = null;
        }

        public void Dispose()
        {
            Invalidate();
        }
    }

    // =================================================================
    // Per-address probing
    // =================================================================
    private async Task<SlaveProbe> ProbeSlaveAsync(
        SessionHolder sessionHolder,
        byte slave,
        int probeTimeout,
        CancellationToken ct)
    {
        var result = new SlaveProbe();
        bool phantomChecked = false;

        // Slow serial->WiFi bridge auto-retry: a real meter's reply over the USR-W610
        // can arrive AFTER the operator's probe timeout (measured live: the Aosong at
        // slave 3 answered within 1.5 s but missed the 200 ms probe three times, so the
        // scan showed 0 meters). On the FIRST read timeout of ANY window of this slave,
        // retry that read once at 3x and keep the escalated timeout for the rest of this
        // slave's reads. A retry that also fails resets the timeout so a genuinely silent
        // bus never gets slowed down. Every window shares this helper so a slow bridge
        // cannot silently truncate a real meter's evidence (an incomplete evidence set
        // used to fail the fingerprint and let a second meter on the SAME Slave ID walk
        // alone into the Found Box).
        int effectiveTimeout = probeTimeout;
        bool escalated = false;

        async Task<(ReadKind Kind, byte[] Payload)> ReadWindowAsync(SensorReadWindow window)
        {
            var (kind0, payload0) = await ReadBlockAsync(sessionHolder, slave, window, effectiveTimeout, ct);
            if (kind0 == ReadKind.Timeout && !escalated)
            {
                escalated = true;
                effectiveTimeout = Math.Min(probeTimeout * 3, MaxProbeTimeoutMs);
                (kind0, payload0) = await ReadBlockAsync(sessionHolder, slave, window, effectiveTimeout, ct);
                if (kind0 is not (ReadKind.Data or ReadKind.WindowInvalid))
                {
                    // Retry also timed out - slave is silent. Keep escalated=true so no more retries.
                    effectiveTimeout = probeTimeout;
                }
            }
            return (kind0, payload0);
        }

        // Full identification: every installed driver's own identification window.
        for (int d = 0; d < _drivers.Count; d++)
        {
            var driver = _drivers[d];
            var windows = driver.ReadWindows;
            var ident = windows[0];
            bool identityProven = false;

            _logger?.LogInformation("scan slave {Slave}: probing {Driver} FC{Fc}@{Start}", slave, driver.DriverKey, ident.FunctionCode, ident.StartRegister);
            var (kind, payload) = await ReadWindowAsync(ident);
            _logger?.LogInformation("scan slave {Slave}: {Driver} ident kind={Kind} payloadLen={Len} payload={Payload}", slave, driver.DriverKey, kind, payload?.Length ?? 0, payload != null ? string.Join("", payload.Select(b => b.ToString("X2"))) : "null");
            if (kind == ReadKind.Unsafe || kind == ReadKind.ConnectFailed)
            {
                result.ConnectionUnsafe = true;
                return result;
            }
            if (kind == ReadKind.WindowInvalid) result.GotData = true;
            if (kind != ReadKind.Data || payload is null)
            {
                // Second-chance identity: a meter that legitimately reads 0 at its ident
                // window (Selec kW/kWh with no load) can MISS the probe on a slow serial
                // -> WiFi bridge even at the escalated timeout - verified live. Its fixed
                // identity register (e.g. the RI-F200-C serial @0x2AC) is a POSITIVE
                // discriminator: probe it here; a non-zero value still acknowledges the
                // family for FOUND-RANK purposes (a gateway zero-filler can never pass,
                // its serial is 0x0000).
                if (driver.DisproofWindow is { } lateWin)
                {
                    _logger?.LogInformation("scan slave {Slave}: {Driver} checking disproof @{Start}", slave, driver.DriverKey, lateWin.StartRegister);
                    var (kl, pl) = await ReadWindowAsync(lateWin);
                    if (kl is ReadKind.Unsafe or ReadKind.ConnectFailed) { result.ConnectionUnsafe = true; return result; }
                    if (kl == ReadKind.Data && driver.ValidateDisproof(pl))
                    {
                        _logger?.LogInformation("scan slave {Slave}: {Driver} DISPROOF PROVEN serial={Serial}", slave, driver.DriverKey, pl != null ? string.Join("", pl.Select(b => b.ToString("X2"))) : "null");
                        result.IdentityAckFamilies.Add(driver.DriverKey);
                    }
                    else
                    {
                        _logger?.LogInformation("scan slave {Slave}: {Driver} disproof kind={Kind} validated={Validated} payload={Payload}", slave, driver.DriverKey, kl, kl == ReadKind.Data && driver.ValidateDisproof(pl), pl != null ? string.Join("", pl.Select(b => b.ToString("X2"))) : "null");
                    }
                }
                continue;
            }

            result.GotData = true;
            // Disproof/identity guard (PER DRIVER - replaces the old address-level
            // out-of-map probe whose shared flag let the FIRST data-serving family set
            // it and every later family skip the check entirely): when the driver
            // declares a DisproofWindow (e.g. the Selec serial @0x2AC), a REAL meter
            // serves it with at least one non-zero byte while a gateway echo/zero-filler
            // returns zeros for everything. A validated disproof is a POSITIVE identity:
            // this family owns the address and negative guards (cross-FC, phantom) are
            // skipped for it, because some real meters (verified live: the RI-F200-C) are
            // non-compliant and answer both function codes + out-of-map registers with
            // zeros. A FAILED disproof without any other evidence still triggers the
            // classic out-of-map phantom check so the USR-W610 filler stays unknown.
            if (driver.DisproofWindow is { } disproofWin)
            {
                var (kd, pd) = await ReadWindowAsync(disproofWin);
                if (kd is ReadKind.Unsafe or ReadKind.ConnectFailed) { result.ConnectionUnsafe = true; return result; }
                if (kd == ReadKind.Data && driver.ValidateDisproof(pd))
                {
                    identityProven = true;
                }
                else if (!phantomChecked)
                {
                    phantomChecked = true;
                    if (await ServesOutOfMapRegistersAsync(sessionHolder, slave, effectiveTimeout, ct))
                    {
                        result.IsPhantom = true;
                        return result;
                    }
                }
            }

            // Cross-function-code guard (PER DRIVER - a shared flag would let the first
            // data-serving family skip the check for every later family): a real meter
            // NEVER serves the OTHER function code at its own ident register - it
            // answers with a Modbus exception. A gateway echo/filler (or a foreign meter
            // sharing the address) serves BOTH FCs, e.g. zeros at regs 0-1 decode as
            // "100 %RH / 0 C" which fits the Aosong map and created ghost candidates.
            // Probing THIS driver's own window on the foreign FC: data means this
            // address cannot be this driver's family.
            {
                if (identityProven)
                {
                    _logger?.LogInformation(
                        "scan slave {Slave}: {Driver} identity PROVEN via disproof window - cross-FC guard skipped",
                        slave, driver.DriverKey);
                    goto evidenceCollection;
                }
                var otherFc = (byte)(ident.FunctionCode == 0x03 ? 0x04 : 0x03);
                var (kk, kPayload) = await ReadWindowAsync(new SensorReadWindow(otherFc, ident.StartRegister, ident.RegisterQuantity));
                if (kk is ReadKind.Unsafe or ReadKind.ConnectFailed) { result.ConnectionUnsafe = true; return result; }
                _logger?.LogInformation(
                    "scan slave {Slave}: cross-FC probe {Fc}@{Start} for {Driver} -> {Kind} payload={Payload}",
                    slave, otherFc, ident.StartRegister, driver.DriverKey, kk, kPayload != null ? string.Join("", kPayload.Select(b => b.ToString("X2"))) : "null");
                if (kk == ReadKind.Data && kPayload is not null && AnyNonZero(kPayload))
                {
                    result.CrossFcDataDetected = true;
                    result.CrossFcSourceDriver = driver.DriverKey;
                    // If this driver's OWN ident was all zeros, mark it as a false-alarm source.
                    // The cross-FC signal is the proven candidate's FC3 bleed, not a second meter.
                    if (kind == ReadKind.Data && !AnyNonZero(payload))
                        result.ZeroIdentCrossFcDrivers.Add(driver.DriverKey);
                    continue; // foreign FC served with non-zero data: this family cannot own the address
                }
            }

            evidenceCollection:
            var evidence = new List<ScanWindowResponse> { new(ident, payload) };
            // At least one OWN window carrying real (non-zero) data is physical evidence
            // that a device of this family answered at THIS Slave ID - even when the
            // evidence set never becomes complete enough to fingerprint.
            bool ownNonZero = AnyNonZero(payload);

            var combinedBytes = new List<byte>(payload.Length * windows.Count);
            combinedBytes.AddRange(payload);

            // Every own window is mandatory; incomplete evidence fails fingerprinting.
            for (int wi = 1; wi < windows.Count; wi++)
            {
                await Task.Delay(InterWindowDelayMs, ct);
                var (k2, pl2) = await ReadWindowAsync(windows[wi]);
                if (k2 is ReadKind.Unsafe or ReadKind.ConnectFailed) { result.ConnectionUnsafe = true; return result; }
                if (k2 != ReadKind.Data) continue;
                combinedBytes.AddRange(pl2);
                evidence.Add(new ScanWindowResponse(windows[wi], pl2));
                if (AnyNonZero(pl2)) ownNonZero = true;

            }

            // Corroboration window: served-with-data proves this family.
            bool proofServed = false;
            bool proofHasData = false;
            if (driver.CorroborationWindow is { } proofWin)
            {
                for (int attempt = 0; attempt < DegeneracyProofAttempts && !proofServed; attempt++)
                {
                    if (attempt > 0) await Task.Delay(InterWindowDelayMs, ct);
                    var (kp, plp) = await ReadBlockAsync(sessionHolder, slave, proofWin, effectiveTimeout, ct);
                    if (kp is ReadKind.Unsafe or ReadKind.ConnectFailed) { result.ConnectionUnsafe = true; return result; }
                    if (kp == ReadKind.Data && driver.ValidateCorroboration(plp))
                    {
                        evidence.Add(new ScanWindowResponse(proofWin, plp));
                        proofServed = true;
                        if (AnyNonZero(plp)) { proofHasData = true; ownNonZero = true; }
                    }
                }
            }

            if (driver.CorroborationWindow is null)
            {
                await Task.Delay(InterWindowDelayMs, ct);
                var (repeatKind, repeatPayload) = await ReadWindowAsync(ident);
                if (repeatKind == ReadKind.Data)
                {
                    evidence.Add(new ScanWindowResponse(ident, repeatPayload));
                    if (AnyNonZero(repeatPayload)) ownNonZero = true;
                }
            }

            bool isProven = CheckpostFingerprintEngine.Matches(driver, evidence);
            if (!isProven)
            {
                // PARTIAL RESPONDER WITNESS: this family served REAL data on its own
                // windows but a window never came back (slow bridge, or - verified live -
                // a second physical meter sharing this Slave ID that only races some of
                // the requests). The witness is carried to the address-level decision so
                // two responders on one ID can never let exactly one of them walk into
                // the Found Box. All-zero answers are NOT witnesses: a gateway
                // echo/zero-filler serves zeros on every family's window.
                // NOTE: A driver whose OWN ident returned zeros must not be counted as a witness just because
                // its cross-FC probe returned data — that data often comes from a
                // DIFFERENT meter family sharing the same Slave ID (e.g. AOSONG FC3@0
                // zeros + cross-FC FC4@0 non-zero means KAIFENG is answering on FC3,
                // not a second FC4 meter). The cross-FC flag is still used at the
                // address-level decision (responders++ below) for meters whose OWN
                // ident window failed but whose cross-FC probe proved a second meter
                // exists on a different function code.
                if (ownNonZero)
                {
                    result.WitnessFamilies.Add(driver.DriverKey);
                    _logger?.LogInformation(
                        "scan slave {Slave}: {Driver} served real data but did not complete its fingerprint - counted as a second-responder witness",
                        slave, driver.DriverKey);
                }
                continue;
            }

            result.AcceptedFamilies.Add(driver.DriverKey);
            if (isProven) result.ProvenFamilies.Add(driver.DriverKey);

            _logger?.LogInformation("scan slave {Slave}: {Driver} fingerprint={Proven} evidenceCount={Count}", slave, driver.DriverKey, isProven, evidence.Count);

            var rank = new CandidateRank(ident.RegisterQuantity, 0, 0, 0, d);

            var dto = new ScannedCandidateDto(
                driver.DriverKey,
                driver.DisplayName,
                ident.StartRegister,
                ident.RegisterQuantity,
                null,
                null,
                isProven,
                false);

            result.Candidates.Add(new CandidateEntry(rank, dto, combinedBytes.ToArray(), evidence.ToArray(), identityProven));

            bool strong = proofHasData;
            _logger?.LogDebug(
                "scan slave {Slave}: {Driver} width={Width} unproven={Unproven} live={Live} extra={Extra} proof={Proof} proofData={ProofData} strong={Strong}",
                slave, driver.DriverKey, rank.Width, rank.Unproven, rank.Live, rank.Extra, proofServed, proofHasData, strong);

            if (strong)
            {
                result.StrongVotes[driver.DriverKey] =
                    result.StrongVotes.GetValueOrDefault(driver.DriverKey) + 1;
            }
        }

        return result;
    }

    // =================================================================
    // Gateway echo/zero-filler detector. The USR-W610 exposes an internal
    // slave that answers EVERY address (even far beyond any meter's register
    // map, verified live: FC03@30000 and FC04@30000 both serve zeros). Real
    // meters answer out-of-map reads with a Modbus exception (0x02/0x0B) or
    // silence. Requiring BOTH probes to return data keeps a real meter from
    // ever being flagged as the filler.
    // =================================================================
    private static readonly SensorReadWindow[] PhantomProbeWindows =
    {
        new(0x03, 30000, 2),
        new(0x04, 30000, 2)
    };

    private async Task<bool> ServesOutOfMapRegistersAsync(
        SessionHolder sessionHolder, byte slave, int probeTimeout, CancellationToken ct)
    {
        foreach (var window in PhantomProbeWindows)
        {
            var (kind, _) = await ReadBlockAsync(sessionHolder, slave, window, probeTimeout, ct);
            if (kind is ReadKind.Unsafe or ReadKind.ConnectFailed) return false;
            if (kind != ReadKind.Data) return false;
        }
        return true;
    }

    private enum ReadKind { Absent, WindowInvalid, Unsafe, Data, ConnectFailed, Timeout }

    private static async Task<(ReadKind Kind, byte[] Payload)> ReadBlockAsync(
        SessionHolder sessionHolder,
        byte slave,
        SensorReadWindow window,
        int probeTimeout,
        CancellationToken ct)
    {
        ModbusTcpSession session;
        try
        {
            session = await sessionHolder.GetSessionAsync(ct);
        }
        catch (ModbusConnectException)
        {
            return (ReadKind.ConnectFailed, Array.Empty<byte>());
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            sessionHolder.Invalidate();
            return (ReadKind.Unsafe, Array.Empty<byte>());
        }

        try
        {
            var payload = window.FunctionCode == 0x04
                ? await session.ReadInputRegistersAsync(slave, window.StartRegister, window.RegisterQuantity, ct, probeTimeout)
                : await session.ReadHoldingRegistersAsync(slave, window.StartRegister, window.RegisterQuantity, ct, probeTimeout);
            return (ReadKind.Data, payload);
        }
        catch (ModbusRepeatedResponseException ex)
        {
            sessionHolder.RepeatedResponses.AddRange(ex.Responses);
            sessionHolder.UnsafeSlaves.Add(ex.SlaveId);
            sessionHolder.Invalidate();
            if (ex.SlaveId != slave)
                return await ReadBlockAsync(sessionHolder, slave, window, probeTimeout, ct);
            return (ReadKind.Unsafe, Array.Empty<byte>());
        }
        catch (ModbusException ex)
        {
            if (ex.IsProtocolError)
            {
                sessionHolder.UnsafeSlaves.Add(slave);
                sessionHolder.Invalidate();
                return (ReadKind.Unsafe, Array.Empty<byte>());
            }
            if (ex.ExceptionCode == 0x0B) return (ReadKind.Absent, Array.Empty<byte>());
            return (ReadKind.WindowInvalid, Array.Empty<byte>());
        }
        catch (TimeoutException)
        {
            sessionHolder.Invalidate();
            // Distinct from Absent: lets the identification loop retry this window at a
            // longer timeout once (slow serial->WiFi bridge) before giving up.
            return (ReadKind.Timeout, Array.Empty<byte>());
        }
        catch (ModbusConnectException)
        {
            return (ReadKind.ConnectFailed, Array.Empty<byte>());
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            sessionHolder.Invalidate();
            return (ReadKind.Unsafe, Array.Empty<byte>());
        }
    }

    // =================================================================
    // Foreign-window collision hunt (deep mode only)
    // =================================================================
    private async Task<bool> HasForeignWindowCollisionAsync(
        ModbusTcpMaster master,
        SmartScanRequest req,
        int connectMs,
        byte slave,
        string winnerKey,
        int probeTimeout,
        CancellationToken ct)
    {
        var foreign = _drivers
            .Where(d => !string.Equals(d.DriverKey, winnerKey, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.ReadWindows[0])
            .Where(w => w.RegisterQuantity == 2)
            .ToList();
        if (foreign.Count == 0) return false;

        int perWindow = Math.Max(1, ForeignCollisionProbeBudget / foreign.Count);
        int readTimeout = Math.Min(probeTimeout, ForeignCollisionReadTimeoutMs);

        foreach (var win in foreign)
        {
            ModbusTcpSession? session = null;
            int sampled = 0;
            int answered = 0;
            int served = 0;
            try
            {
                while (sampled < perWindow)
                {
                    if (session is null)
                    {
                        session = await master.OpenAsync(req.GatewayIp, req.GatewayPort, connectMs, ct);
                    }
                    else
                    {
                        await Task.Delay(ForeignCollisionWindowJumpMs, ct);
                    }

                    sampled++;
                    try
                    {
                        var payload = win.FunctionCode == 0x04
                            ? await session.ReadInputRegistersAsync(slave, win.StartRegister, win.RegisterQuantity, ct, readTimeout)
                            : await session.ReadHoldingRegistersAsync(slave, win.StartRegister, win.RegisterQuantity, ct, readTimeout);
                        if (payload.Length > 0)
                        {
                            served++;
                            if (AnyNonZero(payload)) answered++;
                        }
                    }
                    catch (ModbusException ex) when (ex.IsProtocolError)
                    {
                        return true;
                    }
                    catch (Exception)
                    {
                        // timeout / absent on this read
                    }

                    if (answered > 0 && answered * 4 <= sampled && sampled >= ForeignCollisionEarlyWitnessSamples)
                        return true;

                    if (served == sampled && sampled >= ForeignCollisionGiveUpSamples)
                        break;
                }

                if (answered > 0 && answered * 4 <= sampled) return true;
            }
            finally
            {
                session?.Dispose();
            }
        }

        return false;
    }

    // =================================================================
    // Helpers
    // =================================================================
    private ISensorDriver? DriverOf(string? key) =>
        key is null ? null : SensorDriverCatalog.GetByKey(key);

    private int IndexOfDriver(string key)
    {
        for (int i = 0; i < _drivers.Count; i++)
            if (string.Equals(_drivers[i].DriverKey, key, StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }

    private static bool HasContent(WindowTelemetry w) =>
        (w.PrimaryValue is { } p && p != 0) || (w.SecondaryValue is { } s && s != 0);

    private static bool AnyNonZero(byte[] payload)
    {
        foreach (var b in payload) if (b != 0) return true;
        return false;
    }

    private static bool Plausible(ISensorDriver driver, WindowTelemetry w)
    {
        if (w.ErrorCode != null) return false;

        if (driver is AosongAQ3485Driver)
        {
            // Reject Selec Energy Meter CT ratio and non-ambient register artifacts:
            // CT rating 1000/0 decodes as 100.0% RH and 0.0°C.
            if (w.PrimaryValue == 0.0 && (w.SecondaryValue == 100.0 || w.SecondaryValue == 0.0))
                return false;

            if (w.PrimaryValue <= 5.0 && w.SecondaryValue >= 90.0)
                return false;

            if (w.PrimaryValue == 0.0 && w.SecondaryValue <= 5.0)
                return false;

            // Environmental atmospheric operating range for AQ3485 sensor:
            // Temperature: -40°C to 80°C, Relative Humidity: 1% to 99% RH
            return w.PrimaryValue is >= -40.0 and <= 80.0
                   && w.SecondaryValue is >= 1.0 and <= 99.0;
        }

        if (w.PrimaryValue is { } pv && (!double.IsFinite(pv) || Math.Abs(pv) > 1e7)) return false;
        if (w.SecondaryValue is { } sv && (!double.IsFinite(sv) || Math.Abs(sv) > 1e12)) return false;
        return w.PrimaryValue is not null || w.SecondaryValue is not null;
    }

    private static void MergeOutcome(Dictionary<int, AddressState> states, int slave, SlaveProbe probe)
    {
        if (!states.TryGetValue(slave, out var st))
        {
            st = new AddressState();
            states[slave] = st;
        }

        if (probe.GotData) st.Responded = true;
        if (probe.IsPhantom) st.IsPhantom = true;
        foreach (var fam in probe.AcceptedFamilies) st.AcceptedFamilies.Add(fam);
        foreach (var fam in probe.ProvenFamilies) st.ProvenFamilies.Add(fam);
        foreach (var fam in probe.IdentityAckFamilies) st.IdentityAckFamilies.Add(fam);
        foreach (var fam in probe.WitnessFamilies) st.WitnessFamilies.Add(fam);
        st.CrossFcDataDetected = probe.CrossFcDataDetected;
        st.CrossFcSourceDriver = probe.CrossFcSourceDriver;
        foreach (var fam in probe.ZeroIdentCrossFcDrivers) st.ZeroIdentCrossFcDrivers.Add(fam);
        foreach (var (key, votes) in probe.StrongVotes)
            st.Votes[key] = st.Votes.GetValueOrDefault(key) + votes;

        foreach (var entry in probe.Candidates)
        {
            var idx = st.Candidates.FindIndex(c =>
                string.Equals(c.Dto.DriverKey, entry.Dto.DriverKey, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                if (entry.Rank.CompareTo(st.Candidates[idx].Rank) < 0) st.Candidates[idx] = entry;
            }
            else
            {
                st.Candidates.Add(entry);
            }
        }
    }

    private readonly record struct CandidateRank(int Width, int Unproven, int Live, int Extra, int Order)
        : IComparable<CandidateRank>
    {
        public int CompareTo(CandidateRank other)
        {
            // 1. Proven (0) comes before Unproven (1)
            var c = Unproven.CompareTo(other.Unproven); if (c != 0) return c;
            // 2. Narrower register verification is more specific (ascending: 2 beats 12)
            c = Width.CompareTo(other.Width); if (c != 0) return c;
            // 3. More live values confirmed (descending)
            c = other.Live.CompareTo(Live); if (c != 0) return c;
            // 4. More extra windows verified (descending)
            c = other.Extra.CompareTo(Extra); if (c != 0) return c;
            return Order.CompareTo(other.Order);
        }
    }

    private readonly record struct CandidateEntry(CandidateRank Rank, ScannedCandidateDto Dto, byte[] RawPayload,
        IReadOnlyList<ScanWindowResponse> Evidence, bool IdentityProven = false);

    private sealed class SlaveProbe
    {
        public bool ConnectionUnsafe;
        public bool GotData;
        public bool IsPhantom;
        public readonly List<CandidateEntry> Candidates = new();
        public readonly Dictionary<string, int> StrongVotes = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> AcceptedFamilies = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> ProvenFamilies = new(StringComparer.OrdinalIgnoreCase);

        // Families whose fixed identity register (DisproofWindow) validated WITHOUT a
        // successful ident-window read (slow-bridge miss on a meter that legitimately
        // reads 0, e.g. Selec kW/kWh at rest). Consumed by the merge step so the
        // family still lands in the Found Box with live values read on a retry.
        public readonly HashSet<string> IdentityAckFamilies = new(StringComparer.OrdinalIgnoreCase);

        // Families that served REAL (non-zero) data on their own windows but never
        // completed a fingerprint. Physically a second responder at this Slave ID -
        // carried to the address-level decision so it can never hide behind the meter
        // that did finish its fingerprint.
        public readonly HashSet<string> WitnessFamilies = new(StringComparer.OrdinalIgnoreCase);

        // True when ANY driver's cross-FC probe returned non-zero Data. This means
        // another physical meter on a different FC shares this Slave ID, even if that
        // meter's own ident read failed (e.g. Selec at rest returning WindowInvalid).
        public bool CrossFcDataDetected;

        // Tracks which driver's cross-FC probe SET the CrossFcDataDetected flag.
        // Used to detect false alarms: if a zero-ident driver's cross-FC probe detected
        // data, it's likely the proven candidate's FC3 response bleeding into the probe
        // (e.g. AOSONG FC3@0 zeros + cross-FC FC4@0 non-zero = KAIFENG's FC3 data).
        public string? CrossFcSourceDriver;

        // Drivers whose ident read returned ALL ZEROS but whose cross-FC probe detected
        // non-zero data. These are FALSE ALARM sources — the cross-FC signal is the
        // proven candidate's FC3 response bleeding, not a second meter.
        public readonly HashSet<string> ZeroIdentCrossFcDrivers = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AddressState
    {
        public bool Responded;
        public bool IsPhantom;
        public readonly List<CandidateEntry> Candidates = new();
        public readonly Dictionary<string, int> Votes = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> AcceptedFamilies = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> ProvenFamilies = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> IdentityAckFamilies = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> WitnessFamilies = new(StringComparer.OrdinalIgnoreCase);
        public bool CrossFcDataDetected;
        public string? CrossFcSourceDriver;
        public readonly HashSet<string> ZeroIdentCrossFcDrivers = new(StringComparer.OrdinalIgnoreCase);
    }
}
