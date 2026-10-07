using System.Text.Json;

namespace scada_demo_test.Domain.Drivers;

// Represents WHICH of the driver's two value columns are active for a sensor.
// Driven ENTIRELY by the operator's parameter selection (Sensor.MetricFields):
//   - an unselected parameter is never written to the telemetry table,
//   - never read from it, and
//   - never broadcast over SignalR (so the Monitoring card shows only what the
//     operator chose when they added / edited the sensor).
// Column names always come from the driver catalog - never from user input.
public sealed class SensorColumnSet
{
    public string? Primary { get; }
    public string? Secondary { get; }

    private SensorColumnSet(string? primary, string? secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }

    /// <summary>Resolves the active columns for a sensor from its stored
    /// MetricFields JSON. No selection (legacy sensor, or default full set)
    /// reports BOTH driver columns.</summary>
    public static SensorColumnSet For(ISensorDriver driver, string? metricFieldsJson)
    {
        var selected = ParseKeys(metricFieldsJson);

        if (selected is null || selected.Count == 0)
        {
            return new SensorColumnSet(driver.PrimaryColumnName, driver.SecondaryColumnName);
        }

        return new SensorColumnSet(
            selected.Contains(driver.PrimaryColumnName) ? driver.PrimaryColumnName : null,
            selected.Contains(driver.SecondaryColumnName) ? driver.SecondaryColumnName : null);
    }

    public bool HasAny => Primary is not null || Secondary is not null;
    public bool HasPrimary => Primary is not null;
    public bool HasSecondary => Secondary is not null;

    // Cache-busting key for table provisioning: a changed selection (via "Edit
    // Parameters") must re-run idempotent ADD COLUMN IF NOT EXISTS.
    public string Signature => $"{(Primary ?? "-")}|{(Secondary ?? "-")}";

    private static HashSet<string>? ParseKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict is null ? null : new HashSet<string>(dict.Keys, StringComparer.Ordinal);
        }
        catch
        {
            return null;
        }
    }
}