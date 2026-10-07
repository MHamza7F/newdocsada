using System.Text.Json;
using scada_demo_test.Domain.Services;

namespace scada_demo_test.Maui.Services;

public class AuthService : IAuthService
{
    private const string PrefKeyUser = "scada_auth_user";
    private const string PrefKeyRemember = "scada_auth_remember";

    private readonly FirebaseScadaService _firebase;
    private AuthenticatedUserInfo? _currentUser;

    public AuthService(FirebaseScadaService firebase)
    {
        MauiDiag.Info("AuthService.ctor: start");
        _firebase = firebase;
        MauiDiag.Info("AuthService.ctor: done");
    }

    public bool IsAuthenticated => _currentUser != null;
    public AuthenticatedUserInfo? CurrentUser => _currentUser;
    public string DisplayName => _currentUser != null
        ? $"{_currentUser.FirstName} {_currentUser.LastName}".Trim()
        : "Operator";
    public string CurrentRole => _currentUser?.RoleName ?? "Guest";
    public string CurrentEmail => _currentUser?.Email ?? "";
    public bool IsSuperAdmin => _currentUser?.IsSuperAdmin ?? false;

    public event System.Action? AuthStateChanged;

    public async System.Threading.Tasks.Task InitializeAsync()
    {
        MauiDiag.Info("AuthService.InitializeAsync: start");
        try
        {
            var remember = Preferences.Default.Get<bool>(PrefKeyRemember, false);
            MauiDiag.Info($"AuthService.InitializeAsync: remember={remember}");
            if (remember)
            {
                var cached = Preferences.Default.Get<string?>(PrefKeyUser, null);
                if (!string.IsNullOrEmpty(cached))
                {
                    _currentUser = JsonSerializer.Deserialize<AuthenticatedUserInfo>(cached);
                    MauiDiag.Info(
                        $"AuthService.InitializeAsync: loaded cached user " +
                        $"{(_currentUser != null ? _currentUser.Email : "null")}");
                    AuthStateChanged?.Invoke();
                }
            }
        }
        catch (System.Exception ex)
        {
            MauiDiag.Error("AuthService.InitializeAsync FAILED: " + ex);
            _currentUser = null;
        }
        MauiDiag.Info("AuthService.InitializeAsync: end");
        await System.Threading.Tasks.Task.CompletedTask;
    }

    public async System.Threading.Tasks.Task<(bool Success, string? Error)> LoginAsync(
        string email, string password, bool rememberMe = true)
    {
        try
        {
            var (success, user, error) =
                await _firebase.AuthenticateAsync(email, password, rememberMe);
            if (!success || user == null)
            {
                return (false, error ?? "Invalid credentials. Please verify your email and password.");
            }

            _currentUser = user;

            Preferences.Default.Set(PrefKeyRemember, rememberMe);
            if (rememberMe)
            {
                var json = JsonSerializer.Serialize(user);
                Preferences.Default.Set(PrefKeyUser, json);
            }
            else
            {
                Preferences.Default.Remove(PrefKeyUser);
            }

            AuthStateChanged?.Invoke();
            return (true, null);
        }
        catch (System.Exception ex)
        {
            MauiDiag.Error("AuthService.LoginAsync FAILED: " + ex);
            return (false, $"Login failed: {ex.Message}");
        }
    }

    public async System.Threading.Tasks.Task LogoutAsync()
    {
        _currentUser = null;
        Preferences.Default.Remove(PrefKeyUser);
        Preferences.Default.Set(PrefKeyRemember, false);
        AuthStateChanged?.Invoke();
        await System.Threading.Tasks.Task.CompletedTask;
    }

    public bool HasPermission(string permission)
    {
        if (_currentUser == null) return false;
        if (_currentUser.IsSuperAdmin) return true;
        if (string.Equals(_currentUser.RoleName, "SuperAdmin", System.StringComparison.OrdinalIgnoreCase))
            return true;

        return _currentUser.Permissions.Contains("*") ||
               _currentUser.Permissions.Contains(permission, System.StringComparer.OrdinalIgnoreCase);
    }
}