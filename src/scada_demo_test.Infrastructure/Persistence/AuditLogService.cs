using scada_demo_test.Domain.Entities;

namespace scada_demo_test.Infrastructure.Persistence;

public class AuditLogService
{
    private readonly MyDbContextDxy _db;

    public AuditLogService(MyDbContextDxy db)
    {
        _db = db;
    }

    public async Task LogAsync(
        Guid? userId,
        string? userEmail,
        string? userName,
        string action,
        string entityName,
        string? entityId = null,
        string? details = null,
        string? ipAddress = null)
    {
        try
        {
            var entry = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                UserEmail = userEmail,
                UserName = userName,
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                Details = details,
                IpAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            };

            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Audit log failure must not tear down main business transactions
        }
    }
}
