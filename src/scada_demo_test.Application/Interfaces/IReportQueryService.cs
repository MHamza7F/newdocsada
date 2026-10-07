using scada_demo_test.Application.DTOs;

namespace scada_demo_test.Application.Interfaces;

// Powers "any date range the user picks" reports/charts - from the last minute up
// to a full year - by transparently reading from whichever storage tier (Raw/
// Hourly/Daily/Monthly) still has data covering that range, instead of the caller
// having to know the retention policy.
public interface IReportQueryService
{
    Task<HistorySeriesDto> GetHistoryAsync(Guid deviceId, string metric, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
