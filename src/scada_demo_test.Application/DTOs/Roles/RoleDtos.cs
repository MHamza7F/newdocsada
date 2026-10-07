namespace scada_demo_test.Application.DTOs.Roles;

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSuperAdminRole { get; set; }
    public int UserCount { get; set; }
    public Dictionary<string, bool> Permissions { get; set; } = new(); // tabKey -> granted
}

public class CreateRoleDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateRolePermissionsDto
{
    public Dictionary<string, bool> Permissions { get; set; } = new();
}
