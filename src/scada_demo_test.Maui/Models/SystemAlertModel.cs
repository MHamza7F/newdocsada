using CommunityToolkit.Mvvm.ComponentModel;

namespace scada_demo_test.Maui.Models;

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Model for active alarms and system health warnings.
/// </summary>
public partial class SystemAlertModel : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SourceDevice { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [ObservableProperty]
    private bool _isAcknowledged;

    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string SeverityBadgeColor => Severity switch
    {
        AlertSeverity.Critical => "#EF4444",
        AlertSeverity.Warning => "#F59E0B",
        _ => "#06B6D4"
    };

    public string SeverityIcon => Severity switch
    {
        AlertSeverity.Critical => "🚨",
        AlertSeverity.Warning => "⚠️",
        _ => "ℹ️"
    };
}
