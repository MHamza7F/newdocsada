using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
        bool rememberMe)
    {
        var secret = _config["Jwt:Key"] ?? "SCADA_ENTERPRISE_SUPER_SECURE_SECRET_KEY_2026_!@#$%^&*()";
        var issuer = _config["Jwt:Issuer"] ?? "AlamIotScadaApi";
        var audience = _config["Jwt:Audience"] ?? "AlamIotScadaClients";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

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

        var accessExpiry = DateTime.UtcNow.AddMinutes(120); // 2 hours
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = accessExpiry,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = creds
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = tokenHandler.WriteToken(token);

        // Generate Refresh Token
        var refreshExpiry = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(1);
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
        var secret = _config["Jwt:Key"] ?? "SCADA_ENTERPRISE_SUPER_SECURE_SECRET_KEY_2026_!@#$%^&*()";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false // Allow expired token
        };

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
        catch (Exception ex)
        {
            return (false, null, null, null, null, $"Token validation error: {ex.Message}", null, null, null);
        }

        var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return (false, null, null, null, null, "Invalid claims in token.", null, null, null);
        }

        var savedRefreshToken = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (savedRefreshToken == null)
        {
            return (false, null, null, null, null, "Refresh token does not exist.", null, null, null);
        }

        if (savedRefreshToken.ExpiryDate < DateTime.UtcNow)
        {
            return (false, null, null, null, null, "Refresh token has expired.", null, null, null);
        }

        if (savedRefreshToken.IsUsed || savedRefreshToken.IsRevoked)
        {
            return (false, null, null, null, null, "Refresh token is invalid or already used.", null, null, null);
        }

        if (savedRefreshToken.UserId != userId)
        {
            return (false, null, null, null, null, "Refresh token does not match user.", null, null, null);
        }

        // Mark old token as used (rotation)
        savedRefreshToken.IsUsed = true;
        await _db.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, null, null, null, null, "User not found.", null, null, null);
        }

        var roles = await _userManager.GetRolesAsync(user);
        var roleName = roles.FirstOrDefault() ?? string.Empty;
        var role = await _roleManager.FindByNameAsync(roleName);
        var perms = role != null
            ? await _permissions.GetGrantedTabsForRoleAsync(role.Id, roleName)
            : (user.IsHardcodedSuperAdmin ? AppTabs.All.Concat(AppPermissions.All).ToList() : new List<string>());

        var (newAccess, newRefresh, newAccessExp, newRefreshExp) = await GenerateTokensAsync(user, roleName, perms, rememberMe: true);

        return (true, newAccess, newRefresh, newAccessExp, newRefreshExp, null, user, roleName, perms);
    }

    public async Task RevokeTokenAsync(string refreshToken)
    {
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (token != null)
        {
            token.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }
}
