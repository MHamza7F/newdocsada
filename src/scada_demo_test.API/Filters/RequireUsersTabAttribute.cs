using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Infrastructure.Identity;

namespace scada_demo_test.API.Filters;

// The Web app is a separate Blazor Server process with its own cookie - it
// can't share that cookie with this API out of the box, so on every call to
// a Users/Roles endpoint it forwards the caller's role name in the
// X-Requesting-Role header (see scada_demo_test.Web.Services.PermissionForwardingHandler).
// This filter re-checks that role against the RolePermission table (or the
// SuperAdmin bypass) before letting the request through.
//
// NOTE for later hardening: this trusts the header, which is fine on a private
// plant network but should become a signed JWT once this is exposed publicly.
public class RequireUsersTabAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var roleName = context.HttpContext.Request.Headers["X-Requesting-Role"].ToString();

        if (string.IsNullOrWhiteSpace(roleName))
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult(new { message = "Missing caller role." });
            return;
        }

        if (roleName == IdentitySeeder.SuperAdminRole)
        {
            await next();
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
