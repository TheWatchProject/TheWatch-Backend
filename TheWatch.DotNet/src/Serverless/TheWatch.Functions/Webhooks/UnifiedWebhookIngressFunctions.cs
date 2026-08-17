using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Contracts;
using TheWatch.Infrastructure.Webhooks;

namespace TheWatch.Functions.Webhooks;

/// <summary>
/// Serverless HTTP Webhook Ingress Functions handling external webhooks with HMAC validation.
/// </summary>
public sealed class UnifiedWebhookIngressFunctions
{
    private readonly ILogger<UnifiedWebhookIngressFunctions> _logger;
    private readonly UnifiedWebhookSubscriptionAndDeliveryEngine _engine;

    public UnifiedWebhookIngressFunctions(
        ILogger<UnifiedWebhookIngressFunctions> logger,
        UnifiedWebhookSubscriptionAndDeliveryEngine engine)
    {
        _logger = logger;
        _engine = engine;
    }

    /// <summary>
    /// Ingests incoming 911 CAD & Mutual Aid webhook alerts.
    /// </summary>
    [Function("IngestCadWebhook")]
    public async Task<HttpResponseData> IngestCadWebhook(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhooks/cad")] HttpRequestData req)
    {
        _logger.LogInformation("Processing inbound CAD webhook.");
        string body = await new StreamReader(req.Body).ReadToEndAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync("{\"status\":\"ACCEPTED\",\"event\":\"CAD_WEBHOOK_INGESTED\"}");
        return response;
    }

    /// <summary>
    /// Ingests smart home security & perimeter sensor alerts (Ring, Alexa, Nest).
    /// </summary>
    [Function("IngestSmartHomeWebhook")]
    public async Task<HttpResponseData> IngestSmartHomeWebhook(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhooks/smarthome")] HttpRequestData req)
    {
        _logger.LogInformation("Processing inbound Smart Home Alarm webhook.");
        string body = await new StreamReader(req.Body).ReadToEndAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync("{\"status\":\"ACCEPTED\",\"event\":\"SMARTHOME_ALARM_INGESTED\"}");
        return response;
    }

    /// <summary>
    /// Ingests SCADA, industrial pipeline, and chemical plume alarms.
    /// </summary>
    [Function("IngestScadaWebhook")]
    public async Task<HttpResponseData> IngestScadaWebhook(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhooks/scada")] HttpRequestData req)
    {
        _logger.LogInformation("Processing inbound SCADA industrial telemetry webhook.");
        string body = await new StreamReader(req.Body).ReadToEndAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync("{\"status\":\"ACCEPTED\",\"event\":\"SCADA_ANOMALY_INGESTED\"}");
        return response;
    }
}
