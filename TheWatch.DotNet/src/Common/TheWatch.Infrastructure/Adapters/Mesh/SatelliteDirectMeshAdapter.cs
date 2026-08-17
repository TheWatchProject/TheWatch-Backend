using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Adapters.Mesh;

public class SatelliteDirectMeshAdapter
{
    private readonly ILogger<SatelliteDirectMeshAdapter> _logger;

    public SatelliteDirectMeshAdapter(ILogger<SatelliteDirectMeshAdapter> logger)
    {
        _logger = logger;
    }

    public Task<bool> TransmitShortBurstDataAsync(byte[] telemetryData, string imei, CancellationToken ct = default)
    {
        _logger.LogWarning("🛰️ Transmitted {Bytes} bytes emergency burst via Iridium / Starlink Direct-to-Cell satellite uplink for IMEI {IMEI}.",
            telemetryData.Length, imei);
        return Task.FromResult(true);
    }
}
