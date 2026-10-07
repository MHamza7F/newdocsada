using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using scada_demo_test.Application.DTOs.Auth;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Infrastructure.Identity;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly RoleManager<AppRole> _roleManager;
    private readonly PermissionService _permissions;
    private readonly JwtTokenService _jwtTokenService;
    private readonly AuditLogService _auditLogService;

    public AuthController(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        RoleManager<AppRole> roleManager,
        PermissionService permissions,
        JwtTokenService jwtTokenService,
        AuditLogService auditLogService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _permissions = permissions;
        _jwtTokenService = jwtTokenService;
        _auditLogService = auditLogService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return Unauthorized(new { message = "Email and password are required." });

        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            await _auditLogService.LogAsync(null, dto.Email, null, "Auth.FailedLogin", "User", null, "User not found", HttpContext.Connection.RemoteIpAddress?.ToString());
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var check = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            await _auditLogService.LogAsync(user.Id, user.Email, $"{user.FirstName} {user.LastName}", "Auth.FailedLogin", "User", user.Id.ToString(), check.IsLockedOut ? "Account locked out" : "Wrong password", HttpContext.Connection.RemoteIpAddress?.ToString());
            if (check.IsLockedOut)
                return Unauthorized(new { message = "Account locked due to too many failed attempts. Please try again later." });
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var roleName = roles.FirstOrDefault() ?? string.Empty;
        var role = await _roleManager.FindByNameAsync(roleName);

        var permissions = role != null
            ? await _permissions.GetGrantedTabsForRoleAsync(role.Id, roleName)
            : (user.IsHardcodedSuperAdmin ? AppTabs.All.Concat(AppPermissions.All).ToList() : new List<string>());

        var (accessToken, refreshToken, accessExp, refreshExp) = await _jwtTokenService.GenerateTokensAsync(user, roleName, permissions, dto.RememberMe);

        await _auditLogService.LogAsync(user.Id, user.Email, $"{user.FirstName} {user.LastName}", "Auth.LoginSuccess", "User", user.Id.ToString(), $"Logged in successfully via {(dto.RememberMe ? "Remember Me" : "Session")}", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new LoginResponseDto
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            RoleName = roleName,
            IsSuperAdmin = user.IsHardcodedSuperAdmin || string.Equals(roleName, IdentitySeeder.SuperAdminRole, StringComparison.OrdinalIgnoreCase),
            Permissions = permissions,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiry = accessExp,
            RefreshTokenExpiry = refreshExp
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto dto)
    {
        if (string.IsNullOrEmpty(dto.AccessToken) || string.IsNullOrEmpty(dto.RefreshToken))
            return BadRequest(new { message = "Access token and refresh token are required." });

        var (success, newAccess, newRefresh, newAccessExp, newRefreshExp, error, user, roleName, perms) =
            await _jwtTokenService.RefreshTokenAsync(dto.AccessToken, dto.RefreshToken);

        if (!success || user == null)
            return Unauthorized(new { message = error ?? "Invalid refresh token." });

        return Ok(new LoginResponseDto
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            RoleName = roleName ?? string.Empty,
            IsSuperAdmin = user.IsHardcodedSuperAdmin || string.Equals(roleName, IdentitySeeder.SuperAdminRole, StringComparison.OrdinalIgnoreCase),
            Permissions = perms ?? new(),
            AccessToken = newAccess!,
            RefreshToken = newRefresh!,
            AccessTokenExpiry = newAccessExp ?? DateTime.UtcNow.AddMinutes(120),
            RefreshTokenExpiry = newRefreshExp ?? DateTime.UtcNow.AddDays(30)
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestDto dto)
    {
        if (!string.IsNullOrEmpty(dto.RefreshToken))
        {
            await _jwtTokenService.RevokeTokenAsync(dto.RefreshToken);
        }
        return Ok(new { message = "Logged out successfully." });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        var roleName = roles.FirstOrDefault() ?? string.Empty;
        var role = await _roleManager.FindByNameAsync(roleName);
        var perms = role != null
            ? await _permissions.GetGrantedTabsForRoleAsync(role.Id, roleName)
            : new List<string>();

        return Ok(new
        {
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = roleName,
            Permissions = perms
        });
    }
}
