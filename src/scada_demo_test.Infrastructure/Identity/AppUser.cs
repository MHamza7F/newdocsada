using Microsoft.AspNetCore.Identity;

namespace scada_demo_test.Infrastructure.Identity;

// Extends the standard Identity user with the fields the Users tab needs
// (first/last name) and a hardcoded flag for the one super admin account
// that can never be deleted or demoted, no matter what the Roles/Permissions
// screen is used to change.
public class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsHardcodedSuperAdmin { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string FullName => $"{FirstName} {LastName}".Trim();
}
