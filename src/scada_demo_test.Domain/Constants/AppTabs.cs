namespace scada_demo_test.Domain.Constants;

// Every permission-gated section of the web app lives here as a simple string key.
public static class AppTabs
{
    public const string Monitoring = "Monitoring";
    public const string Summary = "Summary";
    public const string Charts = "Charts";
    public const string Tanks = "Tanks";
    public const string Energy = "Energy";
    public const string Reports = "Reports";
    public const string Devices = "Devices";
    public const string Gateway = "Gateway";
    public const string Alerts = "Alerts";
    public const string AuditLogs = "AuditLogs";
    public const string MultiSite = "MultiSite";
    public const string FirmwareOTA = "FirmwareOTA";
    public const string Users = "Users";
    public const string Settings = "Settings";

    public static readonly string[] All =
    {
        Monitoring, Summary, Charts, Tanks, Energy, Reports, Devices, Gateway, Alerts, AuditLogs, MultiSite, FirmwareOTA, Users, Settings
    };
}
