using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Failover;

public enum CloudProvider { Azure, Aws, Gcp }

public class ActiveActiveCloudOrchestrator
{
    private readonly ILogger<ActiveActiveCloudOrchestrator> _logger;
    private CloudProvider _primaryProvider = CloudProvider.Azure;
    private readonly Dictionary<CloudProvider, bool> _healthStatus = new()
    {
        [CloudProvider.Azure] = true,
        [CloudProvider.Aws] = true,
        [CloudProvider.Gcp] = true
    };

    public ActiveActiveCloudOrchestrator(ILogger<ActiveActiveCloudOrchestrator> logger)
    {
        _logger = logger;
    }

    public CloudProvider GetActiveRoute() => _primaryProvider;

    public void ReportProviderFailure(CloudProvider failedProvider)
    {
        _healthStatus[failedProvider] = false;
        _logger.LogCritical("Cloud Provider {Provider} marked DEGRADED. Initiating failover.", failedProvider);

        if (_primaryProvider == failedProvider)
        {
            _primaryProvider = failedProvider switch
            {
                CloudProvider.Azure => _healthStatus[CloudProvider.Aws] ? CloudProvider.Aws : CloudProvider.Gcp,
                CloudProvider.Aws => _healthStatus[CloudProvider.Gcp] ? CloudProvider.Gcp : CloudProvider.Azure,
                _ => CloudProvider.Azure
            };
            _logger.LogWarning("Failover route shifted to {NewPrimary}", _primaryProvider);
        }
    }

    public void ReportProviderHealthy(CloudProvider provider)
    {
        _healthStatus[provider] = true;
    }
}
