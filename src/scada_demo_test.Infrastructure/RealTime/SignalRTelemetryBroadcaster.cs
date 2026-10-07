using Microsoft.AspNetCore.SignalR;
using scada_demo_test.Application.DTOs;
using scada_demo_test.Application.Interfaces;

namespace scada_demo_test.Infrastructure.RealTime;

// Implements the Application-layer abstraction using SignalR.
// TelemetryIngestService never references SignalR directly - only this interface.
public class SignalRTelemetryBroadcaster : ITelemetryBroadcaster
{
    private readonly IHubContext<LiveTelemetryHub> _hub;
    public SignalRTelemetryBroadcaster(IHubContext<LiveTelemetryHub> hub) => _hub = hub;

    public async Task BroadcastReadingAsync(LiveReadingDto reading, CancellationToken ct = default)
    {
        await _hub.Clients.All.SendAsync("ReceiveReading", reading, ct);
        // Later: broadcast to Clients.Group($"zone-{zoneId}") once zone filtering is wired up
    }
}
