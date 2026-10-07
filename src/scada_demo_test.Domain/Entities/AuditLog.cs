namespace scada_demo_test.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty; // e.g., "User.Login", "Device.Create", "Role.Update"
    public string EntityName { get; set; } = string.Empty; // e.g., "Device", "AppRole", "AlertRule"
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
