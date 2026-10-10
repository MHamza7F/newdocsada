using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Infrastructure.Identity;

namespace scada_demo_test.API.Filters;

// Check the authenticated JWT identity, never a caller-supplied role header.
// Preserve the live role-permission lookup and the existing SuperAdmin bypass.
public class RequireUsersTabAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
            return;
        }

        if (user.IsInRole(IdentitySeeder.SuperAdminRole) || user.HasClaim("IsSuperAdmin", "true"))
        {
            await next();
            return;
        }

        var roleName = user.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(roleName))
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
            return;
        }

        var roleManager = context.HttpContext.RequestServices.GetRequiredService<RoleManager<AppRole>>();
        var permissions = context.HttpContext.RequestServices.GetRequiredService<PermissionService>();

        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
            return;
        }

        var granted = await permissions.GetGrantedTabsForRoleAsync(role.Id, roleName);
        if (!granted.Contains(AppTabs.Users))
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
            return;
        }

        await next();
    }
}
