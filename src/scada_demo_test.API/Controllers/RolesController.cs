using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using scada_demo_test.Application.DTOs.Roles;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Infrastructure.Identity;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private readonly RoleManager<AppRole> _roleManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly PermissionService _permissions;
    private readonly AuditLogService _auditLogs;

    public RolesController(
        RoleManager<AppRole> roleManager,
        UserManager<AppUser> userManager,
        PermissionService permissions,
        AuditLogService auditLogs)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _permissions = permissions;
        _auditLogs = auditLogs;
    }

    [HttpGet("tabs")]
    public IActionResult GetAvailableTabs() => Ok(AppTabs.All);

    [HttpGet("permissions-catalog")]
    public IActionResult GetPermissionsCatalog() => Ok(new
    {
        Tabs = AppTabs.All,
        Actions = AppPermissions.All
    });

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = _roleManager.Roles.ToList();
        var result = new List<RoleDto>();

        foreach (var role in roles)
        {
            var isSuper = role.Name == IdentitySeeder.SuperAdminRole;
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);

            result.Add(new RoleDto
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                Description = role.Description,
                IsSuperAdminRole = isSuper,
                UserCount = usersInRole.Count,
                Permissions = await _permissions.GetPermissionMapForRoleAsync(role.Id, role.Name ?? string.Empty)
            });
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Role name is required." });

        if (await _roleManager.FindByNameAsync(dto.Name) != null)
            return BadRequest(new { message = "A role with this name already exists." });

        var role = new AppRole(dto.Name) { Description = dto.Description };
        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        var allGrants = AppTabs.All.Concat(AppPermissions.All).ToDictionary(t => t, _ => false);
        await _permissions.SetPermissionsAsync(role.Id, allGrants);

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, "Role.Create", "Role", role.Id.ToString(), $"Created new role '{role.Name}'", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { role.Id });
    }

    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> UpdatePermissions(Guid id, [FromBody] UpdateRolePermissionsDto dto)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null) return NotFound();

        if (role.Name == IdentitySeeder.SuperAdminRole)
            return BadRequest(new { message = "SuperAdmin always has full access and can't be edited." });

        await _permissions.SetPermissionsAsync(id, dto.Permissions);

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, "Role.UpdatePermissions", "Role", role.Id.ToString(), $"Updated permissions for role '{role.Name}'", HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null) return NotFound();

        if (role.Name == IdentitySeeder.SuperAdminRole)
            return BadRequest(new { message = "The SuperAdmin role cannot be deleted." });

        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (usersInRole.Count > 0)
            return BadRequest(new { message = $"Can't delete - {usersInRole.Count} user(s) still have this role." });

        await _roleManager.DeleteAsync(role);

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var userId);
        await _auditLogs.LogAsync(userId, User.Identity?.Name, User.Identity?.Name, "Role.Delete", "Role", role.Id.ToString(), $"Deleted role '{role.Name}'", HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }
}
