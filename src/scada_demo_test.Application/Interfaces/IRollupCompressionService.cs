namespace scada_demo_test.Application.Interfaces;

// Runs one pass of a single compression stage. Returns how many buckets were
// compressed (0 is normal - it just means nothing was old enough yet), and never
// throws for a single bad bucket: a failed bucket is skipped and retried on the
// next call, its raw/source data is never deleted until it has been successfully
// rolled up, so a crash or a bad value in one device/metric can't take down the
// pipeline or lose data for every other device.
public interface IRollupCompressionService
{
    Task<int> CompressRawToHourlyAsync(CancellationToken ct = default);
    Task<int> CompressHourlyToDailyAsync(CancellationToken ct = default);
    Task<int> CompressDailyToMonthlyAsync(CancellationToken ct = default);
}
