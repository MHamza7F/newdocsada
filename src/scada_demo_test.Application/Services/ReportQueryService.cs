using scada_demo_test.Application.DTOs;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Interfaces;

namespace scada_demo_test.Application.Services;

public class ReportQueryService : IReportQueryService
{
    private readonly ISensorReadingRepository _rawRepo;
    private readonly IRollupRepository _rollupRepo;

    public ReportQueryService(ISensorReadingRepository rawRepo, IRollupRepository rollupRepo)
    {
        _rawRepo = rawRepo;
        _rollupRepo = rollupRepo;
    }

    public async Task<HistorySeriesDto> GetHistoryAsync(Guid deviceId, string metric, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var isCumulative = MetricCatalog.IsCumulative(metric);
        var dto = new HistorySeriesDto { Metric = metric, IsCumulative = isCumulative };

        // Pick the finest tier that still has data covering the whole requested range -
        // a range that reaches further back than a tier's retention window has already
        // been compressed away from that tier, so fall back to the next coarser one.
        if (fromUtc >= now - RollupPolicy.RawRetention)
        {
            dto.Resolution = "Raw";
            var raw = await _rawRepo.GetRangeAsync(deviceId, metric, fromUtc, toUtc, ct);
            dto.Unit = raw.FirstOrDefault()?.Unit;
            dto.Points = raw.Select(r => new HistoryPointDto { Timestamp = r.Timestamp, Value = r.Value }).ToList();
            if (raw.Count > 0)
            {
                dto.Min = raw.Min(r => r.Value);
                dto.Max = raw.Max(r => r.Value);
                dto.Average = raw.Average(r => r.Value);
            }
        }
        else if (fromUtc >= now - RollupPolicy.HourlyRetention)
        {
            dto.Resolution = "Hourly";
            var hourly = await _rollupRepo.GetHourlySeriesAsync(deviceId, metric, fromUtc, toUtc, ct);
            dto.Unit = hourly.FirstOrDefault()?.Unit;
            dto.Points = hourly.Select(h => new HistoryPointDto { Timestamp = h.PeriodStart, Value = h.AggregatedValue }).ToList();
            FillSummary(dto, hourly.Select(h => h.AggregatedValue).ToList(), isCumulative);
        }
        else if (fromUtc >= now - RollupPolicy.DailyRetention)
        {
            dto.Resolution = "Daily";
            var daily = await _rollupRepo.GetDailySeriesAsync(deviceId, metric, fromUtc, toUtc, ct);
            dto.Unit = daily.FirstOrDefault()?.Unit;
            dto.Points = daily.Select(d => new HistoryPointDto { Timestamp = d.PeriodStart, Value = d.AggregatedValue }).ToList();
            FillSummary(dto, daily.Select(d => d.AggregatedValue).ToList(), isCumulative);
        }
        else
        {
            dto.Resolution = "Monthly";
            var monthly = await _rollupRepo.GetMonthlySeriesAsync(deviceId, metric, fromUtc, toUtc, ct);
            dto.Unit = monthly.FirstOrDefault()?.Unit;
            dto.Points = monthly.Select(m => new HistoryPointDto { Timestamp = m.PeriodStart, Value = m.AggregatedValue }).ToList();
            FillSummary(dto, monthly.Select(m => m.AggregatedValue).ToList(), isCumulative);
        }

        return dto;
    }

    private static void FillSummary(HistorySeriesDto dto, List<double> values, bool isCumulative)
    {
        if (values.Count == 0) return;
        if (isCumulative)
        {
            dto.Total = values.Sum();
        }
        else
        {
            dto.Average = values.Average();
            dto.Min = values.Min();
            dto.Max = values.Max();
        }
    }
}
