using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SitesController : ControllerBase
{
    private readonly MyDbContextDxy _db;
    private readonly AuditLogService _auditLogs;

    public SitesController(MyDbContextDxy db, AuditLogService auditLogs)
    {
        _db = db;
        _auditLogs = auditLogs;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var sites = await _db.Sites.AsNoTracking().OrderBy(s => s.Code).ToListAsync();
        return Ok(sites);
    }

    [HttpPost]
    [Authorize(Policy = $"Action:{AppPermissions.SitesManage}")]
    public async Task<IActionResult> CreateSite([FromBody] Site site)
    {
        site.Id = Guid.NewGuid();
        site.CreatedAt = DateTime.UtcNow;
        _db.Sites.Add(site);
        await _db.SaveChangesAsync();

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, "Site.Create", "Site", site.Id.ToString(), $"Created site '{site.Name}' ({site.Code})", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(site);
    }
}
