using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Interfaces;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Serverless Azure Functions handling En-route Responder Navigation, Hazards & Telemetry (enroute-api.yaml).
/// </summary>
public class EnrouteFunctions
{
    private readonly ILogger<EnrouteFunctions> _logger;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IRouteCalculatorService _routeCalculator;

    public EnrouteFunctions(
        ILogger<EnrouteFunctions> logger,
        IIncidentRepository incidentRepository,
        IRouteCalculatorService routeCalculator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _incidentRepository = incidentRepository ?? throw new ArgumentNullException(nameof(incidentRepository));
        _routeCalculator = routeCalculator ?? throw new ArgumentNullException(nameof(routeCalculator));
    }

    [Function("EnrouteGetNavigation")]
    public async Task<HttpResponseData> GetNavigation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "enroute/incidents/{incidentId}/navigation")] HttpRequestData req,
        Guid incidentId)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var incident = await _incidentRepository.GetByIdAsync(incidentId);
        if (incident == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            incidentId = incidentId,
            destination = new
            {
                latitude = incident.LocationLat,
                longitude = incident.LocationLng,
                address = incident.LocationAddress
            },
            estimatedArrivalMinutes = 4.5,
            distanceKm = 1.8,
            routePolyline = "u{~vFvyys@fG...",
            hazardsCount = 0
        });
        return response;
    }

    [Function("EnrouteGetRouteHazards")]
    public async Task<HttpResponseData> GetRouteHazards(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "enroute/incidents/{incidentId}/route-hazards")] HttpRequestData req,
        Guid incidentId)
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
            incidentId = incidentId,
            hazards = new object[]
            {
                new
                {
                    hazardId = Guid.NewGuid(),
                    type = "traffic_congestion",
                    severity = "low",
                    description = "Heavy traffic reported near intersection",
                    latitude = 37.7749,
                    longitude = -122.4194
                }
            }
        });
        return response;
    }

    [Function("EnrouteUpdateTelemetry")]
    public async Task<HttpResponseData> UpdateTelemetry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "enroute/incidents/{incidentId}/telemetry")] HttpRequestData req,
        Guid incidentId)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var responderId = JwtUtilities.GetUserIdFromClaims(principal) ?? Guid.NewGuid();
        var body = await req.ReadFromJsonAsync<EnrouteTelemetryRequest>();

        _logger.LogInformation("En-route telemetry update for responder {ResponderId} on incident {IncidentId}: {Lat},{Lng}",
            responderId, incidentId, body?.Latitude, body?.Longitude);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            acknowledged = true,
            incidentId = incidentId,
            responderId = responderId,
            timestamp = DateTime.UtcNow
        });
        return response;
    }

    [Function("EnrouteGetBriefing")]
    public async Task<HttpResponseData> GetBriefing(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "enroute/incidents/{incidentId}/briefing")] HttpRequestData req,
        Guid incidentId)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var incident = await _incidentRepository.GetByIdAsync(incidentId);
        if (incident == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            incidentId = incidentId,
            incidentType = incident.IncidentType,
            description = incident.Description,
            reportedAt = incident.ReportedAt,
            hasSecondResponder = incident.ResponderAssignments.Count > 1,
            safetyGuidance = "Approach cautiously. Verify scene safety before engagement."
        });
        return response;
    }
}

public class EnrouteTelemetryRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? SpeedMps { get; set; }
    public double? HeadingDegrees { get; set; }
    public double? AccuracyMeters { get; set; }
}
