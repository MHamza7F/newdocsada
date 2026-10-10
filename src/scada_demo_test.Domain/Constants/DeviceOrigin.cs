namespace scada_demo_test.Domain.Constants;

// Which management tab registered a Device. The Norvi tab and the Gateway tab
// show disjoint device lists (and therefore disjoint attached sensors).
public static class DeviceOrigin
{
    public const string Norvi = "Norvi";
    public const string Gateway = "Gateway";

    public static string Normalize(string? raw, bool norviHardware) =>
        (raw ?? "").Trim() switch
        {
            var s when s.Equals(Norvi, StringComparison.OrdinalIgnoreCase) => Norvi,
            var s when s.Equals(Gateway, StringComparison.OrdinalIgnoreCase) => Gateway,
            // Legacy clients don't send the field - infer from the hardware class.
            _ => norviHardware ? Norvi : Gateway
        };
}
