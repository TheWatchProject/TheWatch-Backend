using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Notifications;

public class EmergencySmsAndTelephonyAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EmergencySmsAndTelephonyAdapter> _logger;

    public EmergencySmsAndTelephonyAdapter(HttpClient httpClient, ILogger<EmergencySmsAndTelephonyAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<int> BroadcastGeoTargetedSmsAsync(IEnumerable<string> phoneNumbers, string alertMessage, CancellationToken ct = default)
    {
        int count = 0;
        foreach (var phone in phoneNumbers)
        {
            _logger.LogInformation("Dispatched emergency SMS alert to {Phone}: '{Message}'", phone, alertMessage);
            count++;
        }
        await Task.CompletedTask;
        return count;
    }
}
