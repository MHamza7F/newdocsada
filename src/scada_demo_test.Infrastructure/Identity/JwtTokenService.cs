using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.Infrastructure.Identity;

public class JwtTokenService
{
    private readonly IConfiguration _config;
    private readonly MyDbContextDxy _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<AppRole> _roleManager;
    private readonly PermissionService _permissions;

    public JwtTokenService(
        IConfiguration config,
        MyDbContextDxy db,
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager,
        PermissionService permissions)
    {
        _config = config;
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _permissions = permissions;
    }

    public async Task<(string AccessToken, string RefreshToken, DateTime AccessExpiry, DateTime RefreshExpiry)> GenerateTokensAsync(
        AppUser user,
        string roleName,
        List<string> permissions,
        bool rememberMe,
        DateTime? sessionExpiry = null)
    {
        var validation = JwtValidation.CreateParameters(_config);
        var creds = new SigningCredentials(validation.IssuerSigningKey, SecurityAlgorithms.HmacSha256);

        var jti = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}".Trim()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Role, roleName),
            new("IsSuperAdmin", (user.IsHardcodedSuperAdmin || string.Equals(roleName, IdentitySeeder.SuperAdminRole, StringComparison.OrdinalIgnoreCase)) ? "true" : "false"),
        };

        // Add both Tab and Action permissions
        foreach (var perm in permissions)
        {
            claims.Add(new Claim("perm", perm));
        }

        var refreshExpiry = sessionExpiry ?? (rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(1));
        var accessExpiry = DateTime.UtcNow.AddMinutes(120); // 2 hours, bounded by the session deadline
        if (refreshExpiry < accessExpiry) accessExpiry = refreshExpiry;
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = accessExpiry,
            Issuer = validation.ValidIssuer,
            Audience = validation.ValidAudience,
            SigningCredentials = creds
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = tokenHandler.WriteToken(token);

        // Generate Refresh Token
        var refreshTokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenString,
            JwtId = jti,
            IsUsed = false,
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow,
            ExpiryDate = refreshExpiry
        };

        _db.RefreshTokens.Add(refreshTokenEntity);
        await _db.SaveChangesAsync();

        return (accessToken, refreshTokenString, accessExpiry, refreshExpiry);
    }

    public async Task<(bool Success, string? AccessToken, string? RefreshToken, DateTime? AccessExpiry, DateTime? RefreshExpiry, string? Error, AppUser? User, string? RoleName, List<string>? Permissions)> RefreshTokenAsync(
        string expiredAccessToken,
        string refreshToken)
    {
        // Only lifetime is relaxed for refresh; signature, algorithm, issuer and audience still apply.
        var tokenValidationParameters = JwtValidation.CreateParameters(_config, validateLifetime: false);

        var tokenHandler = new JwtSecurityTokenHandler();
        ClaimsPrincipal principal;
        try
        {
            principal = tokenHandler.ValidateToken(expiredAccessToken, tokenValidationParameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return (false, null, null, null, null, "Invalid token signature.", null, null, null);
            }
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return (false, null, null, null, null, "Invalid access token.", null, null, null);
        }

        var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return (false, null, null, null, null, "Invalid claims in token.", null, null, null);
        }

        var savedRefreshToken = await _db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (savedRefreshToken == null)
        {
            return (false, null, null, null, null, "Refresh token does not exist.", null, null, null);
        }

        if (savedRefreshToken.ExpiryDate <= DateTime.UtcNow)
        {
            return (false, null, null, null, null, "Refresh token has expired.", null, null, null);
        }

        if (savedRefreshToken.IsUsed || savedRefreshToken.IsRevoked)
        {
            return (false, null, null, null, null, "Refresh token is invalid or already used.", null, null, null);
        }

        if (savedRefreshToken.UserId != userId || string.IsNullOrEmpty(jti) || savedRefreshToken.JwtId != jti)
        {
            return (false, null, null, null, null, "Refresh token does not match user.", null, null, null);
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null || await _userManager.IsLockedOutAsync(user))
        {
            return (false, null, null, null, null, "User not found.", null, null, null);
        }

        var roles = await _userManager.GetRolesAsync(user);
        var roleName = roles.FirstOrDefault() ?? string.Empty;
        var role = string.IsNullOrEmpty(roleName) ? null : await _roleManager.FindByNameAsync(roleName);
        var perms = role != null
            ? await _permissions.GetGrantedTabsForRoleAsync(role.Id, roleName)
            : (user.IsHardcodedSuperAdmin ? AppTabs.All.Concat(AppPermissions.All).ToList() : new List<string>());

        // Compare-and-set in the database prevents concurrent refreshes from both succeeding.
        // Insertion and consumption commit together so a failed issuance doesn't burn the old token.
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var consumed = await _db.RefreshTokens
            .Where(r => r.Id == savedRefreshToken.Id && !r.IsUsed && !r.IsRevoked && r.ExpiryDate > now)
            .ExecuteUpdateAsync(update => update.SetProperty(r => r.IsUsed, true));
        if (consumed != 1)
            return (false, null, null, null, null, "Refresh token is invalid or already used.", null, null, null);

        var (newAccess, newRefresh, newAccessExp, newRefreshExp) = await GenerateTokensAsync(
            user, roleName, perms, rememberMe: false, sessionExpiry: savedRefreshToken.ExpiryDate);
        await transaction.CommitAsync();

        return (true, newAccess, newRefresh, newAccessExp, newRefreshExp, null, user, roleName, perms);
    }

    public async Task RevokeTokenAsync(string refreshToken)
    {
        await _db.RefreshTokens.Where(r => r.Token == refreshToken)
            .ExecuteUpdateAsync(update => update.SetProperty(r => r.IsRevoked, true));
    }
}
