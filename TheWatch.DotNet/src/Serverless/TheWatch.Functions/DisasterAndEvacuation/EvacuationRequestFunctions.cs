using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for evacuation request management.
/// Implements endpoints from evacuation-api.yaml
/// </summary>
public class EvacuationRequestFunctions
{
    private readonly ILogger<EvacuationRequestFunctions> _logger;

    public EvacuationRequestFunctions(ILogger<EvacuationRequestFunctions> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// POST /requests/evacuate - Create an evacuation request
    /// Used by evacuees to request help during disasters
    /// Triggers the Evacuation Matcher for geohash-based proximity matching
    /// </summary>
    [Function("CreateEvacuationRequest")]
    public async Task<HttpResponseData> CreateEvacuationRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "requests/evacuate")] HttpRequestData req)
    {
        _logger.LogInformation("Creating evacuation request");

        // TODO: Implement evacuation request creation logic
        // 1. Parse EvacuationRequestCreate from request body
        // 2. Validate evacuee authentication
        // 3. Check Idempotency-Key header to prevent duplicates
        // 4. Calculate geohash from currentLocation (precision 9 for ~5m accuracy)
        // 5. Validate party_size, urgency, preferred_resource_type
        // 6. Create evacuation_requests record
        // 7. Send message to evacuation-matching-queue (Service Bus)
        // 8. Return EvacuationRequest with status 'pending'

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            requestId = Guid.NewGuid(),
            evacueeId = Guid.NewGuid(),
            status = "pending",
            urgency = "immediate",
            preferredResourceType = "vehicle",
            currentLocation = new
            {
                latitude = 34.0522,
                longitude = -118.2437,
                geohash = "9q5ct2",
                address = "Los Angeles, CA"
            },
            partySize = 2,
            hasPets = false,
            createdAt = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// GET /requests/{requestId} - Get evacuation request details
    /// Allows evacuee to check request status and assigned provider
    /// </summary>
    [Function("GetEvacuationRequest")]
    public async Task<HttpResponseData> GetEvacuationRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "requests/{requestId}")] HttpRequestData req,
        string requestId)
    {
        _logger.LogInformation("Getting evacuation request: {RequestId}", requestId);

        // TODO: Implement evacuation request retrieval logic
        // 1. Validate authentication
        // 2. Check authorization (evacuee owns request, or HQ role)
        // 3. Parse requestId as Guid
        // 4. Retrieve evacuation_requests record from database
        // 5. Return EvacuationRequest with current status
        // 6. If status is 'matched', include provider details

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            requestId,
            evacueeId = Guid.NewGuid(),
            status = "matching",
            urgency = "within_hours",
            preferredResourceType = "shelter",
            currentLocation = new
            {
                latitude = 34.0522,
                longitude = -118.2437,
                geohash = "9q5ct2",
                address = "Los Angeles, CA"
            },
            partySize = 4,
            hasPets = true,
            petTypes = new[] { "dog", "cat" },
            createdAt = DateTime.UtcNow.AddHours(-1),
            updatedAt = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// PATCH /requests/{requestId} - Update an evacuation request
    /// Allows evacuee to adjust urgency, location, or notes while request is pending
    /// </summary>
    [Function("UpdateEvacuationRequest")]
    public async Task<HttpResponseData> UpdateEvacuationRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "requests/{requestId}")] HttpRequestData req,
        string requestId)
    {
        _logger.LogInformation("Updating evacuation request: {RequestId}", requestId);

        // TODO: Implement evacuation request update logic
        // 1. Parse EvacuationRequestUpdate from request body
        // 2. Validate evacuee authentication and ownership
        // 3. Check Idempotency-Key header
        // 4. Parse requestId as Guid
        // 5. Validate request is in 'pending' or 'matching' status (cannot update if matched/in_progress)
        // 6. Update evacuation_requests record (location, urgency, party_size, notes, etc.)
        // 7. Recalculate geohash if location changed
        // 8. If urgency increased, trigger re-matching (send message to matching queue)
        // 9. Return updated EvacuationRequest

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            requestId,
            evacueeId = Guid.NewGuid(),
            status = "pending",
            urgency = "immediate",
            preferredResourceType = "vehicle",
            currentLocation = new
            {
                latitude = 34.0622,
                longitude = -118.2537,
                geohash = "9q5ct3",
                address = "Updated Location, Los Angeles, CA"
            },
            partySize = 3,
            notes = "Updated: Need evacuation ASAP",
            updatedAt = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// POST /requests/{requestId}/cancel - Cancel an evacuation request
    /// Allows evacuee to cancel if no longer needed (e.g., found alternative help)
    /// </summary>
    [Function("CancelEvacuationRequest")]
    public async Task<HttpResponseData> CancelEvacuationRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "requests/{requestId}/cancel")] HttpRequestData req,
        string requestId)
    {
        _logger.LogInformation("Cancelling evacuation request: {RequestId}", requestId);

        // TODO: Implement evacuation request cancellation logic
        // 1. Parse CancelRequest from request body (optional reason)
        // 2. Validate evacuee authentication and ownership
        // 3. Check Idempotency-Key header
        // 4. Parse requestId as Guid
        // 5. Validate request is not already completed
        // 6. Update status to 'cancelled'
        // 7. If request was matched, notify provider of cancellation
        // 8. If active evacuation exists, update active_evacuations status to 'cancelled'
        // 9. Remove from matching queue if still pending
        // 10. Return cancelled EvacuationRequest

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            requestId,
            evacueeId = Guid.NewGuid(),
            status = "cancelled",
            urgency = "immediate",
            preferredResourceType = "vehicle",
            cancelledAt = DateTime.UtcNow,
            reason = "Found alternative transportation"
        });

        return response;
    }
}
