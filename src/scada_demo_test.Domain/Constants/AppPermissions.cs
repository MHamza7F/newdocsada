namespace scada_demo_test.Domain.Constants;

public static class AppPermissions
{
    // Devices
    public const string DevicesView = "Devices.View";
    public const string DevicesAdd = "Devices.Add";
    public const string DevicesEdit = "Devices.Edit";
    public const string DevicesDelete = "Devices.Delete";

    // Reports
    public const string ReportsView = "Reports.View";
    public const string ReportsExportPdf = "Reports.ExportPdf";
    public const string ReportsExportExcel = "Reports.ExportExcel";

    // Alerts
    public const string AlertsView = "Alerts.View";
    public const string AlertsConfigure = "Alerts.Configure";
    public const string AlertsAcknowledge = "Alerts.Acknowledge";

    // Firmware / OTA
    public const string FirmwareView = "Firmware.View";
    public const string FirmwareUpload = "Firmware.Upload";
    public const string FirmwareRollout = "Firmware.Rollout";

    // Multi-Site
    public const string SitesView = "Sites.View";
    public const string SitesManage = "Sites.Manage";

    // Audit Logs
    public const string AuditLogsView = "AuditLogs.View";

    // Users & Roles Management
    public const string UsersManage = "Users.Manage";
    public const string RolesManage = "Roles.Manage";

    public static readonly string[] All =
    {
        DevicesView, DevicesAdd, DevicesEdit, DevicesDelete,
        ReportsView, ReportsExportPdf, ReportsExportExcel,
        AlertsView, AlertsConfigure, AlertsAcknowledge,
        FirmwareView, FirmwareUpload, FirmwareRollout,
        SitesView, SitesManage,
        AuditLogsView,
        UsersManage, RolesManage
    };
}
