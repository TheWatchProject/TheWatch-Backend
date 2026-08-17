using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Telemetry;

public class SensorTelemetryAdapter : ITelemetryPort
{
    private readonly ILogger<SensorTelemetryAdapter> _logger;

    public SensorTelemetryAdapter(ILogger<SensorTelemetryAdapter> logger)
    {
        _logger = logger;
    }

    public Task IngestTelemetryAsync(TelemetryPacket packet, CancellationToken ct = default)
    {
        _logger.LogDebug("Ingested telemetry for {DeviceId}: Lat={Lat}, Lon={Lon}, Battery={Bat}%",
            packet.DeviceId, packet.Latitude, packet.Longitude, packet.Battery);
        return Task.CompletedTask;
    }
}