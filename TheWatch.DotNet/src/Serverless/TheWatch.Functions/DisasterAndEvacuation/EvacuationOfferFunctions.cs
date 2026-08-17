using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for evacuation resource offer management.
/// Implements endpoints from evacuation-api.yaml
/// Providers offer vehicles, shelters, supplies, medical assistance, fuel, or equipment
/// </summary>
public class EvacuationOfferFunctions
{
    private readonly ILogger<EvacuationOfferFunctions> _logger;

    public EvacuationOfferFunctions(ILogger<EvacuationOfferFunctions> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// POST /offers/resources - Create an evacuation resource offer
    /// Providers offer resources (vehicle, shelter, supplies, etc) with a service radius
    /// Matching uses offer geohash + radius filtering for efficient proximity matching
    /// </summary>
    [Function("CreateResourceOffer")]
    public async Task<HttpResponseData> CreateResourceOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "offers/resources")] HttpRequestData req)
    {
        _logger.LogInformation("Creating evacuation resource offer");

        // TODO: Implement resource offer creation logic
        // 1. Parse EvacuationOfferCreate from request body
        // 2. Validate provider authentication
        // 3. Check Idempotency-Key header to prevent duplicates
        // 4. Calculate geohash from offer location (precision 9)
        // 5. Validate resource_type (vehicle, shelter, supplies, medical, fuel, equipment)
        // 6. Validate service_radius_km (1-300 km)
        // 7. Validate capacity and availability window
        // 8. Create evacuation_resource_offers record with status 'active'
        // 9. Index offer by geohash for proximity matching
        // 10. Return EvacuationOffer

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            offerId = Guid.NewGuid(),
            providerId = Guid.NewGuid(),
            resourceType = "vehicle",
            location = new
            {
                latitude = 34.0522,
                longitude = -118.2437,
                geohash = "9q5ct2",
                address = "Los Angeles, CA"
            },
            serviceRadiusKm = 50,
            capacity = 4,
            availableFrom = DateTime.UtcNow,
            availableUntil = DateTime.UtcNow.AddDays(3),
            status = "active",
            contactPhone = "+13105551234",
            notes = "4-door sedan, can transport up to 4 passengers with luggage",
            createdAt = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// GET /offers/resources/{offerId} - Get an evacuation offer
    /// Retrieve details of a specific resource offer
    /// </summary>
    [Function("GetEvacuationOffer")]
    public async Task<HttpResponseData> GetEvacuationOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offers/resources/{offerId}")] HttpRequestData req,
        string offerId)
    {
        _logger.LogInformation("Getting evacuation offer: {OfferId}", offerId);

        // TODO: Implement evacuation offer retrieval logic
        // 1. Validate authentication
        // 2. Check authorization (provider owns offer, matched evacuee, or HQ role)
        // 3. Parse offerId as Guid
        // 4. Retrieve evacuation_resource_offers record from database
        // 5. Return EvacuationOffer with current status
        // 6. If status is 'matched' or 'in_use', include matched request details

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            offerId,
            providerId = Guid.NewGuid(),
            resourceType = "shelter",
            location = new
            {
                latitude = 34.0522,
                longitude = -118.2437,
                geohash = "9q5ct2",
                address = "Community Center, Los Angeles, CA"
            },
            serviceRadiusKm = 25,
            capacity = 50,
            availableFrom = DateTime.UtcNow.AddHours(-2),
            availableUntil = DateTime.UtcNow.AddDays(7),
            status = "active",
            contactPhone = "+13105555678",
            notes = "Large community center with kitchen, showers, pet-friendly",
            createdAt = DateTime.UtcNow.AddHours(-2),
            updatedAt = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// PATCH /offers/resources/{offerId} - Update an evacuation offer
    /// Allows provider to update capacity, availability window, or service radius
    /// </summary>
    [Function("UpdateEvacuationOffer")]
    public async Task<HttpResponseData> UpdateEvacuationOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "offers/resources/{offerId}")] HttpRequestData req,
        string offerId)
    {
        _logger.LogInformation("Updating evacuation offer: {OfferId}", offerId);

        // TODO: Implement evacuation offer update logic
        // 1. Parse EvacuationOfferUpdate from request body
        // 2. Validate provider authentication and ownership
        // 3. Check Idempotency-Key header
        // 4. Parse offerId as Guid
        // 5. Validate offer is in 'active' status (cannot update if withdrawn/expired)
        // 6. Update evacuation_resource_offers record (capacity, service_radius_km, availability, notes)
        // 7. Recalculate geohash if location changed
        // 8. If capacity increased, trigger re-matching for pending requests
        // 9. Return updated EvacuationOffer

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            offerId,
            providerId = Guid.NewGuid(),
            resourceType = "vehicle",
            location = new
            {
                latitude = 34.0622,
                longitude = -118.2537,
                geohash = "9q5ct3",
                address = "Updated Location, Los Angeles, CA"
            },
            serviceRadiusKm = 75,
            capacity = 6,
            availableFrom = DateTime.UtcNow,
            availableUntil = DateTime.UtcNow.AddDays(5),
            status = "active",
            notes = "Updated: SUV, increased capacity to 6 passengers",
            updatedAt = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// POST /offers/resources/{offerId}/withdraw - Withdraw an evacuation offer
    /// Provider withdraws offer if no longer able to provide resources
    /// </summary>
    [Function("WithdrawEvacuationOffer")]
    public async Task<HttpResponseData> WithdrawEvacuationOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "offers/resources/{offerId}/withdraw")] HttpRequestData req,
        string offerId)
    {
        _logger.LogInformation("Withdrawing evacuation offer: {OfferId}", offerId);

        // TODO: Implement evacuation offer withdrawal logic
        // 1. Parse CancelRequest from request body (optional reason)
        // 2. Validate provider authentication and ownership
        // 3. Check Idempotency-Key header
        // 4. Parse offerId as Guid
        // 5. Validate offer is not already withdrawn/expired
        // 6. Update status to 'withdrawn'
        // 7. If offer was matched, notify evacuees and trigger re-matching
        // 8. If active evacuations exist, notify evacuees and HQ of disruption
        // 9. Remove from matching pool
        // 10. Return withdrawn EvacuationOffer

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            offerId,
            providerId = Guid.NewGuid(),
            resourceType = "vehicle",
            status = "withdrawn",
            withdrawnAt = DateTime.UtcNow,
            reason = "Vehicle needed for family evacuation"
        });

        return response;
    }

    /// <summary>
    /// GET /offers/resources - List available resource offers (internal use by matcher)
    /// Used by Evacuation Matcher to find offers near a request
    /// </summary>
    [Function("ListResourceOffers")]
    public async Task<HttpResponseData> ListResourceOffers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offers/resources")] HttpRequestData req)
    {
        _logger.LogInformation("Listing available resource offers");

        // TODO: Implement resource offer listing logic
        // 1. Parse query parameters: geohash_prefix, resource_type, radius_km, status
        // 2. Validate authentication (HQ or matcher service)
        // 3. Query evacuation_resource_offers filtered by:
        //    - Geohash prefix matching (for proximity)
        //    - Resource type if specified
        //    - Status = 'active' (default)
        //    - Available_until > NOW()
        // 4. Calculate distance from request location using haversine
        // 5. Filter by service_radius_km
        // 6. Rank by proximity, capacity, reliability_rating
        // 7. Return top N offers (limit query param, default 50)

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            items = new[]
            {
                new
                {
                    offerId = Guid.NewGuid(),
                    providerId = Guid.NewGuid(),
                    resourceType = "vehicle",
                    location = new { latitude = 34.0522, longitude = -118.2437, geohash = "9q5ct2" },
                    serviceRadiusKm = 50,
                    capacity = 4,
                    status = "active",
                    distanceKm = 2.5
                },
                new
                {
                    offerId = Guid.NewGuid(),
                    providerId = Guid.NewGuid(),
                    resourceType = "shelter",
                    location = new { latitude = 34.0622, longitude = -118.2537, geohash = "9q5ct3" },
                    serviceRadiusKm = 25,
                    capacity = 50,
                    status = "active",
                    distanceKm = 1.8
                }
            },
            total = 2
        });

        return response;
    }
}
