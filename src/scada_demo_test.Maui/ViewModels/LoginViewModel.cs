using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Maui.Services;

namespace scada_demo_test.Maui.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _authService;

    [ObservableProperty]
    private string _email = "superadmin@alamiot.com";

    [ObservableProperty]
    private string _password = "SuperAdmin@123";

    [ObservableProperty]
    private bool _isPasswordHidden = true;

    [ObservableProperty]
    private bool _rememberMe = true;

    [ObservableProperty]
    private string _firebaseStatus = "Connected to Firebase RTDB";

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordHidden = !IsPasswordHidden;
    }

    [RelayCommand]
    private void QuickFillAdmin()
    {
        Email = "superadmin@alamiot.com";
        Password = "SuperAdmin@123";
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        MauiDiag.Info($"LoginViewModel.LoginAsync: pressed with email='{Email}'");

        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Please enter your email address.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter your password.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            MauiDiag.Info("LoginViewModel: calling _authService.LoginAsync");
            var (success, error) = await _authService.LoginAsync(Email.Trim(), Password, RememberMe);
            MauiDiag.Info($"LoginViewModel: auth result success={success}, error='{error}'");

            if (!success)
            {
                ErrorMessage = error ?? "Invalid email or password.";
                return;
            }

            // Successfully authenticated -> Transition to AppShell
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    MauiDiag.Info("LoginViewModel: creating and navigating to AppShell");
                    var shell = new AppShell();
                    if (Application.Current?.Windows.Count > 0)
                    {
                        Application.Current.Windows[0].Page = shell;
                    }
                    else if (Application.Current != null)
                    {
#pragma warning disable CS0618
                        Application.Current.MainPage = shell;
#pragma warning restore CS0618
                    }
                    MauiDiag.Info("LoginViewModel: AppShell navigation succeeded");
                }
                catch (Exception ex)
                {
                    MauiDiag.Error("LoginViewModel: navigate to AppShell FAILED: " + ex);
                    ErrorMessage = $"Navigation error: {ex.Message}";
                }
            });
        }
        catch (Exception ex)
        {
            MauiDiag.Error("LoginViewModel.LoginAsync FAILED: " + ex);
            ErrorMessage = $"Login failure: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
