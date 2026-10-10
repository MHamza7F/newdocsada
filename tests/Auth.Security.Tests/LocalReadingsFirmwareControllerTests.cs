using System.Security.Claims;
using Xunit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using scada_demo_test.API.Controllers;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Persistence;

namespace Auth.Security.Tests;

// Backend controller coverage. The test project owner should link the two new API
// controller files (or reference the API project) when enabling this file; no API
// production registration is required here.
public sealed class LocalReadingsFirmwareControllerTests
{
    [Fact]
    public async Task Readings_are_bounded_and_return_oldest_to_newest()
    {
        await using var db = await CreateDbAsync();
        var device = new Device { ExternalId = "dev-1", Name = "Meter", IsOnline = false };
        db.Devices.Add(device);
        for (var i = 0; i < 4; i++)
            db.SensorReadings.Add(new SensorReading { DeviceId = device.Id, Metric = "FlowRate", Value = i, Timestamp = DateTime.UtcNow.AddMinutes(i) });
        await db.SaveChangesAsync();

        var result = await new ReadingsController(db).GetReadings("dev-1", "FlowRate", 2);
        var points = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value as IEnumerable<ReadingsController.ReadingPointDto>;
        Assert.Equal(new[] { 2d, 3d }, points!.Select(p => p.Value));
        var latest = await new ReadingsController(db).GetLatest(default);
        var snapshots = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(latest.Result).Value as IEnumerable<ReadingsController.LatestSnapshotDto>;
        Assert.Equal(3d, Assert.Single(snapshots!).FlowRate);
        Assert.Null(Assert.Single(snapshots!).LastSeenAt);
    }

    [Fact]
    public async Task Firmware_metadata_does_not_fabricate_a_checksum_and_rollout_is_unsupported()
    {
        await using var db = await CreateDbAsync();
        var audit = new AuditLogService(db);
        var controller = new FirmwareController(db, audit) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }));

        var created = await controller.Create(new FirmwareController.UploadFirmwareRequest("v2", "desc", "ESP32", "firmware.bin", 12, "notes"), default);
        var payload = Assert.IsType<Microsoft.AspNetCore.Mvc.CreatedAtActionResult>(created.Result).Value;
        Assert.Equal(string.Empty, ((FirmwareController.FirmwareReleaseDto)payload!).ChecksumSha256);

        var rollout = await controller.Rollout(new FirmwareController.RolloutRequest(Guid.NewGuid(), "dev-1"), default);
        Assert.Equal(StatusCodes.Status501NotImplemented, ((Microsoft.AspNetCore.Mvc.ObjectResult)rollout).StatusCode);
    }

    private static async Task<MyDbContextDxy> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MyDbContextDxy>().UseSqlite(connection).Options;
        var db = new MyDbContextDxy(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
