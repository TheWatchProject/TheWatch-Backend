using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Serverless Azure Functions handling DevOps control plane, pipelines, health & feature flags (devops-api.yaml).
/// </summary>
public class DevOpsFunctions
{
    private readonly ILogger<DevOpsFunctions> _logger;

    public DevOpsFunctions(ILogger<DevOpsFunctions> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("DevOpsListPipelines")]
    public async Task<HttpResponseData> ListPipelines(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devops/pipelines")] HttpRequestData req)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null || !JwtUtilities.HasRole(principal, "admin"))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new[]
        {
            new { pipelineId = Guid.NewGuid(), name = "Core-API-CI-CD", status = "succeeded", lastRun = DateTime.UtcNow.AddHours(-1) },
            new { pipelineId = Guid.NewGuid(), name = "Functions-Serverless-Deploy", status = "succeeded", lastRun = DateTime.UtcNow.AddMinutes(-30) },
            new { pipelineId = Guid.NewGuid(), name = "HQ-Portal-Build", status = "succeeded", lastRun = DateTime.UtcNow.AddHours(-2) }
        });
        return response;
    }

    [Function("DevOpsGetHealth")]
    public async Task<HttpResponseData> GetHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devops/health")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "healthy",
            services = new
            {
                database = "operational",
                blobStorage = "operational",
                signalR = "operational",
                functions = "operational",
                hangfire = "operational"
            },
            version = "1.0.0",
            timestamp = DateTime.UtcNow
        });
        return response;
    }

    [Function("DevOpsListFeatureFlags")]
    public async Task<HttpResponseData> ListFeatureFlags(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devops/feature-flags")] HttpRequestData req)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            enableLocalWhisper = true,
            enableAlexaIntegration = true,
            enableGoogleHomeIntegration = true,
            enableDurableOrchestrator = true,
            enableRealtimeSignalR = true
        });
        return response;
    }

    [Function("DevOpsTriggerDeployment")]
    public async Task<HttpResponseData> TriggerDeployment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "devops/deployments")] HttpRequestData req)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null || !JwtUtilities.HasRole(principal, "admin"))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var body = await req.ReadFromJsonAsync<DevOpsDeploymentRequest>();
        _logger.LogInformation("Triggered DevOps deployment for environment {Env}", body?.Environment);

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            deploymentId = Guid.NewGuid(),
            environment = body?.Environment ?? "staging",
            status = "in_progress",
            triggeredAt = DateTime.UtcNow
        });
        return response;
    }
}

public class DevOpsDeploymentRequest
{
    public string? Environment { get; set; }
    public string? CommitHash { get; set; }
}
