using Microsoft.AspNetCore.SignalR;

namespace scada_demo_test.Infrastructure.RealTime;

// Thin SignalR hub. Clients (web today, MAUI later) connect here and can
// join a "zone group" so they only receive updates for the devices they're viewing.
public class LiveTelemetryHub : Hub
{
    public async Task JoinZone(string zoneId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"zone-{zoneId}");

    public async Task LeaveZone(string zoneId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"zone-{zoneId}");
}
