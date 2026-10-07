using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using scada_demo_test.Domain.Services;

namespace scada_demo_test.Maui.ViewModels;

public class UserDisplayModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = "Operator";
    public bool IsHardcodedSuperAdmin { get; set; }
    public string FormattedDate { get; set; } = string.Empty;
    public string RoleBadgeColor => RoleName switch
    {
        "SuperAdmin" => "#EF4444",
        "Admin" => "#F59E0B",
        "Manager" => "#06B6D4",
        _ => "#10B981"
    };
}

public partial class UsersViewModel : BaseViewModel
{
    private readonly FirebaseScadaService _firebase;
    private List<UserDisplayModel> _rawUsers = new();

    [ObservableProperty]
    private ObservableCollection<UserDisplayModel> _users = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalUsersCount;

    public UsersViewModel(FirebaseScadaService firebase)
    {
        _firebase = firebase;
        Title = "User & Access Management";
    }

    [RelayCommand]
    public async Task LoadUsersAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var users = await _firebase.GetUsersAsync();
            var roles = await _firebase.GetRolesAsync();
            var userRoles = await _firebase.GetUserRolesAsync();

            _rawUsers = users.Select(u =>
            {
                var mapping = userRoles.FirstOrDefault(ur => ur.UserId == u.Id);
                var role = roles.FirstOrDefault(r => r.Id == mapping?.RoleId);
                var roleName = role?.Name ?? (u.IsHardcodedSuperAdmin ? "SuperAdmin" : "Employee");

                return new UserDisplayModel
                {
                    Id = u.Id,
                    FullName = $"{u.FirstName} {u.LastName}".Trim(),
                    Email = u.Email,
                    RoleName = roleName,
                    IsHardcodedSuperAdmin = u.IsHardcodedSuperAdmin,
                    FormattedDate = u.CreatedAt.ToString("MMM dd, yyyy")
                };
            }).ToList();

            ApplyFilter();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Could not load users: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _rawUsers
            : _rawUsers.Where(u =>
                u.FullName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                u.RoleName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        Users.Clear();
        foreach (var user in filtered)
        {
            Users.Add(user);
        }
        TotalUsersCount = _rawUsers.Count;
    }
}
