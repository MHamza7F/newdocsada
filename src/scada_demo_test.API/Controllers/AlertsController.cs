using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly MyDbContextDxy _db;
    private readonly AuditLogService _auditLogs;

    public AlertsController(MyDbContextDxy db, AuditLogService auditLogs)
    {
        _db = db;
        _auditLogs = auditLogs;
    }

    [HttpGet("incidents")]
    [Authorize(Policy = $"Action:{AppPermissions.AlertsView}")]
    public async Task<IActionResult> GetIncidents([FromQuery] int limit = 50)
    {
        var incidents = await _db.AlertIncidents
            .AsNoTracking()
            .OrderByDescending(i => i.TriggeredAt)
            .Take(limit)
            .ToListAsync();
        return Ok(incidents);
    }

    [HttpPost("acknowledge/{id}")]
    [Authorize(Policy = $"Action:{AppPermissions.AlertsAcknowledge}")]
    public async Task<IActionResult> Acknowledge(Guid id)
    {
        var incident = await _db.AlertIncidents.FindAsync(id);
        if (incident == null) return NotFound();

        incident.AcknowledgedAt = DateTime.UtcNow;
        incident.AcknowledgedBy = User.Identity?.Name ?? "Admin";
        incident.IsResolved = true;
        incident.ResolvedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, "Alert.Acknowledge", "AlertIncident", incident.Id.ToString(), $"Acknowledged alert: {incident.Message}", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(incident);
    }

    [HttpDelete("incidents/{id}")]
    [Authorize(Policy = $"Action:{AppPermissions.AlertsView}")]
    public async Task<IActionResult> DeleteIncident(Guid id)
    {
        var incident = await _db.AlertIncidents.FindAsync(id);
        if (incident == null) return NotFound();

        _db.AlertIncidents.Remove(incident);
        await _db.SaveChangesAsync();

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, "Alert.DeleteIncident", "AlertIncident", id.ToString(), $"Deleted alert incident: {incident.Message}", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { message = "Alert incident deleted." });
    }

    [HttpGet("rules")]
    [Authorize(Policy = $"Action:{AppPermissions.AlertsView}")]
    public async Task<IActionResult> GetRules()
    {
        var rules = await _db.AlertRules
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return Ok(rules);
    }

    [HttpPost("rules")]
    [Authorize(Policy = $"Action:{AppPermissions.AlertsConfigure}")]
    public async Task<IActionResult> CreateRule([FromBody] AlertRule rule)
    {
        rule.Id = Guid.NewGuid();
        rule.CreatedAt = DateTime.UtcNow;
        _db.AlertRules.Add(rule);
        await _db.SaveChangesAsync();

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, "AlertRule.Create", "AlertRule", rule.Id.ToString(), $"Created alert rule for {rule.Metric} {rule.Condition} {rule.ThresholdValue}", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(rule);
    }

    [HttpDelete("rules/{id}")]
    [Authorize(Policy = $"Action:{AppPermissions.AlertsConfigure}")]
    public async Task<IActionResult> DeleteRule(Guid id)
    {
        var rule = await _db.AlertRules.FindAsync(id);
        if (rule == null) return NotFound();

        _db.AlertRules.Remove(rule);
        await _db.SaveChangesAsync();

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, "AlertRule.Delete", "AlertRule", id.ToString(), $"Deleted alert rule", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { message = "Alert rule deleted." });
    }
}
