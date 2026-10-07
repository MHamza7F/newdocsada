using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace scada_demo_test.Web.Services;

// Attached to the ScadaDemoTestApiClient's HttpClient pipeline (see Program.cs).
// Reads the signed-in user's role and JWT access token off the current circuit's
// AuthenticationStateProvider or HttpContext (with fallback to the latest active
// session token for iframe environments) and forwards them to scada_demo_test.API.
public class PermissionForwardingHandler : DelegatingHandler
{
    public static string? FallbackAccessToken { get; set; }
    public static string? FallbackRole { get; set; }
    public static ClaimsPrincipal? FallbackPrincipal { get; set; }

    private readonly IHttpContextAccessor _accessor;
    private readonly IServiceProvider _serviceProvider;

    public PermissionForwardingHandler(IHttpContextAccessor accessor, IServiceProvider serviceProvider)
    {
        _accessor = accessor;
        _serviceProvider = serviceProvider;
        InnerHandler = new HttpClientHandler { AllowAutoRedirect = false };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? role = null;
        string? token = null;

        var user = _accessor.HttpContext?.User;
        if (user != null && user.Identity?.IsAuthenticated == true)
        {
            role = user.FindFirstValue(ClaimTypes.Role);
            token = user.FindFirstValue("access_token");
        }

        if (string.IsNullOrEmpty(token))
        {
            try
            {
                // Resolve directly from the circuit's IServiceProvider (never CreateScope,
                // which would create an uninitialized AuthenticationStateProvider).
                var authStateProvider = _serviceProvider.GetService<AuthenticationStateProvider>();
                if (authStateProvider != null)
                {
                    var authState = await authStateProvider.GetAuthenticationStateAsync();
                    if (authState.User.Identity?.IsAuthenticated == true)
                    {
                        role ??= authState.User.FindFirstValue(ClaimTypes.Role);
                        token ??= authState.User.FindFirstValue("access_token");
                    }
                }
            }
            catch
            {
                // Circuit not yet initialized; fall back below.
            }
        }

        role ??= FallbackRole;
        token ??= FallbackAccessToken;

        if (!string.IsNullOrEmpty(role))
        {
            request.Headers.Remove("X-Requesting-Role");
            request.Headers.Add("X-Requesting-Role", role);
        }

        if (!string.IsNullOrEmpty(token) && request.Headers.Authorization == null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // Circuit or caller explicitly cancelled request (e.g. browser refreshed or navigated)
            return new HttpResponseMessage((System.Net.HttpStatusCode)499)
            {
                RequestMessage = request,
                Content = new StringContent($"{{\"error\":\"Request was canceled by the client: {ex.Message}\"}}", System.Text.Encoding.UTF8, "application/json")
            };
        }
        catch (OperationCanceledException ex)
        {
            // Request timed out (API on port 5080 did not answer within timeout)
            return new HttpResponseMessage(System.Net.HttpStatusCode.GatewayTimeout)
            {
                RequestMessage = request,
                Content = new StringContent($"{{\"error\":\"API request timed out (verify scada_demo_test.API is running on port 5080): {ex.Message}\"}}", System.Text.Encoding.UTF8, "application/json")
            };
        }
        catch (HttpRequestException ex)
        {
            // API connection refused (port 5080 offline)
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                Content = new StringContent($"{{\"error\":\"API unreachable ({ex.Message}). Start scada_demo_test.API on port 5080.\",\"status\":503}}", System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
