using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Contracts;
using TheWatch.Geospatial.Db;

namespace TheWatch.Functions.EmergencyAndDispatch;

public sealed class TacticalRoutingAndCrowdMeshFunctions
{
    private readonly ILogger<TacticalRoutingAndCrowdMeshFunctions> _logger;
    private readonly IConstrainedPathfinder _pathfinder;
    private readonly IVolunteerCrowdMonitoringEngine _crowdEngine;

    public TacticalRoutingAndCrowdMeshFunctions(
        ILogger<TacticalRoutingAndCrowdMeshFunctions> logger,
        IConstrainedPathfinder pathfinder,
        IVolunteerCrowdMonitoringEngine crowdEngine)
    {
        _logger = logger;
        _pathfinder = pathfinder;
        _crowdEngine = crowdEngine;
    }

    [Function("CalculateHazardAvoidanceRoute")]
    public async Task<HttpResponseData> CalculateRouteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/routing/calculate")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body ?? "{}");
        var root = doc.RootElement;

        string incidentId = root.GetProperty("incidentId").GetString() ?? "INC-UNKNOWN";
        string unitId = root.GetProperty("unitId").GetString() ?? "UNIT-UNKNOWN";
        double origLat = root.GetProperty("originLatitude").GetDouble();
        double origLon = root.GetProperty("originLongitude").GetDouble();
        double destLat = root.GetProperty("destinationLatitude").GetDouble();
        double destLon = root.GetProperty("destinationLongitude").GetDouble();

        var route = _pathfinder.CalculateEmergencyRoute(incidentId, unitId, origLat, origLon, destLat, destLon);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(route);
        return res;
    }

    [Function("IngestCrowdDistressDetection")]
    public async Task<HttpResponseData> IngestCrowdDetectionAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/crowd/detection")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body ?? "{}");
        var root = doc.RootElement;

        string eventId = root.GetProperty("eventId").GetString() ?? "EVENT-UNKNOWN";
        string volId = root.GetProperty("volunteerUserId").GetString() ?? "VOL-UNKNOWN";
        string phrase = root.GetProperty("detectedPhrase").GetString() ?? "Help";
        double conf = root.GetProperty("confidence").GetDouble();
        double lat = root.GetProperty("latitude").GetDouble();
        double lon = root.GetProperty("longitude").GetDouble();

        var triangulated = _crowdEngine.IngestVolunteerDetection(eventId, volId, phrase, conf, lat, lon, DateTime.UtcNow);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { Triangulated = triangulated != null, Signal = triangulated });
        return res;
    }
}
