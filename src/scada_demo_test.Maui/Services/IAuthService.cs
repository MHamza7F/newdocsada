using scada_demo_test.Domain.Services;

namespace scada_demo_test.Maui.Services;

public interface IAuthService
{
    bool IsAuthenticated { get; }
    AuthenticatedUserInfo? CurrentUser { get; }
    string DisplayName { get; }
    string CurrentRole { get; }
    string CurrentEmail { get; }
    bool IsSuperAdmin { get; }

    event Action? AuthStateChanged;

    Task<(bool Success, string? Error)> LoginAsync(string email, string password, bool rememberMe = true);
    Task LogoutAsync();
    bool HasPermission(string permission);
    Task InitializeAsync();
}
