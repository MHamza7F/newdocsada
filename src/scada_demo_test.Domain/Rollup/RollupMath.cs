namespace scada_demo_test.Domain.Rollup;

// Pure, deterministic aggregation math - no DB, no I/O, fully unit-testable.
// Kept in Domain so both the raw->hourly step (input: SensorReading points) and the
// hourly->daily / daily->monthly steps (input: prior rollup rows) can reuse the exact
// same formulas via RollupSource, avoiding any drift between tiers.
public readonly record struct RollupSource(DateTime Timestamp, double Value, double Weight);

public static class RollupMath
{
    // Time-weighted average for instantaneous metrics (FlowRate, Pressure, ...).
    // Trapezoidal integration between consecutive samples so uneven sampling
    // intervals don't skew the result the way a plain arithmetic mean would.
    public static double TimeWeightedAverage(IReadOnlyList<(DateTime Timestamp, double Value)> points)
    {
        if (points.Count == 0) return 0;
        if (points.Count == 1) return points[0].Value;

        var sorted = points.OrderBy(p => p.Timestamp).ToList();
        double weightedSum = 0, totalWeight = 0;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var seconds = (sorted[i + 1].Timestamp - sorted[i].Timestamp).TotalSeconds;
            if (seconds <= 0) continue;
            var segmentAvg = (sorted[i].Value + sorted[i + 1].Value) / 2.0;
            weightedSum += segmentAvg * seconds;
            totalWeight += seconds;
        }

        return totalWeight > 0 ? weightedSum / totalWeight : sorted.Average(p => p.Value);
    }

    // Combines already-averaged rollup rows (e.g. 24 HourlyRollup rows -> 1 Daily
    // value) weighted by each row's own sample count, so an hour built from more
    // raw samples counts proportionally more than a sparse one.
    public static double WeightedAverage(IReadOnlyList<(double Value, int Weight)> rows)
    {
        if (rows.Count == 0) return 0;
        var totalWeight = rows.Sum(r => r.Weight);
        if (totalWeight <= 0) return rows.Average(r => r.Value);
        return rows.Sum(r => r.Value * r.Weight) / totalWeight;
    }

    // Cumulative-counter delta (Totalizer) across raw points, tolerant of a hardware
    // counter reset: a negative step is treated as "counter restarted from ~0" and
    // the new value is counted in full rather than subtracted, so a reset never
    // shows up as negative/lost consumption.
    public static double CumulativeDelta(IReadOnlyList<(DateTime Timestamp, double Value)> points)
    {
        if (points.Count < 2) return 0;
        var sorted = points.OrderBy(p => p.Timestamp).ToList();
        double delta = 0;
        for (int i = 1; i < sorted.Count; i++)
        {
            var diff = sorted[i].Value - sorted[i - 1].Value;
            delta += diff >= 0 ? diff : sorted[i].Value;
        }
        return delta;
    }

    // Sums already-computed deltas from the tier below (e.g. 24 hourly deltas -> 1
    // daily delta). No reset-handling needed here - that was already resolved when
    // each lower-tier delta was first computed.
    public static double SumDeltas(IReadOnlyList<double> deltas) => deltas.Sum();
}
