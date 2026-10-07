using Microsoft.Extensions.Logging;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Domain.Interfaces;
using scada_demo_test.Domain.Rollup;

namespace scada_demo_test.Application.Services;

// The tiered storage/compression pipeline described by the plant team:
//
//   Raw (1 sec)  --older than 3h-->  Hourly  --older than 7d-->  Daily  --older than 60d-->  Monthly
//
// Each stage is idempotent (checks the target row doesn't already exist before
// inserting) and fault-isolated per (device, metric, bucket): one bad bucket is
// logged and skipped, never crashes the run, and its source data is left alone so
// it gets picked up again next cycle - zero data loss even across a restart or a
// mid-cycle crash.
public class RollupCompressionService : IRollupCompressionService
{
    private readonly IRollupRepository _repo;
    private readonly ILogger<RollupCompressionService> _logger;

    public RollupCompressionService(IRollupRepository repo, ILogger<RollupCompressionService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<int> CompressRawToHourlyAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - RollupPolicy.RawRetention;
        var raw = await _repo.GetRawOlderThanAsync(cutoff, ct);
        if (raw.Count == 0) return 0;

        var buckets = raw
            .GroupBy(r => new
            {
                r.DeviceId,
                r.Metric,
                Bucket = FloorToHour(r.Timestamp)
            })
            // Only compress hours that are fully closed (i.e. no more raw readings
            // for that hour can still arrive within the retention window).
            .Where(g => g.Key.Bucket.AddHours(1) <= cutoff);

        int compressed = 0;
        var idsSafeToDelete = new List<long>();

        foreach (var bucket in buckets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var alreadyDone = await _repo.HourlyExistsAsync(bucket.Key.DeviceId, bucket.Key.Metric, bucket.Key.Bucket, ct);
                if (alreadyDone)
                {
                    // Rollup already exists from a previous (possibly interrupted) run -
                    // the raw rows behind it are safe to purge, nothing new to insert.
                    idsSafeToDelete.AddRange(bucket.Select(r => r.Id));
                    continue;
                }

                var points = bucket.Select(r => (r.Timestamp, r.Value)).ToList();
                var isCumulative = MetricCatalog.IsCumulative(bucket.Key.Metric);
                var ordered = points.OrderBy(p => p.Timestamp).ToList();

                var rollup = new HourlyRollup
                {
                    Id = Guid.NewGuid(),
                    DeviceId = bucket.Key.DeviceId,
                    Metric = bucket.Key.Metric,
                    Unit = bucket.First().Unit,
                    PeriodStart = bucket.Key.Bucket,
                    PeriodEnd = bucket.Key.Bucket.AddHours(1),
                    SampleCount = bucket.Count(),
                    MinValue = bucket.Min(r => r.Value),
                    MaxValue = bucket.Max(r => r.Value),
                    FirstValue = ordered.First().Value,
                    LastValue = ordered.Last().Value,
                    IsCumulative = isCumulative,
                    AggregatedValue = isCumulative
                        ? RollupMath.CumulativeDelta(points)
                        : RollupMath.TimeWeightedAverage(points)
                };

                await _repo.AddHourlyRollupsAsync(new[] { rollup }, ct);
                idsSafeToDelete.AddRange(bucket.Select(r => r.Id));
                compressed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Raw->Hourly rollup failed for device {DeviceId} metric {Metric} hour {Bucket}; raw rows kept for retry next cycle",
                    bucket.Key.DeviceId, bucket.Key.Metric, bucket.Key.Bucket);
                // Do NOT delete this bucket's raw rows - retried automatically next tick.
            }
        }

        if (idsSafeToDelete.Count > 0)
            await _repo.RemoveRawAsync(idsSafeToDelete, ct);

        return compressed;
    }

    public async Task<int> CompressHourlyToDailyAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - RollupPolicy.HourlyRetention;
        var hourly = await _repo.GetHourlyOlderThanAsync(cutoff, ct);
        if (hourly.Count == 0) return 0;

        var buckets = hourly
            .GroupBy(h => new { h.DeviceId, h.Metric, Bucket = h.PeriodStart.Date })
            // Only compress a day once ALL 24 of its hours are past the retention
            // cutoff - otherwise a day still partially "in progress" (some hours
            // not yet old enough to even exist as HourlyRollup rows) would get
            // rolled up incomplete.
            .Where(g => g.Key.Bucket.AddDays(1) <= cutoff);

        int compressed = 0;
        var idsSafeToDelete = new List<Guid>();

        foreach (var bucket in buckets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var alreadyDone = await _repo.DailyExistsAsync(bucket.Key.DeviceId, bucket.Key.Metric, bucket.Key.Bucket, ct);
                if (alreadyDone)
                {
                    idsSafeToDelete.AddRange(bucket.Select(h => h.Id));
                    continue;
                }

                var ordered = bucket.OrderBy(h => h.PeriodStart).ToList();
                var isCumulative = ordered.First().IsCumulative;

                var daily = new DailyRollup
                {
                    Id = Guid.NewGuid(),
                    DeviceId = bucket.Key.DeviceId,
                    Metric = bucket.Key.Metric,
                    Unit = ordered.First().Unit,
                    PeriodStart = bucket.Key.Bucket,
                    PeriodEnd = bucket.Key.Bucket.AddDays(1),
                    SampleCount = ordered.Sum(h => h.SampleCount),
                    MinValue = ordered.Min(h => h.MinValue),
                    MaxValue = ordered.Max(h => h.MaxValue),
                    FirstValue = ordered.First().FirstValue,
                    LastValue = ordered.Last().LastValue,
                    IsCumulative = isCumulative,
                    AggregatedValue = isCumulative
                        ? RollupMath.SumDeltas(ordered.Select(h => h.AggregatedValue).ToList())
                        : RollupMath.WeightedAverage(ordered.Select(h => (h.AggregatedValue, h.SampleCount)).ToList())
                };

                await _repo.AddDailyRollupsAsync(new[] { daily }, ct);
                idsSafeToDelete.AddRange(bucket.Select(h => h.Id));
                compressed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Hourly->Daily rollup failed for device {DeviceId} metric {Metric} day {Bucket}; hourly rows kept for retry next cycle",
                    bucket.Key.DeviceId, bucket.Key.Metric, bucket.Key.Bucket);
            }
        }

        if (idsSafeToDelete.Count > 0)
            await _repo.RemoveHourlyAsync(idsSafeToDelete, ct);

        return compressed;
    }

    public async Task<int> CompressDailyToMonthlyAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - RollupPolicy.DailyRetention;
        var daily = await _repo.GetDailyOlderThanAsync(cutoff, ct);
        if (daily.Count == 0) return 0;

        var buckets = daily.GroupBy(d => new
        {
            d.DeviceId,
            d.Metric,
            Bucket = new DateTime(d.PeriodStart.Year, d.PeriodStart.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        int compressed = 0;
        // Daily rows are never deleted by this stage on purpose - see note below.
        var idsSafeToArchive = new List<Guid>();

        foreach (var bucket in buckets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Only fold in a calendar month once it has fully rolled past the
                // daily retention window, so a partially-elapsed month is left alone.
                var monthEnd = bucket.Key.Bucket.AddMonths(1);
                if (monthEnd > cutoff) continue;

                var alreadyDone = await _repo.MonthlyExistsAsync(bucket.Key.DeviceId, bucket.Key.Metric, bucket.Key.Bucket, ct);
                if (alreadyDone)
                {
                    idsSafeToArchive.AddRange(bucket.Select(d => d.Id));
                    continue;
                }

                var ordered = bucket.OrderBy(d => d.PeriodStart).ToList();
                var isCumulative = ordered.First().IsCumulative;

                var monthly = new MonthlyRollup
                {
                    Id = Guid.NewGuid(),
                    DeviceId = bucket.Key.DeviceId,
                    Metric = bucket.Key.Metric,
                    Unit = ordered.First().Unit,
                    PeriodStart = bucket.Key.Bucket,
                    PeriodEnd = monthEnd,
                    SampleCount = ordered.Sum(d => d.SampleCount),
                    MinValue = ordered.Min(d => d.MinValue),
                    MaxValue = ordered.Max(d => d.MaxValue),
                    FirstValue = ordered.First().FirstValue,
                    LastValue = ordered.Last().LastValue,
                    IsCumulative = isCumulative,
                    AggregatedValue = isCumulative
                        ? RollupMath.SumDeltas(ordered.Select(d => d.AggregatedValue).ToList())
                        : RollupMath.WeightedAverage(ordered.Select(d => (d.AggregatedValue, d.SampleCount)).ToList())
                };

                await _repo.AddMonthlyRollupsAsync(new[] { monthly }, ct);
                idsSafeToArchive.AddRange(bucket.Select(d => d.Id));
                compressed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Daily->Monthly rollup failed for device {DeviceId} metric {Metric} month {Bucket}; daily rows kept for retry next cycle",
                    bucket.Key.DeviceId, bucket.Key.Metric, bucket.Key.Bucket);
            }
        }

        // Monthly is the final (kept-forever) tier, but daily rows are the only
        // record with per-day resolution - unlike raw->hourly and hourly->daily,
        // we deliberately do NOT delete daily rows here, only stop compressing new
        // ones. This trades a little extra storage for keeping day-level drill-down
        // available under a monthly report. If storage ever needs trimming further,
        // purge DailyRollup rows older than e.g. 2 years in a separate, explicit step.
        _ = idsSafeToArchive;

        return compressed;
    }

    private static DateTime FloorToHour(DateTime ts) =>
        new(ts.Year, ts.Month, ts.Day, ts.Hour, 0, 0, DateTimeKind.Utc);
}
