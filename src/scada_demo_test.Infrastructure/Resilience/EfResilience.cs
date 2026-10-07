using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;

namespace scada_demo_test.Infrastructure.Resilience;

// One shared retry policy for every DB write in the app. Industrial telemetry keeps
// arriving every second whether or not the database had a one-off blip (connection
// pool exhaustion, a deadlock, a brief hiccup) - retrying a handful of times turns
// those transient errors into a short delay instead of a dropped reading or a crashed
// background service.
//
// LOCAL TUNING (2026-09-22): the Supabase-era policy below retried 4x with an
// exponential backoff (200ms * 2^n + jitter, up to ~3s stall) because the database
// was a REMOTE cloud Postgres. On local SQL Server LocalDB that stall pushed back the
// poller's sequential per-sensor insert loop, so it is now 3 quick retries at a fixed
// 100ms. Supabase-era policy kept above for the documented revert path
// (DB_LOCAL_SWITCH_CHANGELOG.md).
//
// Supabase-era (commented out):
//     .Handle<NpgsqlException>()
//     .WaitAndRetryAsync(retryCount: 4,
//         sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1))
//                                            + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100)));
public static class EfResilience
{
    public static readonly AsyncRetryPolicy Policy = Polly.Policy
        .Handle<SqlException>()
        .Or<Microsoft.Data.Sqlite.SqliteException>()
        .Or<DbUpdateException>(IsTransient)
        .Or<TimeoutException>()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: _ => TimeSpan.FromMilliseconds(100));

    private static bool IsTransient(DbUpdateException ex) =>
        ex.InnerException is SqlException || ex.InnerException is Microsoft.Data.Sqlite.SqliteException;

    // Wrap any DB call: EfResilience.RunAsync(() => _db.SaveChangesAsync(ct))
    public static Task RunAsync(Func<Task> action) => Policy.ExecuteAsync(action);

    public static Task<T> RunAsync<T>(Func<Task<T>> action) => Policy.ExecuteAsync(action);
}
