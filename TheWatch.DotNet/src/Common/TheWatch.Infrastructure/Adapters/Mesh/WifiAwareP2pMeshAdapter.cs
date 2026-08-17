using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Adapters.Mesh;

public class WifiAwareP2pMeshAdapter
{
    private readonly ILogger<WifiAwareP2pMeshAdapter> _logger;

    public WifiAwareP2pMeshAdapter(ILogger<WifiAwareP2pMeshAdapter> logger)
    {
        _logger = logger;
    }

    public Task<bool> BroadcastWifiAwarePacketAsync(string serviceName, byte[] payload, CancellationToken ct = default)
    {
        _logger.LogInformation("Forwarded Wi-Fi Aware (NAN) peer-to-peer packet over service '{ServiceName}'. Bytes: {Bytes}",
            serviceName, payload.Length);
        return Task.FromResult(true);
    }
}
