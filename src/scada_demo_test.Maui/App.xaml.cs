using scada_demo_test.Maui.Services;
using scada_demo_test.Maui.Views;

namespace scada_demo_test.Maui;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAuthService _authService;

    public App(IServiceProvider serviceProvider, IAuthService authService)
    {
        MauiDiag.Info("App.ctor: enter");
        try
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;
            _authService = authService;
            MauiDiag.Info("App.ctor: InitializeComponent done");
        }
        catch (System.Exception ex)
        {
            MauiDiag.Error("App.ctor FAILED: " + ex);
            throw;
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        MauiDiag.Info("CreateWindow: enter");
        try
        {
            _ = SafeInitAsync();

            var isAuthed = _authService.IsAuthenticated;
            MauiDiag.Info($"CreateWindow: IsAuthenticated={isAuthed}");

            if (isAuthed)
            {
                MauiDiag.Info("CreateWindow: returning AppShell window");
                return new Window(new AppShell());
            }

            MauiDiag.Info("CreateWindow: returning LoginPage window");
            var loginPage = _serviceProvider.GetRequiredService<LoginPage>();
            return new Window(new NavigationPage(loginPage));
        }
        catch (System.Exception ex)
        {
            MauiDiag.Error("CreateWindow FAILED: " + ex);
            return new Window(new NavigationPage(new ContentPage
            {
                Content = new Label
                {
                    Text = "Startup error. Check logcat (MAUI_DIAG).\n\n" + ex.Message,
                    Padding = 24,
                    FontSize = 14
                }
            }));
        }
    }

    private async System.Threading.Tasks.Task SafeInitAsync()
    {
        try
        {
            await _authService.InitializeAsync();
            MauiDiag.Info("SafeInitAsync: done");
        }
        catch (System.Exception ex)
        {
            MauiDiag.Error("SafeInitAsync FAILED: " + ex);
        }
    }
}