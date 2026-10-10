using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
// Resolve once per request/circuit. GetTokens only reads the cookie; it does not
// try to set response headers from an interactive Blazor render.
builder.Services.AddScoped<AntiforgeryTokenSet>(sp =>
    sp.GetRequiredService<IAntiforgery>().GetTokens(
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext
        ?? throw new InvalidOperationException("An initial HTTP context is required for account forms.")));
builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(20);
});
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(3);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<WebAuthSessionStore>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = false;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Events.OnValidatePrincipal = async context =>
        {
            var sessions = context.HttpContext.RequestServices.GetRequiredService<WebAuthSessionStore>();
            if (sessions.Find(context.Principal) == null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var tab in AppTabs.All)
    {
        options.AddPolicy($"Tab:{tab}", policy => policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
            ctx.User.HasClaim("IsSuperAdmin", "true") ||
            ctx.User.IsInRole("SuperAdmin") ||
            ctx.User.HasClaim("perm", tab)));
    }

    foreach (var action in AppPermissions.All)
    {
        options.AddPolicy($"Action:{action}", policy => policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
            ctx.User.HasClaim("IsSuperAdmin", "true") ||
            ctx.User.IsInRole("SuperAdmin") ||
            ctx.User.HasClaim("perm", action)));
    }
});
builder.Services.AddCascadingAuthenticationState();

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5080";
var baseUri = new Uri(apiBaseUrl.TrimEnd('/') + "/");

static HttpClient BuildApiClient(IServiceProvider sp, Uri baseAddress, TimeSpan timeout)
{
    var handler = new PermissionForwardingHandler(
        sp.GetRequiredService<IHttpContextAccessor>(),
        sp,
        sp.GetRequiredService<WebAuthSessionStore>());
    return new HttpClient(handler, disposeHandler: true)
    {
        BaseAddress = baseAddress,
        Timeout = timeout
    };
}

// Circuit-scoped ScadaDemoTestApiClient (AGENTS.md Item 39): each HttpClient gets
// its own fresh PermissionForwardingHandler resolved from the circuit's own
// IServiceProvider so AuthenticationStateProvider is always valid.
builder.Services.AddScoped<ScadaDemoTestApiClient>(sp =>
    new ScadaDemoTestApiClient(
        BuildApiClient(sp, baseUri, TimeSpan.FromSeconds(30)),
        BuildApiClient(sp, baseUri, TimeSpan.FromMinutes(5))));

builder.Services.AddSingleton<ITelemetryBroadcastBus, TelemetryBroadcastBus>();
builder.Services.AddScoped<LiveTelemetryState>();

// Load the static-web-assets manifest so wwwroot/_framework/blazor.server.js and
// site.css are served even when the .exe is launched directly (ASPNETCORE_ENVIRONMENT
// not set -> Production, where the manifest is NOT loaded automatically). Guarded by
// the manifest file actually existing so a source-only checkout never 500s.
var staticManifestPath = Path.Combine(
    AppContext.BaseDirectory,
    builder.Environment.ApplicationName + ".staticwebassets.runtime.json");
if (File.Exists(staticManifestPath))
{
    builder.WebHost.UseStaticWebAssets();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

// The classic ServerPrerendered host must issue the cookie before rendering.
// The subsequent circuit request carries that cookie, so its scoped token also
// matches it even when a form (such as the logout menu) is rendered later.
app.Use(async (http, next) =>
{
    if (HttpMethods.IsGet(http.Request.Method) &&
        !http.Request.Path.StartsWithSegments("/_blazor"))
    {
        http.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http);
    }
    await next();
});

// ---- Local Login Endpoint with JWT & RememberMe Support ----
app.MapPost("/account/login", async (HttpContext http, IAntiforgery antiforgery, ScadaDemoTestApiClient api, WebAuthSessionStore sessions) =>
{
    // Validate before credentials, session creation, or any API request.
    if (!await antiforgery.IsRequestValidAsync(http))
        return Results.BadRequest("Invalid account form. Reload the page and try again.");

    var form = await http.Request.ReadFormAsync(http.RequestAborted);
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var rememberMe = form["rememberMe"] == "on" || form["rememberMe"] == "true";

    var (success, data, error) = await api.LoginAsync(email, password, rememberMe);
    if (!success || data == null)
    {
        // API transport/parse failures may contain exception details or internal
        // addresses. Do not put those into production redirects or page markup.
        var message = app.Environment.IsDevelopment()
            ? error ?? "Invalid email or password."
            : "Sign-in failed. Check your credentials or try again later.";
        return Results.Redirect("/login?error=" + Uri.EscapeDataString(message));
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, data.UserId.ToString()),
        new(ClaimTypes.Name, $"{data.FirstName} {data.LastName}".Trim()),
        new(ClaimTypes.Email, data.Email),
        new(ClaimTypes.Role, data.RoleName),
        new("IsSuperAdmin", data.IsSuperAdmin ? "true" : "false"),
        new(WebAuthSessionStore.SessionClaim, sessions.Create(data))
    };

    claims.AddRange(data.Permissions.Select(p => new Claim("perm", p)));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
    {
        IsPersistent = rememberMe,
        ExpiresUtc = new DateTimeOffset(DateTime.SpecifyKind(data.RefreshTokenExpiry, DateTimeKind.Utc))
    });

    return Results.Redirect("/dashboard");
});

app.MapPost("/account/logout", async (HttpContext http, IAntiforgery antiforgery, ScadaDemoTestApiClient api, WebAuthSessionStore sessions) =>
{
    if (!await antiforgery.IsRequestValidAsync(http))
        return Results.BadRequest("Invalid account form. Reload the page and try again.");

    var session = sessions.Find(http.User);
    if (session != null)
    {
        await session.Gate.WaitAsync(http.RequestAborted);
        try
        {
            sessions.Remove(http.User);
            await api.LogoutAsync(session.Tokens.AccessToken, session.Tokens.RefreshToken);
        }
        finally { session.Gate.Release(); }
    }
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
