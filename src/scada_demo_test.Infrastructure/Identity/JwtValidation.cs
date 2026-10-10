using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace scada_demo_test.Infrastructure.Identity;

public static class JwtValidation
{
    public static TokenValidationParameters CreateParameters(IConfiguration configuration, bool validateLifetime = true)
    {
        var secret = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32 ||
            secret == "SCADA_ENTERPRISE_SUPER_SECURE_SECRET_KEY_2026_!@#$%^&*()")
            throw new InvalidOperationException("Configure Jwt:Key with a unique signing key of at least 32 UTF-8 bytes.");

        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            RequireSignedTokens = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateIssuer = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? "AlamIotScadaApi",
            ValidateAudience = true,
            ValidAudience = configuration["Jwt:Audience"] ?? "AlamIotScadaClients",
            RequireExpirationTime = true,
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }
}
