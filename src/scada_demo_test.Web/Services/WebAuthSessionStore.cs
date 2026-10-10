using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace scada_demo_test.Web.Services;

// Tokens never cross users or enter the authentication cookie. A restart requires sign-in again.
public sealed class WebAuthSessionStore : IDisposable
{
    public const string SessionClaim = "scada_session";
    private readonly MemoryCache _sessions = new(new MemoryCacheOptions { SizeLimit = 10000 });

    public sealed class Session(ScadaDemoTestApiClient.LoginResponse tokens)
    {
        public ScadaDemoTestApiClient.LoginResponse Tokens { get; set; } = tokens;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool Revoked { get; set; }
    }

    public string Create(ScadaDemoTestApiClient.LoginResponse tokens)
    {
        var id = Guid.NewGuid().ToString("N");
        _sessions.Set(id, new Session(tokens), new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = new DateTimeOffset(DateTime.SpecifyKind(tokens.RefreshTokenExpiry, DateTimeKind.Utc)),
            Size = 1
        });
        return id;
    }

    public Session? Find(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        var id = user.FindFirstValue(SessionClaim);
        if (id == null || !_sessions.TryGetValue<Session>(id, out var session) || session == null ||
            session.Revoked || session.Tokens.RefreshTokenExpiry <= DateTime.UtcNow ||
            user.FindFirstValue(ClaimTypes.NameIdentifier) != session.Tokens.UserId.ToString()) return null;
        return session;
    }

    public void Remove(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(SessionClaim);
        if (id == null) return;
        if (_sessions.TryGetValue<Session>(id, out var session) && session != null) session.Revoked = true;
        _sessions.Remove(id);
    }

    public void Dispose() => _sessions.Dispose();
}
