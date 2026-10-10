using System.Net;
using System.Net.Http.Json;
using scada_demo_test.Web.Services;
using Xunit;

namespace Auth.Security.Tests;

/// <summary>Exercises the Web client exclusively through its local HTTP API.</summary>
public sealed class ApiClientLocalApiSecurityTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ProtectedReadsSurfaceApiDenials(HttpStatusCode status)
    {
        using var harness = new Harness(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<HttpRequestException>(() => harness.Client.GetDevicesAsync());
        await Assert.ThrowsAsync<HttpRequestException>(() => harness.Client.GetLatestSnapshotAsync());
        await Assert.ThrowsAsync<HttpRequestException>(() => harness.Client.GetReadingsAsync("flow-1"));
        await Assert.ThrowsAsync<HttpRequestException>(() => harness.Client.GetFirmwareReleasesAsync());
        Assert.All(harness.Requests, request => Assert.StartsWith("/api/", request.RequestUri!.AbsolutePath));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ApiErrorsDoNotBecomeSuccessfulMutations(HttpStatusCode status)
    {
        using var harness = new Harness(_ => new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(new { message = "service unavailable" })
        });

        var device = await harness.Client.CreateDeviceAsync(new("flow-1", "Flow", "FlowMeter", "ModbusRtu", 1, null, null));
        var firmware = await harness.Client.UploadFirmwareAsync(new("1.2.3", "test", "FlowMeter", "firmware.bin", 12, "notes"));
        Assert.False(device.Success);
        Assert.False(firmware.Success);
        Assert.NotEmpty(harness.Requests);
    }

    [Fact]
    public async Task EmptyAuthoritativeResponsesRemainEmpty()
    {
        using var harness = new Harness(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<object>())
        });

        Assert.Empty(await harness.Client.GetDevicesAsync());
        Assert.Empty(await harness.Client.GetLatestSnapshotAsync());
        Assert.Empty(await harness.Client.GetReadingsAsync("flow-1"));
        Assert.Empty(await harness.Client.GetFirmwareReleasesAsync());
    }

    [Fact]
    public async Task LocalApiMapsDeviceAndReadingDtosWithoutFallback()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        using var harness = new Harness(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/devices" => Json(new[] { new ScadaDemoTestApiClient.GatewayDeviceDto(id, "flow-1", "Flow", "USR-W610", "10.0.0.5", 502, 9600, "None", 1, 2000, 8, 1, true, now, now) }),
            "/api/readings/latest" => Json(new[] { new ScadaDemoTestApiClient.LatestSnapshotDto(id, "flow-1", "Flow", "FlowMeter", "Online", 2, "10.0.0.5", now, 4.5, "m³/h", 12, "m³") }),
            _ when request.RequestUri.AbsolutePath.StartsWith("/api/readings/", StringComparison.Ordinal) => Json(new[] { new ScadaDemoTestApiClient.ReadingPointDto(now, 4.5, "m³/h") }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var devices = await harness.Client.GetDevicesAsync();
        var latest = await harness.Client.GetLatestSnapshotAsync();
        var readings = await harness.Client.GetReadingsAsync("flow-1", "FlowRate", 30);
        Assert.Equal((id, "flow-1", "USR-W610", "Online"), (devices[0].Id, devices[0].ExternalId, devices[0].DeviceType, devices[0].Status));
        Assert.Equal(4.5, latest[0].FlowRate);
        Assert.Equal(4.5, readings[0].Value);
        Assert.Contains(harness.Requests, r => r.RequestUri!.Query.Contains("metric=FlowRate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FirmwareMetadataUsesApiAndRolloutReportsUnsupported()
    {
        using var harness = new Harness(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/firmware" when request.Method == HttpMethod.Get => Json(new[] { new ScadaDemoTestApiClient.FirmwareReleaseDto(Guid.NewGuid(), "1.2.3", "metadata", "FlowMeter", "firmware.bin", 42, "sha", true, DateTime.UtcNow, "test") }),
            "/api/firmware" when request.Method == HttpMethod.Post => new HttpResponseMessage(HttpStatusCode.Created),
            "/api/firmware/rollout" => new HttpResponseMessage(HttpStatusCode.NotImplemented) { Content = JsonContent.Create(new { message = "OTA rollout is not supported" }) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var releases = await harness.Client.GetFirmwareReleasesAsync();
        var upload = await harness.Client.UploadFirmwareAsync(new("1.2.4", "metadata", "FlowMeter", "firmware.bin", 42, "notes"));
        var rollout = await harness.Client.RolloutFirmwareAsync(new(Guid.NewGuid(), "flow-1"));
        Assert.Equal("1.2.3", releases[0].Version);
        Assert.True(upload.Success);
        Assert.False(rollout.Success);
        Assert.Contains("not supported", rollout.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WebClientAndProgramContainNoFirebaseReferences()
    {
        var root = FindRepositoryRoot();
        var client = File.ReadAllText(Path.Combine(root, "src", "scada_demo_test.Web", "Services", "ScadaDemoTestApiClient.cs"));
        var program = File.ReadAllText(Path.Combine(root, "src", "scada_demo_test.Web", "Program.cs"));
        Assert.DoesNotContain("Firebase", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Firebase", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HistoryUsesRequestedRangeAndAuthoritativeStatistics()
    {
        var from = DateTime.UtcNow.AddDays(-2);
        var to = from.AddDays(1);
        var id = Guid.NewGuid();
        using var harness = new Harness(request =>
        {
            Assert.Equal("/api/reports/flow-1/history", request.RequestUri!.AbsolutePath);
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.Contains($"from={from:O}", query);
            Assert.Contains($"to={to:O}", query);
            return Json(new ScadaDemoTestApiClient.HistorySeriesDto(id, "flow-1", "FlowRate", "L/min", "Hourly", false, from, to,
                new() { new(from, 12, 8, 4, 20) }));
        });
        var history = await harness.Client.GetHistoryAsync("flow-1", "FlowRate", from, to);
        Assert.Equal(id, history!.DeviceId);
        Assert.Equal("L/min", history.Unit);
        Assert.Equal(4d, Assert.Single(history.Points).Min);
        Assert.Equal(20d, Assert.Single(history.Points).Max);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class Harness : IDisposable
    {
        public ScadaDemoTestApiClient Client { get; }
        public List<HttpRequestMessage> Requests { get; } = new();

        public Harness(Func<HttpRequestMessage, HttpResponseMessage> api)
        {
            Client = new ScadaDemoTestApiClient(
                new HttpClient(new Stub(request => { Requests.Add(request); return api(request); })) { BaseAddress = new Uri("https://api") },
                new HttpClient(new Stub(request => { Requests.Add(request); return api(request); })) { BaseAddress = new Uri("https://scan") });
        }

        public void Dispose() => Client.Dispose();
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "scada_demo_test.Web"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run this suite from the repository checkout.");
    }
}
