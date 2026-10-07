using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Services;
using scada_demo_test.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(20);
});
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    options.EnableDetailedErrors = true;
});

builder.Services.AddHttpContextAccessor();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        if (!OperatingSystem.IsWindows())
        {
            options.Cookie.SameSite = SameSiteMode.None;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var tab in AppTabs.All)
    {
        options.AddPolicy($"Tab:{tab}", policy => policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("IsSuperAdmin", "true") ||
            ctx.User.IsInRole("SuperAdmin") ||
            ctx.User.HasClaim("perm", tab)));
    }

    foreach (var action in AppPermissions.All)
    {
        options.AddPolicy($"Action:{action}", policy => policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("IsSuperAdmin", "true") ||
            ctx.User.IsInRole("SuperAdmin") ||
            ctx.User.HasClaim("perm", action)));
    }
});
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton<FirebaseScadaConfig>();
builder.Services.AddSingleton<FirebaseScadaService>();

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5080";
var baseUri = new Uri(apiBaseUrl.TrimEnd('/') + "/");

static HttpClient BuildApiClient(IServiceProvider sp, Uri baseAddress, TimeSpan timeout)
{
    var handler = new PermissionForwardingHandler(
        sp.GetRequiredService<IHttpContextAccessor>(),
        sp);
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
        BuildApiClient(sp, baseUri, TimeSpan.FromMinutes(5)),
        sp.GetRequiredService<FirebaseScadaService>()));

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

// In non-Windows cloud preview (iframe where 3P cookies may be blocked by the browser),
// fall back to the active signed-in session principal once login has succeeded.
if (!OperatingSystem.IsWindows())
{
    app.Use(async (ctx, next) =>
    {
        if (ctx.User.Identity?.IsAuthenticated != true && PermissionForwardingHandler.FallbackPrincipal != null)
        {
            ctx.User = PermissionForwardingHandler.FallbackPrincipal;
        }
        await next();
    });
}

app.UseAuthorization();

// ---- Local Login Endpoint with JWT & RememberMe Support ----
app.MapPost("/account/login", async (HttpContext http, IFormCollection form, ScadaDemoTestApiClient api) =>
{
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var rememberMe = form["rememberMe"] == "on" || form["rememberMe"] == "true";

    var (success, data, error) = await api.LoginAsync(email, password, rememberMe);
    if (!success || data == null)
    {
        return Results.Redirect("/login?error=" + Uri.EscapeDataString(error ?? "Invalid email or password."));
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, data.UserId.ToString()),
        new(ClaimTypes.Name, $"{data.FirstName} {data.LastName}".Trim()),
        new(ClaimTypes.Email, data.Email),
        new(ClaimTypes.Role, data.RoleName),
        new("IsSuperAdmin", data.IsSuperAdmin ? "true" : "false"),
        new("access_token", data.AccessToken),
        new("refresh_token", data.RefreshToken)
    };

    claims.AddRange(data.Permissions.Select(p => new Claim("perm", p)));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    PermissionForwardingHandler.FallbackAccessToken = data.AccessToken;
    PermissionForwardingHandler.FallbackRole = data.RoleName;
    PermissionForwardingHandler.FallbackPrincipal = principal;

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
    {
        IsPersistent = rememberMe,
        ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null
    });

    return Results.Redirect("/dashboard");
}).DisableAntiforgery();

app.MapGet("/account/logout", async (HttpContext http) =>
{
    PermissionForwardingHandler.FallbackAccessToken = null;
    PermissionForwardingHandler.FallbackRole = null;
    PermissionForwardingHandler.FallbackPrincipal = null;
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
