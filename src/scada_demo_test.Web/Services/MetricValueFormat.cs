namespace scada_demo_test.Web.Services;

/// <summary>Formats a live metric for cards/tables. Flow meters (m³/h, m³, Nm³/h)
/// report in 3 decimals; temperature/humidity in 1 decimal. Negative zero from
/// a meter is normalized so "-0.0" never shows as a leading minus.</summary>
public static class MetricValueFormat
{
    public static string Display(double? value, string? unit)
    {
        if (value is null) return "--";

        var v = value.Value;
        if (v == 0) v = 0; // -0.0 -> 0

        var isFlow = unit is not null &&
                     (unit.Contains("m³") || unit.Contains("Nm³") || unit.Contains("m3"));
        return v.ToString(isFlow ? "0.000" : "0.0", System.Globalization.CultureInfo.InvariantCulture);
    }
}