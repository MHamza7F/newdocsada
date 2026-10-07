using scada_demo_test.Maui.Services;
using scada_demo_test.Maui.ViewModels;
using scada_demo_test.Maui.Views;

namespace scada_demo_test.Maui;

public partial class AppShell : Shell
{
    private IAuthService? _authService;

    public AppShell()
    {
        MauiDiag.Info("AppShell.ctor: enter");
        try
        {
            InitializeComponent();
            MauiDiag.Info("AppShell.ctor: InitializeComponent done");

            _authService = Handler?.MauiContext?.Services.GetService<IAuthService>()
                ?? Application.Current?.Handler?.MauiContext?.Services.GetService<IAuthService>();
            MauiDiag.Info($"AppShell.ctor: auth resolved = {(_authService != null ? "yes" : "no")}");

            UpdateHeaderDetails();

            Routing.RegisterRoute(nameof(DashboardPage), typeof(DashboardPage));
            Routing.RegisterRoute(nameof(SitesPage), typeof(SitesPage));
            Routing.RegisterRoute(nameof(StorageTanksPage), typeof(StorageTanksPage));
            Routing.RegisterRoute(nameof(DevicesPage), typeof(DevicesPage));
            Routing.RegisterRoute(nameof(AnalyticsPage), typeof(AnalyticsPage));
            Routing.RegisterRoute(nameof(AlertsPage), typeof(AlertsPage));
            Routing.RegisterRoute(nameof(AuditLogsPage), typeof(AuditLogsPage));
            Routing.RegisterRoute(nameof(UsersPage), typeof(UsersPage));
            Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));

            MauiDiag.Info("AppShell.ctor: routes registered");
        }
        catch (System.Exception ex)
        {
            MauiDiag.Error("AppShell.ctor FAILED: " + ex);
            throw;
        }
    }

    private void UpdateHeaderDetails()
    {
        try
        {
            if (_authService != null && _authService.IsAuthenticated)
            {
                LblUserName.Text = _authService.DisplayName;
                LblUserEmail.Text = _authService.CurrentEmail;
                LblUserRole.Text = _authService.CurrentRole;
            }
        }
        catch (System.Exception ex)
        {
            MauiDiag.Warn("AppShell.UpdateHeaderDetails: " + ex.Message);
        }
    }

    private async void OnSignOutClicked(object? sender, System.EventArgs e)
    {
        try
        {
            bool confirm = await DisplayAlert(
                "Sign Out",
                "Are you sure you want to sign out of the SCADA Portal?",
                "Sign Out", "Cancel");
            if (!confirm) return;

            if (_authService != null)
                await _authService.LogoutAsync();

            var loginPage = Handler?.MauiContext?.Services.GetService<LoginPage>()
                ?? Application.Current?.Handler?.MauiContext?.Services.GetService<LoginPage>();

            if (Application.Current == null) return;

            var targetPage = loginPage != null
                ? new NavigationPage(loginPage)
                : new NavigationPage(new LoginPage(new LoginViewModel(_authService!)));

            if (Application.Current.Windows.Count > 0)
                Application.Current.Windows[0].Page = targetPage;
            else
            {
#pragma warning disable CS0618
                Application.Current.MainPage = targetPage;
#pragma warning restore CS0618
            }
        }
        catch (System.Exception ex)
        {
            MauiDiag.Error("AppShell.OnSignOutClicked FAILED: " + ex);
        }
    }
}