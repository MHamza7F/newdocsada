using Microsoft.AspNetCore.Identity;

namespace scada_demo_test.Infrastructure.Identity;

// Roles are fully dynamic - SuperAdmin/Admin/Employee are just the 3 seeded on
// first run. New roles can be created from the Users tab any time; they just
// start with zero RolePermission rows (no tab access) until someone grants some.
public class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }

    public AppRole() { }
    public AppRole(string name) : base(name) { }
}
