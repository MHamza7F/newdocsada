using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using scada_demo_test.Application.DTOs.Users;
using scada_demo_test.Infrastructure.Identity;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AuditLogService _auditLogs;

    public UsersController(UserManager<AppUser> userManager, AuditLogService auditLogs)
    {
        _userManager = userManager;
        _auditLogs = auditLogs;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = _userManager.Users.OrderBy(u => u.CreatedAt).ToList();
        var result = new List<UserDto>();

        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            result.Add(new UserDto
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email ?? string.Empty,
                PhoneNumber = u.PhoneNumber,
                RoleName = roles.FirstOrDefault() ?? string.Empty,
                IsHardcodedSuperAdmin = u.IsHardcodedSuperAdmin,
                CreatedAt = u.CreatedAt
            });
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        if (await _userManager.FindByEmailAsync(dto.Email) != null)
            return BadRequest(new { message = "A user with this email already exists." });

        var user = new AppUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        if (!string.IsNullOrWhiteSpace(dto.RoleName))
            await _userManager.AddToRoleAsync(user, dto.RoleName);

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var currentUserId);
        await _auditLogs.LogAsync(currentUserId, User.Identity?.Name, User.Identity?.Name, "User.Create", "User", user.Id.ToString(), $"Created user '{user.Email}' with role '{dto.RoleName}'", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { user.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound();

        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;
        user.PhoneNumber = dto.PhoneNumber;
        await _userManager.UpdateAsync(user);

        // Hardcoded super admin's role can never be changed away from SuperAdmin
        if (!user.IsHardcodedSuperAdmin && !string.IsNullOrWhiteSpace(dto.RoleName))
        {
            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, dto.RoleName);
        }

        if (!string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
        }

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var currentUserId);
        await _auditLogs.LogAsync(currentUserId, User.Identity?.Name, User.Identity?.Name, "User.Update", "User", user.Id.ToString(), $"Updated user details for '{user.Email}'", HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound();

        if (user.IsHardcodedSuperAdmin)
            return BadRequest(new { message = "The super admin account cannot be deleted." });

        await _userManager.DeleteAsync(user);

        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdStr, out var currentUserId);
        await _auditLogs.LogAsync(currentUserId, User.Identity?.Name, User.Identity?.Name, "User.Delete", "User", user.Id.ToString(), $"Deleted user '{user.Email}'", HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }
}
