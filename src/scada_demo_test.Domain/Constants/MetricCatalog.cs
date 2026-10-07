namespace scada_demo_test.Domain.Constants;

// Tells the rollup engine HOW a metric should be compressed:
//   - Cumulative counters (Totalizer) only ever go up (except on a hardware/counter
//     reset), so they're compressed with Last/First delta logic.
//   - Everything else (FlowRate, Pressure, Temperature, ...) is an instantaneous
//     reading, so it's compressed with a time-weighted average instead - a plain
//     average would quietly drift if samples aren't evenly spaced.
public static class MetricCatalog
{
    private static readonly HashSet<string> CumulativeMetrics = new(StringComparer.OrdinalIgnoreCase)
    {
        "Totalizer"
    };

    public static bool IsCumulative(string metric) => CumulativeMetrics.Contains(metric);
}
