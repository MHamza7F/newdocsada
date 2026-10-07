using scada_demo_test.Application.DTOs;

namespace scada_demo_test.Application.Interfaces;

// Abstraction over "how live data reaches connected clients".
// Infrastructure implements this with SignalR today; swapping to another
// real-time transport later never touches Application/Domain code.
public interface ITelemetryBroadcaster
{
    Task BroadcastReadingAsync(LiveReadingDto reading, CancellationToken ct = default);
}
