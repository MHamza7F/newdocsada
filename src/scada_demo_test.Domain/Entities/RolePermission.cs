namespace scada_demo_test.Domain.Entities;

// One row = "this role can see this tab". Super Admin bypasses this table entirely
// (checked in code via AppUser.IsHardcodedSuperAdmin / role name == SuperAdmin) so it
// can never be locked out even if every row for its role got deleted by mistake.
// RoleId is a string (Guid.ToString()) so this entity has zero dependency on the
// Identity package types - Domain stays clean, Infrastructure wires the FK.
public class RolePermission
{
    public Guid Id { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public string TabKey { get; set; } = string.Empty; // matches scada_demo_test.Domain.Constants.AppTabs
    public bool IsGranted { get; set; } = true;
}
