using Microsoft.Extensions.Logging;
using scada_demo_test.Domain.Services;
using scada_demo_test.Maui.Services;
using scada_demo_test.Maui.ViewModels;
using scada_demo_test.Maui.Views;

namespace scada_demo_test.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // ====== SAFE crash handlers (must NEVER throw themselves) ======
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var msg = e.ExceptionObject?.ToString() ?? "Unknown crash";
                MauiDiag.Error("UNHANDLED", msg);

                string dir;
                try
                {
#if ANDROID
                    dir = Android.App.Application.Context?.FilesDir?.AbsolutePath
                          ?? System.IO.Path.GetTempPath();
#else
                    dir = System.IO.Path.GetTempPath();
#endif
                }
                catch { dir = System.IO.Path.GetTempPath(); }

                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "crash.txt");
                System.IO.File.WriteAllText(path,
                    $"[{DateTime.UtcNow:o}] UNHANDLED\n{msg}\n");
            }
            catch { /* never throw from a handler */ }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try
            {
                var msg = e.Exception?.ToString() ?? "Unknown task crash";
                MauiDiag.Error("TASK", msg);

                string dir;
                try
                {
#if ANDROID
                    dir = Android.App.Application.Context?.FilesDir?.AbsolutePath
                          ?? System.IO.Path.GetTempPath();
#else
                    dir = System.IO.Path.GetTempPath();
#endif
                }
                catch { dir = System.IO.Path.GetTempPath(); }

                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "crash.txt");
                System.IO.File.AppendAllText(path,
                    $"\n--- TaskException {DateTime.UtcNow:o} ---\n{msg}\n");
            }
            catch { }
            finally { e.SetObserved(); }
        };

        MauiDiag.Info("CreateMauiApp: start");

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // 1. Firebase + Core Services
        builder.Services.AddSingleton<FirebaseScadaConfig>();
        builder.Services.AddSingleton<FirebaseScadaService>();
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IMockTelemetryService, MockTelemetryService>();
        builder.Services.AddSingleton<IScadaApiService, ScadaApiService>();
        builder.Services.AddSingleton<ITelemetryCoordinator, TelemetryCoordinator>();

        // 2. ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<SitesViewModel>();
        builder.Services.AddSingleton<StorageTanksViewModel>();
        builder.Services.AddSingleton<DevicesViewModel>();
        builder.Services.AddSingleton<AnalyticsViewModel>();
        builder.Services.AddSingleton<AlertsViewModel>();
        builder.Services.AddSingleton<AuditLogsViewModel>();
        builder.Services.AddSingleton<UsersViewModel>();
        builder.Services.AddSingleton<LogsViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // 3. Views
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<SitesPage>();
        builder.Services.AddTransient<StorageTanksPage>();
        builder.Services.AddTransient<DevicesPage>();
        builder.Services.AddTransient<AnalyticsPage>();
        builder.Services.AddTransient<AlertsPage>();
        builder.Services.AddTransient<AuditLogsPage>();
        builder.Services.AddTransient<UsersPage>();
        builder.Services.AddTransient<LogsPage>();
        builder.Services.AddTransient<SettingsPage>();

        MauiDiag.Info("CreateMauiApp: services registered");

        var app = builder.Build();
        MauiDiag.Info("CreateMauiApp: built");
        return app;
    }
}