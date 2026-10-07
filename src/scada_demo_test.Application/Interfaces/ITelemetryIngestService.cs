using scada_demo_test.Application.DTOs;

namespace scada_demo_test.Application.Interfaces;

// Entry point every transport (MQTT today, HTTP webhook or OPC-UA later)
// calls into once it has decoded a message into the common DTO shape.
public interface ITelemetryIngestService
{
    Task IngestAsync(TelemetryMessageDto message, CancellationToken ct = default);
}
