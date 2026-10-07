using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using scada_demo_test.Application.Interfaces;
using scada_demo_test.Application.Services;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Drivers;
using scada_demo_test.Domain.Interfaces;
using scada_demo_test.Infrastructure.Identity;
using scada_demo_test.Infrastructure.Modbus;
using scada_demo_test.Infrastructure.Persistence;
using scada_demo_test.Infrastructure.RealTime;
using scada_demo_test.Infrastructure.Rollup;

var builder = WebApplication.CreateBuilder(args);

// Clean console/debug logging without Windows EventLog crash
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ---- Persistence (SQLite) ----
builder.Services.AddDbContext<MyDbContextDxy>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// ---- Identity: Users/Roles/RolePermissions ----
builder.Services.AddIdentity<AppUser, AppRole>(options =>
{
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
})
.AddEntityFrameworkStores<MyDbContextDxy>()
.AddDefaultTokenProviders();

// ---- JWT Authentication ----
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SCADA_ENTERPRISE_SUPER_SECURE_SECRET_KEY_2026_!@#$%^&*()";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "AlamIotScadaApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "AlamIotScadaClients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

// ---- Granular Tab & Action Authorization Policies ----
builder.Services.AddAuthorization(options =>
{
    foreach (var tab in AppTabs.All)
    {
        options.AddPolicy($"Tab:{tab}", policy => policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("IsSuperAdmin", "true") ||
            ctx.User.IsInRole(IdentitySeeder.SuperAdminRole) ||
            ctx.User.HasClaim("perm", tab)));
    }

    foreach (var action in AppPermissions.All)
    {
        options.AddPolicy($"Action:{action}", policy => policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("IsSuperAdmin", "true") ||
            ctx.User.IsInRole(IdentitySeeder.SuperAdminRole) ||
            ctx.User.HasClaim("perm", action)));
    }
});

// ---- Services Registration ----
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<AlertService>();

// ---- Domain <- Infrastructure bindings ----
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<ISensorReadingRepository, SensorReadingRepository>();
builder.Services.AddScoped<IRollupRepository, RollupRepository>();
builder.Services.AddScoped<ISensorRepository, SensorRepository>();
builder.Services.AddScoped<ISensorTelemetryRepository, SensorTelemetryRepository>();

// ---- Application <- Infrastructure bindings ----
builder.Services.AddScoped<ITelemetryIngestService, TelemetryIngestService>();
builder.Services.AddScoped<IPushTelemetryIngestService, PushTelemetryIngestService>();
builder.Services.AddScoped<ITelemetryBroadcaster, SignalRTelemetryBroadcaster>();
builder.Services.AddScoped<IRollupCompressionService, RollupCompressionService>();
builder.Services.AddScoped<IReportQueryService, ReportQueryService>();
builder.Services.AddScoped<ISmartScanService, ModbusScanner>();

// ---- Installed sensor driver library (drives Add-Sensor form + polling) ----
builder.Services.AddSingleton<ISensorDriver, AosongAQ3485Driver>();
builder.Services.AddSingleton<ISensorDriver, ElectromagneticFlowmeterDriver>();
builder.Services.AddSingleton<ISensorDriver, VortexFlowmeterDriver>();
builder.Services.AddSingleton<ISensorDriver, SelecPowerMeterDriver>();

// ---- IIoT Modbus polling engine: polls gateways & streams to per-sensor tables ----
builder.Services.AddHostedService<ModbusPollingHostedService>();

// ---- Tiered rollup/compression pipeline: raw -> hourly -> daily -> monthly ----
builder.Services.AddHostedService<RollupCompressionHostedService>();

// ---- Web / Real-time / API ----
builder.Services.AddSignalR();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// ---- Database Migration & Seed Data on Startup ----
// The platform must never refuse to boot just because the cloud DB was temporarily
// unreachable: apply MigrateAsync + seed + additive schema init now, and if they
// fail, keep retrying in the background until the database answers. The Modbus
// worker is offline-tolerant, so the API can already start serving in between.
var dbInitialized = false;
using (var scope = app.Services.CreateScope())
{
    try
    {
        await InitializeDatabaseAsync(scope.ServiceProvider);
        dbInitialized = true;
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Database unavailable at startup; the API will keep running and retry schema initialization in the background.");
    }
}

if (!dbInitialized)
{
    var lifetime = app.Lifetime;
    _ = Task.Run(async () =>
    {
        try
        {
            while (true)
            {
                var stopping = lifetime.ApplicationStopping;
                await Task.Delay(TimeSpan.FromSeconds(30), stopping);

                try
                {
                    using var scope = app.Services.CreateScope();
                    await InitializeDatabaseAsync(scope.ServiceProvider);
                    app.Logger.LogInformation("Database is reachable - migrations, seed and schema initialization completed.");
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    app.Logger.LogWarning(ex, "Database still unreachable; retrying schema initialization in 30s.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutting down.
        }
    });
}

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    var db = services.GetRequiredService<MyDbContextDxy>();
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInit");

    try
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            // MigrateAsync alone handles both a fresh database and an already-migrated
            // one. The old Supabase-era "stamp legacy history" baseline is gone: it
            // inserted InitialPostgres into __EFMigrationsHistory on every boot where
            // AlertIncidents existed, which polluted the history on the local DB.
            await db.Database.MigrateAsync();
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "MigrateAsync could not apply pending migrations. Falling back to additive schema initialization.");
    }

    // Idempotent additive schema upgrade for the IIoT platform (new columns on
    // Devices/Sensors + the missing Sensors table on legacy databases).
    await DynamicSchemaInitializer.EnsureSchemaAsync(services);

    await IdentitySeeder.SeedAsync(services);
}

// Global safety net for unhandled exceptions
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(feature?.Error, "Unhandled exception on {Path}", context.Request.Path);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong. Please try again." });
    });
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<LiveTelemetryHub>("/hubs/telemetry");

app.Run();
