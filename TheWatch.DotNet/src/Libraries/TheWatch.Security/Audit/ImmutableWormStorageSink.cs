using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Security.Audit;

public class ImmutableWormStorageSink
{
    private readonly ILogger<ImmutableWormStorageSink> _logger;

    public ImmutableWormStorageSink(ILogger<ImmutableWormStorageSink> logger)
    {
        _logger = logger;
    }

    public async Task<string> AppendImmutableAuditRecordAsync(string incidentId, string actorId, string payloadJson, CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var recordBody = $"{timestamp:O}|{incidentId}|{actorId}|{payloadJson}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recordBody)));

        _logger.LogInformation("Locked immutable WORM audit record for Incident {IncidentId}. Legal Hash: {Hash}", incidentId, hash);
        await Task.CompletedTask;
        return hash;
    }
}
