namespace scada_demo_test.Web.Services;

public interface ITelemetryBroadcastBus
{
    event Action<string, string, string, double, string?, bool>? OnReading;
    void PublishReading(string externalId, string deviceName, string metric, double value, string? unit, bool isOnline);
}

public class TelemetryBroadcastBus : ITelemetryBroadcastBus
{
    public event Action<string, string, string, double, string?, bool>? OnReading;

    public void PublishReading(string externalId, string deviceName, string metric, double value, string? unit, bool isOnline)
    {
        OnReading?.Invoke(externalId, deviceName, metric, value, unit, isOnline);
    }
}
