using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace scada_demo_test.Web.Services;

// Each client resolves its own circuit identity; tokens live in an isolated server session.
public class PermissionForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IServiceProvider _serviceProvider;
    private readonly WebAuthSessionStore _sessions;

    public PermissionForwardingHandler(IHttpContextAccessor accessor, IServiceProvider serviceProvider,
        WebAuthSessionStore sessions)
    {
        _accessor = accessor;
        _serviceProvider = serviceProvider;
        _sessions = sessions;
        InnerHandler = new HttpClientHandler { AllowAutoRedirect = false };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var user = _accessor.HttpContext?.User;
        // Prefer the circuit identity when available; HttpContext may be from the initial handshake.
        try
        {
            var provider = _serviceProvider.GetService<AuthenticationStateProvider>();
            if (provider != null) user = (await provider.GetAuthenticationStateAsync()).User;
        }
        catch (InvalidOperationException) { /* No active Blazor circuit during an HTTP endpoint. */ }

        var session = _sessions.Find(user);
        request.Headers.Remove("X-Requesting-Role");
        var path = request.RequestUri?.AbsolutePath;
        var isAuthRequest = string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "/api/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "/api/auth/logout", StringComparison.OrdinalIgnoreCase);
        var token = session?.Tokens.AccessToken;
        var ownsAuthorization = request.Headers.Authorization == null && !isAuthRequest && session != null;
        if (ownsAuthorization)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            // Buffer before the first send so a single retry uses a new request, even for JSON writes.
            var body = ownsAuthorization && request.Content != null
                ? await request.Content.ReadAsByteArrayAsync(cancellationToken) : null;
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized || !ownsAuthorization || session == null)
                return response;

            await session.Gate.WaitAsync(cancellationToken);
            try
            {
                if (session.Revoked || session.Tokens.RefreshTokenExpiry <= DateTime.UtcNow) return response;
                // Another request in this session may already have refreshed while we waited.
                if (session.Tokens.AccessToken == token)
                {
                    using var refresh = new HttpRequestMessage(HttpMethod.Post, new Uri(request.RequestUri!, "/api/auth/refresh"))
                    {
                        Content = JsonContent.Create(new { accessToken = token, refreshToken = session.Tokens.RefreshToken })
                    };
                    using var refreshed = await base.SendAsync(refresh, cancellationToken);
                    if (!refreshed.IsSuccessStatusCode)
                    {
                        if (refreshed.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
                            session.Revoked = true;
                        return response;
                    }
                    ScadaDemoTestApiClient.LoginResponse? data;
                    try { data = await refreshed.Content.ReadFromJsonAsync<ScadaDemoTestApiClient.LoginResponse>(cancellationToken); }
                    catch (System.Text.Json.JsonException) { return response; }
                    if (data == null || data.UserId != session.Tokens.UserId || string.IsNullOrWhiteSpace(data.AccessToken) ||
                        string.IsNullOrWhiteSpace(data.RefreshToken)) return response;
                    session.Tokens = data;
                }
            }
            finally { session.Gate.Release(); }

            using var retry = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy
            };
            foreach (var header in request.Headers) retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var option in request.Options) retry.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
            if (body != null)
            {
                retry.Content = new ByteArrayContent(body);
                foreach (var header in request.Content!.Headers) retry.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Tokens.AccessToken);
            response.Dispose();
            return await base.SendAsync(retry, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Preserve cancellation instead of presenting it as a server response.
        }
        catch (OperationCanceledException)
        {
            return Error(request, HttpStatusCode.GatewayTimeout, "API request timed out.");
        }
        catch (HttpRequestException)
        {
            return Error(request, HttpStatusCode.ServiceUnavailable, "API is unavailable. Please try again.");
        }
    }

    private static HttpResponseMessage Error(HttpRequestMessage request, HttpStatusCode status, string message) => new(status)
    {
        RequestMessage = request,
        Content = JsonContent.Create(new { error = message })
    };
}
