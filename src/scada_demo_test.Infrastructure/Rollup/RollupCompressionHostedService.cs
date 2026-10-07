using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Domain.Constants;

namespace scada_demo_test.Infrastructure.Rollup;

// Drives the 3-tier compression pipeline (Raw->Hourly, Hourly->Daily, Daily->Monthly)
// on independent timers. Each tier runs in its own try/catch inside the loop, so:
//   - a failure in one tier this tick never stops the other tiers, and
//   - a failure THIS tick never stops the service from trying again next tick.
// This is the "project should never go down because one thing failed" requirement -
// the service body can only ever log and continue, it can't throw its way out of
// ExecuteAsync (short of the host itself shutting down via stoppingToken).
public class RollupCompressionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RollupCompressionHostedService> _logger;

    private DateTime _nextRawToHourly = DateTime.MinValue;
    private DateTime _nextHourlyToDaily = DateTime.MinValue;
    private DateTime _nextDailyToMonthly = DateTime.MinValue;

    public RollupCompressionHostedService(IServiceScopeFactory scopeFactory, ILogger<RollupCompressionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Rollup/compression pipeline started (raw>{RawH}h -> hourly>{HourlyD}d -> daily>{DailyD}d -> monthly)",
            RollupPolicy.RawRetention.TotalHours, RollupPolicy.HourlyRetention.TotalDays, RollupPolicy.DailyRetention.TotalDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            if (now >= _nextRawToHourly)
            {
                await RunStageAsync("Raw->Hourly", s => s.CompressRawToHourlyAsync(stoppingToken), stoppingToken);
                _nextRawToHourly = now + RollupPolicy.RawToHourlyInterval;
            }

            if (now >= _nextHourlyToDaily)
            {
                await RunStageAsync("Hourly->Daily", s => s.CompressHourlyToDailyAsync(stoppingToken), stoppingToken);
                _nextHourlyToDaily = now + RollupPolicy.HourlyToDailyInterval;
            }

            if (now >= _nextDailyToMonthly)
            {
                await RunStageAsync("Daily->Monthly", s => s.CompressDailyToMonthlyAsync(stoppingToken), stoppingToken);
                _nextDailyToMonthly = now + RollupPolicy.DailyToMonthlyInterval;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
                break;
            }
        }
    }

    private async Task RunStageAsync(string stageName, Func<IRollupCompressionService, Task<int>> stage, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRollupCompressionService>();
            var compressed = await stage(service);
            if (compressed > 0)
                _logger.LogInformation("Rollup stage {Stage} compressed {Count} bucket(s)", stageName, compressed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Clean host shutdown, do not log as error
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested)
        {
            // Host provider shutting down, do not log as error
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;

            // Whatever went wrong (DB unreachable, etc.) - log it and move on.
            _logger.LogError(ex, "Rollup stage {Stage} failed this cycle - will retry on the next tick", stageName);
        }
    }
}
