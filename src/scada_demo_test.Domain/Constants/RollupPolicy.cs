namespace scada_demo_test.Domain.Constants;

// Central knobs for the tiered storage/compression pipeline.
// Raw (1-second) data is kept just long enough to be safely rolled up, then purged -
// this is what keeps the SensorReadings table small no matter how long the plant runs.
//
//   Raw (seconds)  --3h--> Hourly rollups  --7d--> Daily rollups --60d--> Monthly rollups (kept forever)
//
// Every stage only ever reads data OLDER than its retention window (never data still
// "in flight"), and raw/source rows are only deleted after the rollup row that replaces
// them has been committed - so a crash mid-cycle just means the same bucket is retried
// on the next tick, never silent data loss.
public static class RollupPolicy
{
    public static readonly TimeSpan RawRetention = TimeSpan.FromHours(3);
    public static readonly TimeSpan HourlyRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan DailyRetention = TimeSpan.FromDays(60);

    // How often the background service checks each tier. Cheap query if there's
    // nothing to do yet, so short intervals are fine.
    public static readonly TimeSpan RawToHourlyInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan HourlyToDailyInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan DailyToMonthlyInterval = TimeSpan.FromHours(6);
}
