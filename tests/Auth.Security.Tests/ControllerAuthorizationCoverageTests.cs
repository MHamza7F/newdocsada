using System.Text.RegularExpressions;
using scada_demo_test.Domain.Constants;
using Xunit;

namespace Auth.Security.Tests;

// Source-level architecture checks deliberately avoid starting the API (which starts
// database seeding and hardware workers). These complement, not replace, HTTP tests.
public class ControllerAuthorizationCoverageTests
{
    private static readonly string Controllers = FindControllers();
    // Keep the inventory independent of whether an action returns IActionResult,
    // ActionResult<T>, or a nested generic Task<ActionResult<T>>.
    private static readonly Regex Methods = new(
        @"public\s+(?!record\b)(?:async\s+)?[^\r\n{;]+?\s+(?<name>\w+)\s*\(");
    private static readonly Regex Attributes = new(
        @"(?<attributes>(?:^[ \t]*\[[^\r\n]*\][ \t]*\r?\n)+)[ \t]*public\s+(?!record\b)(?:async\s+)?[^\r\n{;]+?\s+(?<name>\w+)\s*\(",
        RegexOptions.Multiline);

    [Fact]
    public void EveryControllerActionHasAuthorizationExceptExplicitProtocolExceptions()
    {
        var exceptions = new HashSet<string>
        {
            "AuthController.Login", "AuthController.Refresh", "AuthController.Logout",
            // Legacy hardware sends no credential; retain this blocker explicitly.
            "TelemetryController.TankReading"
        };
        var seenExceptions = new HashSet<string>();
        var files = Directory.GetFiles(Controllers, "*Controller.cs");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var controller = Path.GetFileNameWithoutExtension(file);
            var classMatch = Regex.Match(source, @"\bpublic\s+(?:sealed\s+|abstract\s+)?class\s+\w+");
            Assert.True(classMatch.Success, $"Could not locate controller declaration in {file}.");
            var classAttributes = source[..classMatch.Index];
            var actions = Attributes.Matches(source);
            // Do not silently skip a newly-added action without an attribute block.
            Assert.Equal(Methods.Matches(source).Count, actions.Count);
            foreach (Match action in actions)
            {
                var name = controller + "." + action.Groups["name"].Value;
                if (exceptions.Contains(name))
                {
                    seenExceptions.Add(name);
                    continue;
                }
                var effective = classAttributes + action.Groups["attributes"].Value;
                Assert.DoesNotContain("[AllowAnonymous", effective);
                Assert.True(effective.Contains("[Authorize", StringComparison.Ordinal),
                    $"{name} has no authorization boundary.");
                if (controller != "AuthController")
                    Assert.Contains("AppPermissions.", effective);
            }
        }
        Assert.True(exceptions.SetEquals(seenExceptions), "Review removed/renamed protocol exceptions.");
    }

    [Theory]
    [InlineData("Users", "GetAll", nameof(AppPermissions.UsersManage))]
    [InlineData("Users", "Create", nameof(AppPermissions.UsersManage))]
    [InlineData("Users", "Update", nameof(AppPermissions.UsersManage))]
    [InlineData("Users", "Delete", nameof(AppPermissions.UsersManage))]
    [InlineData("Roles", "GetAll", nameof(AppPermissions.RolesManage))]
    [InlineData("Roles", "Create", nameof(AppPermissions.RolesManage))]
    [InlineData("Roles", "UpdatePermissions", nameof(AppPermissions.RolesManage))]
    [InlineData("Roles", "Delete", nameof(AppPermissions.RolesManage))]
    [InlineData("Devices", "GetAll", nameof(AppPermissions.DevicesView))]
    [InlineData("Devices", "GetOne", nameof(AppPermissions.DevicesView))]
    [InlineData("Sites", "GetAll", nameof(AppPermissions.SitesView))]
    [InlineData("AuditLogs", "GetLogs", nameof(AppPermissions.AuditLogsView))]
    [InlineData("Tanks", "GetAll", nameof(AppPermissions.DevicesView))]
    [InlineData("Tanks", "GetById", nameof(AppPermissions.DevicesView))]
    [InlineData("Tanks", "Create", nameof(AppPermissions.DevicesAdd))]
    [InlineData("Tanks", "Update", nameof(AppPermissions.DevicesEdit))]
    [InlineData("Tanks", "Delete", nameof(AppPermissions.DevicesDelete))]
    [InlineData("Sensors", "GetLibraries", nameof(AppPermissions.DevicesView))]
    [InlineData("Sensors", "Update", nameof(AppPermissions.DevicesEdit))]
    [InlineData("Alerts", "DeleteIncident", nameof(AppPermissions.AlertsConfigure))]
    [InlineData("Reports", "GetHistory", nameof(AppPermissions.ReportsView))]
    [InlineData("Reports", "GenerateRangeReport", nameof(AppPermissions.ReportsExportPdf))]
    [InlineData("Reports", "GenerateReport", nameof(AppPermissions.ReportsExportPdf))]
    [InlineData("Reports", "ExportHistoryCsv", nameof(AppPermissions.ReportsExportExcel))]
    [InlineData("Sensors", "ExportTelemetryPdf", nameof(AppPermissions.ReportsExportPdf))]
    [InlineData("Sensors", "ExportTelemetryCsv", nameof(AppPermissions.ReportsExportExcel))]
    [InlineData("Export", "ExportReadings", nameof(AppPermissions.ReportsExportExcel))]
    [InlineData("Export", "ExportRollups", nameof(AppPermissions.ReportsExportExcel))]
    [InlineData("Readings", "GetLatest", nameof(AppPermissions.DevicesView))]
    [InlineData("Readings", "GetReadings", nameof(AppPermissions.ReportsView))]
    [InlineData("Firmware", "Get", nameof(AppPermissions.FirmwareView))]
    [InlineData("Firmware", "Create", nameof(AppPermissions.FirmwareUpload))]
    [InlineData("Firmware", "Rollout", nameof(AppPermissions.FirmwareRollout))]
    public void SensitiveActionsRequireTheirFunctionPermission(string controller, string method, string permission)
    {
        var source = File.ReadAllText(Path.Combine(Controllers, controller + "Controller.cs"));
        var action = Assert.Single(Attributes.Matches(source).Cast<Match>(),
            a => a.Groups["name"].Value == method);
        var classMatch = Regex.Match(source, @"\bpublic\s+(?:sealed\s+|abstract\s+)?class\s+\w+");
        Assert.True(classMatch.Success);
        var effective = source[..classMatch.Index]
            + action.Groups["attributes"].Value;
        Assert.Contains($"[Authorize(Policy = $\"Action:{{AppPermissions.{permission}}}\")]", effective);
    }

    [Fact]
    public void AllDeclaredActionPoliciesUseRegisteredPermissionConstants()
    {
        foreach (var file in Directory.GetFiles(Controllers, "*Controller.cs"))
        foreach (Match permission in Regex.Matches(File.ReadAllText(file), @"Action:\{AppPermissions\.(\w+)\}"))
        {
            var field = typeof(AppPermissions).GetField(permission.Groups[1].Value);
            Assert.NotNull(field);
            Assert.Contains((string)field.GetRawConstantValue()!, AppPermissions.All);
        }
    }

    private static string FindControllers()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "scada_demo_test.API", "Controllers");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Run this architecture suite from the repository checkout.");
    }
}
