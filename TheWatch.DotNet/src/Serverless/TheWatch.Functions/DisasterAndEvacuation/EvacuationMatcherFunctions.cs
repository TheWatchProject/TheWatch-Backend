using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for evacuation request-to-offer matching.
/// Implements matching logic from evacuation-api.yaml and todo.md
/// Uses geohash proximity matching with multi-round provider notifications
/// </summary>
public class EvacuationMatcherFunctions
{
    private readonly ILogger<EvacuationMatcherFunctions> _logger;

    public EvacuationMatcherFunctions(ILogger<EvacuationMatcherFunctions> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Service Bus trigger for matching evacuation requests to resource offers
    /// Triggered when new request is created or existing request is updated
    /// Queue: evacuation-matching-queue
    /// </summary>
    [Function("EvacuationMatcher")]
    public async Task ProcessEvacuationMatch(
        [ServiceBusTrigger("evacuation-matching-queue", Connection = "ServiceBusConnection")] string message,
        FunctionContext context)
    {
        _logger.LogInformation("Processing evacuation match: {Message}", message);

        // TODO: Implement geohash-based proximity matching
        // 1. Parse request message (requestId, evacueeId, location, urgency, resource_type)
        // 2. Extract geohash from request location (precision 9)
        // 3. Query evacuation_resource_offers where:
        //    - Geohash prefix matches (start with same prefix for proximity)
        //    - Resource type matches or is 'any'
        //    - Status = 'active'
        //    - Available_until > NOW()
        //    - Current_matches < max_matches
        // 4. Calculate haversine distance for each candidate offer
        // 5. Filter by service_radius_km (offer must cover request location)
        // 6. Rank offers by:
        //    a. Urgency match (immediate > within_hours > within_day > flexible)
        //    b. Provider reliability_rating (excellent > good > fair)
        //    c. Distance (closer is better)
        //    d. Capacity (prefer offers with available capacity)
        // 7. Create match_proposals for top 3-5 providers
        // 8. Send notifications to providers via notification-queue
        // 9. Set match expiration (5 minutes for immediate, 30 min for within_hours, etc.)
        // 10. Track response rate for re-matching if all decline
        // 11. If no matches found, expand geohash search radius and retry
        // 12. If still no matches, escalate to HQ for manual coordination

        _logger.LogInformation("Evacuation match processing complete");
    }

    /// <summary>
    /// GET /matches - List match proposals for a request or offer
    /// Returns match proposals with status (proposed, accepted, declined, expired)
    /// </summary>
    [Function("ListMatches")]
    public async Task<HttpResponseData> ListMatches(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "matches")] HttpRequestData req)
    {
        _logger.LogInformation("Listing match proposals");

        // TODO: Implement match listing logic
        // 1. Parse query parameters: requestId, offerId, status, limit, cursor
        // 2. Validate authentication
        // 3. Check authorization (must own request/offer or be HQ)
        // 4. Query match_proposals table filtered by parameters
        // 5. Order by created_at DESC
        // 6. Support pagination with cursor-based paging
        // 7. Return PagedMatches with items and nextCursor

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            items = new[]
            {
                new
                {
                    matchId = Guid.NewGuid(),
                    requestId = Guid.NewGuid(),
                    offerId = Guid.NewGuid(),
                    score = 95.5,
                    status = "proposed",
                    createdAt = DateTime.UtcNow.AddMinutes(-10),
                    expiresAt = DateTime.UtcNow.AddMinutes(20)
                },
                new
                {
                    matchId = Guid.NewGuid(),
                    requestId = Guid.NewGuid(),
                    offerId = Guid.NewGuid(),
                    score = 88.2,
                    status = "declined",
                    createdAt = DateTime.UtcNow.AddMinutes(-15),
                    expiresAt = DateTime.UtcNow.AddMinutes(15)
                }
            },
            nextCursor = (string?)null
        });

        return response;
    }

    /// <summary>
    /// POST /matches/{matchId}/respond - Provider responds to a match proposal
    /// Provider accepts or declines the match. If accepted, creates Active Evacuation
    /// </summary>
    [Function("RespondToMatch")]
    public async Task<HttpResponseData> RespondToMatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "matches/{matchId}/respond")] HttpRequestData req,
        string matchId)
    {
        _logger.LogInformation("Responding to match: {MatchId}", matchId);

        // TODO: Implement match response logic
        // 1. Parse MatchResponse from request body (decision: accept/decline, reason, notes)
        // 2. Validate provider authentication
        // 3. Check Idempotency-Key header
        // 4. Parse matchId as Guid
        // 5. Retrieve match_proposal record
        // 6. Verify provider owns the offer in this match
        // 7. Verify match is still in 'proposed' status and not expired
        // 8. Update match_proposal status to 'accepted' or 'declined'
        // 9. If ACCEPTED:
        //    a. Create active_evacuations record (status: 'meeting_arranged')
        //    b. Update evacuation_requests.status = 'matched'
        //    c. Update evacuation_resource_offers.current_matches++
        //    d. Notify evacuee of match via notification-queue
        //    e. Send meeting coordination message
        //    f. Mark other match proposals for same request as 'expired'
        //    g. Return MatchResponseResult with evacuationId
        // 10. If DECLINED:
        //    a. Log decline reason
        //    b. Check if other proposed matches exist for this request
        //    c. If no other matches, trigger re-matching (send to matching queue)
        //    d. Return MatchResponseResult with decision
        // 11. Track provider response metrics (acceptance rate, average response time)

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            matchId,
            decision = "accept",
            evacuationId = Guid.NewGuid(),
            recordedAt = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// Timer trigger to expire old match proposals
    /// Runs every 2 minutes to mark expired matches and trigger re-matching
    /// </summary>
    [Function("ExpireMatchProposals")]
    public async Task ExpireMatchProposals(
        [TimerTrigger("0 */2 * * * *")] TimerInfo timer,
        FunctionContext context)
    {
        _logger.LogInformation("Expiring match proposals");

        // TODO: Implement match expiration logic
        // 1. Query match_proposals where:
        //    - Status = 'proposed'
        //    - expires_at < NOW()
        // 2. Update status to 'expired'
        // 3. For each expired match, check if request has any other proposed/accepted matches
        // 4. If no active matches remain for request, trigger re-matching:
        //    a. Send message to evacuation-matching-queue
        //    b. Increment match_round counter
        //    c. Expand search radius if this is round 2+
        // 5. Log expiration metrics (expiration rate per urgency level)

        _logger.LogInformation("Match expiration complete");
    }

    /// <summary>
    /// GET /evacuations/{evacuationId} - Get active evacuation details
    /// Returns details of an active evacuation including participant info and status
    /// </summary>
    [Function("GetEvacuation")]
    public async Task<HttpResponseData> GetEvacuation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "evacuations/{evacuationId}")] HttpRequestData req,
        string evacuationId)
    {
        _logger.LogInformation("Getting evacuation: {EvacuationId}", evacuationId);

        // TODO: Implement evacuation retrieval logic
        // 1. Validate authentication
        // 2. Parse evacuationId as Guid
        // 3. Retrieve active_evacuations record
        // 4. Check authorization (evacuee, provider, or HQ)
        // 5. Join with evacuation_requests and evacuation_resource_offers
        // 6. Return ActiveEvacuation with full details
        // 7. Include meeting_location, destination, status, timestamps

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            evacuationId,
            requestId = Guid.NewGuid(),
            offerId = Guid.NewGuid(),
            evacueeId = Guid.NewGuid(),
            providerId = Guid.NewGuid(),
            resourceType = "vehicle",
            status = "en_route_to_pickup",
            meetingLocation = new
            {
                latitude = 34.0522,
                longitude = -118.2437,
                address = "Main Street & 1st Ave"
            },
            destination = new
            {
                latitude = 34.1522,
                longitude = -118.3437,
                address = "Safe Zone Community Center"
            },
            createdAt = DateTime.UtcNow.AddMinutes(-30),
            updatedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        return response;
    }

    /// <summary>
    /// POST /evacuations/{evacuationId}/location - Update participant location (ephemeral)
    /// Real-time location sharing during active evacuation only
    /// Auto-deleted after evacuation completes (privacy-first design)
    /// </summary>
    [Function("UpdateEvacuationLocation")]
    public async Task<HttpResponseData> UpdateEvacuationLocation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "evacuations/{evacuationId}/location")] HttpRequestData req,
        string evacuationId)
    {
        _logger.LogInformation("Updating evacuation location: {EvacuationId}", evacuationId);

        // TODO: Implement ephemeral location update logic
        // 1. Parse EvacuationLocationUpdate from request body (userId, userRole, location, timestamp, ttlSeconds)
        // 2. Validate authentication
        // 3. Check Idempotency-Key header
        // 4. Parse evacuationId as Guid
        // 5. Verify active_evacuations exists and is in active state
        // 6. Verify user is participant (evacuee or provider)
        // 7. Insert/update evacuation_locations table (ephemeral storage)
        // 8. Set TTL to ttlSeconds (default 1 hour, max 24 hours)
        // 9. Broadcast location update to other participant via SignalR
        // 10. Return 200 OK (no body needed)
        // 11. Note: Locations auto-deleted by TTL or when evacuation completes

        var response = req.CreateResponse(HttpStatusCode.OK);
        return response;
    }

    /// <summary>
    /// POST /evacuations/{evacuationId}/messages - Send evacuation message
    /// Lightweight messaging between evacuee/provider (and HQ if enabled)
    /// </summary>
    [Function("SendEvacuationMessage")]
    public async Task<HttpResponseData> SendEvacuationMessage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "evacuations/{evacuationId}/messages")] HttpRequestData req,
        string evacuationId)
    {
        _logger.LogInformation("Sending evacuation message: {EvacuationId}", evacuationId);

        // TODO: Implement evacuation messaging logic
        // 1. Parse EvacuationMessageCreate from request body (fromUserId, fromRole, message, priority, timestamp)
        // 2. Validate authentication
        // 3. Check Idempotency-Key header
        // 4. Parse evacuationId as Guid
        // 5. Verify active_evacuations exists
        // 6. Verify user is participant (evacuee, provider, or HQ)
        // 7. Validate message length (max 2000 chars)
        // 8. Create evacuation_messages record
        // 9. Send notification to other participant via notification-queue
        // 10. Broadcast message via SignalR for real-time delivery
        // 11. Return EvacuationMessage

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            messageId = Guid.NewGuid(),
            fromUserId = Guid.NewGuid(),
            fromRole = "provider",
            message = "I'm on my way, ETA 10 minutes",
            priority = "normal",
            timestamp = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// GET /evacuations/{evacuationId}/messages - List evacuation messages
    /// Returns message history for coordination between evacuee and provider
    /// </summary>
    [Function("ListEvacuationMessages")]
    public async Task<HttpResponseData> ListEvacuationMessages(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "evacuations/{evacuationId}/messages")] HttpRequestData req,
        string evacuationId)
    {
        _logger.LogInformation("Listing evacuation messages: {EvacuationId}", evacuationId);

        // TODO: Implement message listing logic
        // 1. Parse query parameters: limit, cursor
        // 2. Validate authentication
        // 3. Parse evacuationId as Guid
        // 4. Check authorization (evacuee, provider, or HQ)
        // 5. Query evacuation_messages for this evacuation
        // 6. Order by timestamp DESC
        // 7. Support pagination with cursor
        // 8. Return PagedMessages

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            items = new[]
            {
                new
                {
                    messageId = Guid.NewGuid(),
                    fromUserId = Guid.NewGuid(),
                    fromRole = "provider",
                    message = "I'm on my way, ETA 10 minutes",
                    priority = "normal",
                    timestamp = DateTime.UtcNow.AddMinutes(-5)
                },
                new
                {
                    messageId = Guid.NewGuid(),
                    fromUserId = Guid.NewGuid(),
                    fromRole = "evacuee",
                    message = "Thank you! Waiting at the corner of Main and 1st",
                    priority = "normal",
                    timestamp = DateTime.UtcNow.AddMinutes(-10)
                }
            },
            nextCursor = (string?)null
        });

        return response;
    }
}
