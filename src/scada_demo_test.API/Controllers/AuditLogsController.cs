using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/audit-logs")]
public class AuditLogsController : ControllerBase
{
    private readonly MyDbContextDxy _db;

    public AuditLogsController(MyDbContextDxy db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs([FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(a =>
                (a.UserEmail != null && a.UserEmail.Contains(s)) ||
                (a.UserName != null && a.UserName.Contains(s)) ||
                a.Action.Contains(s) ||
                a.EntityName.Contains(s) ||
                (a.Details != null && a.Details.Contains(s)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        });
    }
}
