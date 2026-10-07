using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.Infrastructure.Identity;

public static class IdentitySeeder
{
    public const string SuperAdminRole = "SuperAdmin";
    public const string AdminRole = "Admin";
    public const string EmployeeRole = "Employee";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var roleManager = services.GetRequiredService<RoleManager<AppRole>>();
        var config = services.GetRequiredService<IConfiguration>();
        var permissions = services.GetRequiredService<PermissionService>();
        var db = services.GetRequiredService<MyDbContextDxy>();

        // ---- 1. Default roles ----
        await EnsureRoleAsync(roleManager, SuperAdminRole, "Full access to every tab and action, cannot be edited or deleted.");
        await EnsureRoleAsync(roleManager, AdminRole, "Access to everything except managing users/roles.");
        await EnsureRoleAsync(roleManager, EmployeeRole, "Limited access - Monitoring, Charts, Tanks and View-only permissions.");

        // ---- 2. Default permissions ----
        var adminRole = await roleManager.FindByNameAsync(AdminRole);
        if (adminRole != null)
        {
            var current = await permissions.GetGrantedTabsForRoleAsync(adminRole.Id, AdminRole);
            if (current.Count == 0)
            {
                var grants = new Dictionary<string, bool>();
                foreach (var tab in AppTabs.All)
                    grants[tab] = tab != AppTabs.Users;
                foreach (var act in AppPermissions.All)
                    grants[act] = act != AppPermissions.UsersManage && act != AppPermissions.RolesManage;

                await permissions.SetPermissionsAsync(adminRole.Id, grants);
            }
        }

        var employeeRole = await roleManager.FindByNameAsync(EmployeeRole);
        if (employeeRole != null)
        {
            var current = await permissions.GetGrantedTabsForRoleAsync(employeeRole.Id, EmployeeRole);
            if (current.Count == 0)
            {
                var grants = new Dictionary<string, bool>();
                foreach (var tab in AppTabs.All)
                    grants[tab] = tab is AppTabs.Monitoring or AppTabs.Charts or AppTabs.Tanks or AppTabs.Summary or AppTabs.Reports or AppTabs.Devices or AppTabs.Alerts;
                foreach (var act in AppPermissions.All)
                    grants[act] = act is AppPermissions.DevicesView or AppPermissions.ReportsView or AppPermissions.AlertsView; // View only, NO Add/Edit/Delete

                await permissions.SetPermissionsAsync(employeeRole.Id, grants);
            }
        }

        // ---- 3. Hardcoded SuperAdmin ----
        var superAdminEmail = config["SuperAdmin:Email"] ?? "superadmin@alamiot.com";
        var superAdminPassword = config["SuperAdmin:Password"] ?? "SuperAdmin@123";

