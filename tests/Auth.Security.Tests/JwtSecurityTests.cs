using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using scada_demo_test.Infrastructure.Identity;
using scada_demo_test.Infrastructure.Persistence;
using Xunit;

namespace Auth.Security.Tests;

public class JwtSecurityTests
{
    private static IConfiguration Config(string? key = "isolated-test-only-key-not-for-deployment-0123456789") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = key, ["Jwt:Issuer"] = "test-issuer", ["Jwt:Audience"] = "test-audience"
        }).Build();

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("SCADA_ENTERPRISE_SUPER_SECURE_SECRET_KEY_2026_!@#$%^&*()")]
    public void MissingWeakOrPublicDefaultSigningKeysAreRejected(string? key) =>
        Assert.Throws<InvalidOperationException>(() => JwtValidation.CreateParameters(Config(key)));

    [Theory]
    [InlineData("other-issuer", "test-audience", SecurityAlgorithms.HmacSha256)]
    [InlineData("test-issuer", "other-audience", SecurityAlgorithms.HmacSha256)]
    [InlineData("test-issuer", "test-audience", SecurityAlgorithms.HmacSha384)]
    public void RefreshValidationRejectsWrongIssuerAudienceAndAlgorithm(string issuer, string audience, string algorithm)
    {
        var validation = JwtValidation.CreateParameters(Config(), validateLifetime: false);
        var jwt = new JwtSecurityToken(issuer, audience, new[] { new Claim("sub", Guid.NewGuid().ToString()) },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(validation.IssuerSigningKey, algorithm));
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(
            new JwtSecurityTokenHandler().WriteToken(jwt), validation, out _));
    }

    [Fact]
    public async Task RotationBindsJwtIdIsSingleUseAndPreservesOriginalExpiry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Tokens.GenerateTokensAsync(fixture.User, "Operator", new(), false);
        var other = await fixture.Tokens.GenerateTokensAsync(fixture.User, "Operator", new(), false);
        Assert.False((await fixture.Tokens.RefreshTokenAsync(other.AccessToken, first.RefreshToken)).Success);
        var rotated = await fixture.Tokens.RefreshTokenAsync(first.AccessToken, first.RefreshToken);
        Assert.True(rotated.Success);
        Assert.Equal(first.RefreshExpiry, rotated.RefreshExpiry);
        Assert.False((await fixture.Tokens.RefreshTokenAsync(first.AccessToken, first.RefreshToken)).Success);
        Assert.True((await fixture.Tokens.RefreshTokenAsync(rotated.AccessToken!, rotated.RefreshToken!)).Success);
    }

    [Fact]
    public async Task RevokedExpiredAndLockedOutSessionsCannotRefresh()
    {
        await using var fixture = await Fixture.CreateAsync();
        var revoked = await fixture.Tokens.GenerateTokensAsync(fixture.User, "Operator", new(), false);
        await fixture.Tokens.RevokeTokenAsync(revoked.RefreshToken);
        Assert.False((await fixture.Tokens.RefreshTokenAsync(revoked.AccessToken, revoked.RefreshToken)).Success);
        var expired = await fixture.Tokens.GenerateTokensAsync(fixture.User, "Operator", new(), false);
        await fixture.Services.GetRequiredService<MyDbContextDxy>().RefreshTokens.Where(r => r.Token == expired.RefreshToken)
            .ExecuteUpdateAsync(update => update.SetProperty(r => r.ExpiryDate, DateTime.UtcNow.AddSeconds(-1)));
        Assert.False((await fixture.Tokens.RefreshTokenAsync(expired.AccessToken, expired.RefreshToken)).Success);
        var locked = await fixture.Tokens.GenerateTokensAsync(fixture.User, "Operator", new(), false);
        var manager = fixture.Services.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.SetLockoutEndDateAsync(fixture.User, DateTimeOffset.UtcNow.AddMinutes(5))).Succeeded);
        Assert.False((await fixture.Tokens.RefreshTokenAsync(locked.AccessToken, locked.RefreshToken)).Success);
    }

    [Fact]
    public async Task RotatedAccessTokenCannotOutliveOriginalSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        var deadline = DateTime.UtcNow.AddMinutes(1);
        var initial = await fixture.Tokens.GenerateTokensAsync(fixture.User, "Operator", new(), false, deadline);
        var refreshed = await fixture.Tokens.RefreshTokenAsync(initial.AccessToken, initial.RefreshToken);
        Assert.True(refreshed.Success);
        Assert.Equal(deadline, refreshed.AccessExpiry);
        Assert.Equal(deadline, refreshed.RefreshExpiry);
    }

    private sealed class Fixture(SqliteConnection connection, ServiceProvider services, AppUser user) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;
        public AppUser User { get; } = user;
        public JwtTokenService Tokens => Services.GetRequiredService<JwtTokenService>();

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Config());
            services.AddScoped<MyDbContextDxy>(_ => new TestDb(new DbContextOptionsBuilder<MyDbContextDxy>()
                .UseSqlite(connection).Options));
            services.AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<MyDbContextDxy>();
            services.AddScoped<PermissionService>();
            services.AddScoped<JwtTokenService>();
            var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<MyDbContextDxy>().Database.EnsureCreatedAsync();
            var user = new AppUser { Id = Guid.NewGuid(), UserName = "test-user", LockoutEnabled = true };
            Assert.True((await provider.GetRequiredService<UserManager<AppUser>>().CreateAsync(user)).Succeeded);
            return new Fixture(connection, provider, user);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task FailedRotationRollsBackConsumptionOfOriginalToken()
    {
        await using var fixture = await Fixture.CreateAsync();
        var initial = await fixture.Tokens.GenerateTokensAsync(fixture.User, "Operator", new(), false);
        var db = (TestDb)fixture.Services.GetRequiredService<MyDbContextDxy>();
        db.FailNextSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Tokens.RefreshTokenAsync(initial.AccessToken, initial.RefreshToken));
        db.ChangeTracker.Clear();
        Assert.False((await db.RefreshTokens.SingleAsync(r => r.Token == initial.RefreshToken)).IsUsed);
        Assert.Equal(1, await db.RefreshTokens.CountAsync());
        Assert.True((await fixture.Tokens.RefreshTokenAsync(initial.AccessToken, initial.RefreshToken)).Success);
    }

    private sealed class TestDb(DbContextOptions<MyDbContextDxy> options) : MyDbContextDxy(options)
    {
        public bool FailNextSave { get; set; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("Simulated issuance failure.");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
