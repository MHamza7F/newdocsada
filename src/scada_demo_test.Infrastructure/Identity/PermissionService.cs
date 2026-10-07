using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.Infrastructure.Identity;

public class PermissionService
{
    private readonly MyDbContextDxy _db;

    public PermissionService(MyDbContextDxy db) => _db = db;

    public async Task<List<string>> GetGrantedTabsForRoleAsync(Guid roleId, string roleName)
    {
        if (string.Equals(roleName, IdentitySeeder.SuperAdminRole, StringComparison.OrdinalIgnoreCase))
            return AppTabs.All.Concat(AppPermissions.All).ToList();

        return await _db.RolePermissions
            .Where(p => p.RoleId == roleId.ToString() && p.IsGranted)
            .Select(p => p.TabKey)
            .ToListAsync();
    }

    public async Task<Dictionary<string, bool>> GetPermissionMapForRoleAsync(Guid roleId, string roleName)
    {
        var granted = await GetGrantedTabsForRoleAsync(roleId, roleName);
        var allKeys = AppTabs.All.Concat(AppPermissions.All).Distinct().ToList();
        return allKeys.ToDictionary(t => t, t => granted.Contains(t));
    }

    public async Task SetPermissionsAsync(Guid roleId, Dictionary<string, bool> grants)
    {
        var roleIdStr = roleId.ToString();
        var existing = await _db.RolePermissions.Where(p => p.RoleId == roleIdStr).ToListAsync();
        _db.RolePermissions.RemoveRange(existing);

        foreach (var (key, isGranted) in grants)
        {
            if (isGranted)
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = roleIdStr,
                    TabKey = key,
                    IsGranted = true
                });
            }
        }

        await _db.SaveChangesAsync();
    }
}