        var existing = await userManager.FindByEmailAsync(superAdminEmail);
        if (existing == null)
        {
            var superAdmin = new AppUser
            {
                UserName = superAdminEmail,
                Email = superAdminEmail,
                FirstName = "Super",
                LastName = "Admin",
                IsHardcodedSuperAdmin = true,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(superAdmin, superAdminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(superAdmin, SuperAdminRole);
            }
        }

        // ---- 4. Seed Sites (Multi-Site support) ----
        if (!await db.Sites.AnyAsync())
        {
            var sites = new List<Site>
            {
                new() { Id = Guid.NewGuid(), Code = "PLANT-LHE", Name = "Lahore Main Plant #1", Location = "Sundar Industrial Estate, Lahore", Description = "Primary manufacturing and water treatment plant." },
                new() { Id = Guid.NewGuid(), Code = "PLANT-KHI", Name = "Karachi Port Terminal", Location = "Port Qasim, Karachi", Description = "Bulk liquid terminal and distribution hub." },
                new() { Id = Guid.NewGuid(), Code = "PLANT-FSD", Name = "Faisalabad Textile Hub", Location = "Khurrianwala Industrial Area, Faisalabad", Description = "Industrial utility and high-flow monitoring facility." }
            };
            db.Sites.AddRange(sites);
            await db.SaveChangesAsync();
        }

        // ---- 5. Seed Storage Tanks ----
        if (!await db.StorageTanks.AnyAsync())
        {
            var site = await db.Sites.FirstOrDefaultAsync();
            var siteId = site?.Id;

            var tanks = new List<StorageTank>
            {
                new() { Id = Guid.NewGuid(), SiteId = siteId, TankCode = "TK-01", Name = "Raw Water Storage Tank 1", CapacityLiters = 50000, CurrentVolumeLiters = 38500, LevelPercentage = 77.0, TemperatureCelsius = 22.4, Status = "Filling", LiquidType = "Raw Water", InletFlowRate = 145.2, OutletFlowRate = 90.0, LastUpdatedAt = DateTime.UtcNow },
                new() { Id = Guid.NewGuid(), SiteId = siteId, TankCode = "TK-02", Name = "Buffer Equalization Tank", CapacityLiters = 35000, CurrentVolumeLiters = 21200, LevelPercentage = 60.5, TemperatureCelsius = 23.1, Status = "Normal", LiquidType = "Process Water", InletFlowRate = 88.0, OutletFlowRate = 85.5, LastUpdatedAt = DateTime.UtcNow },
                new() { Id = Guid.NewGuid(), SiteId = siteId, TankCode = "TK-03", Name = "Treated Pure Water Reservoir", CapacityLiters = 80000, CurrentVolumeLiters = 67200, LevelPercentage = 84.0, TemperatureCelsius = 20.8, Status = "Filling", LiquidType = "Purified Water", InletFlowRate = 210.0, OutletFlowRate = 180.0, LastUpdatedAt = DateTime.UtcNow },
                new() { Id = Guid.NewGuid(), SiteId = siteId, TankCode = "TK-04", Name = "Effluent Discharge Sump", CapacityLiters = 25000, CurrentVolumeLiters = 7800, LevelPercentage = 31.2, TemperatureCelsius = 28.5, Status = "Draining", LiquidType = "Effluent Water", InletFlowRate = 45.0, OutletFlowRate = 110.0, LastUpdatedAt = DateTime.UtcNow }
            };
            db.StorageTanks.AddRange(tanks);
            await db.SaveChangesAsync();
        }

        // ---- 6. Seed Default Alert Rules ----
        if (!await db.AlertRules.AnyAsync())
        {
            var rules = new List<AlertRule>
            {
                new() { Id = Guid.NewGuid(), DeviceExternalId = "ALL", Metric = "FlowRate", Condition = "GreaterThan", ThresholdValue = 250.0, Severity = "Critical", IsEnabled = true, NotificationEmail = "alerts@alamiot.com" },
                new() { Id = Guid.NewGuid(), DeviceExternalId = "ALL", Metric = "FlowRate", Condition = "LessThan", ThresholdValue = 10.0, Severity = "Warning", IsEnabled = true, NotificationEmail = "alerts@alamiot.com" },
                new() { Id = Guid.NewGuid(), DeviceExternalId = "ALL", Metric = "Pressure", Condition = "GreaterThan", ThresholdValue = 8.5, Severity = "Critical", IsEnabled = true, NotificationEmail = "alerts@alamiot.com" }
            };
            db.AlertRules.AddRange(rules);
            await db.SaveChangesAsync();
        }

        // ---- 7. Seed Firmware Release ----
        if (!await db.FirmwareReleases.AnyAsync())
        {
            var release = new FirmwareRelease
            {
                Id = Guid.NewGuid(),
                Version = "v2.1.0-PROD",
                Description = "High-throughput Modbus RTU gateway with SPIFFS offline ring-buffering and TLS MQTT telemetry.",
                HardwareTarget = "Norvi-ESP32",
                BinaryFileName = "firmware_norvi_v2.1.0.bin",
                FileSizeBytes = 1485920,
                ChecksumSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                ReleaseNotes = "Added offline failover buffer (up to 10,000 readings on flash), dynamic WiFi captive portal provisioning, and Modbus RTU CRC check speedup."
            };
            db.FirmwareReleases.Add(release);
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureRoleAsync(RoleManager<AppRole> roleManager, string name, string description)
    {
        if (await roleManager.FindByNameAsync(name) == null)
        {
            await roleManager.CreateAsync(new AppRole(name) { Description = description });
        }
    }
}
