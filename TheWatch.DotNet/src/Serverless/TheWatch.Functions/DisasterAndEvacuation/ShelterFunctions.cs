using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for temporary shelter management during disasters.
/// Implements shelter endpoints from evacuation-api.yaml
/// Tracks shelter capacity with check-in/check-out operations
/// </summary>
public class ShelterFunctions
{
    private readonly ILogger<ShelterFunctions> _logger;

    public ShelterFunctions(ILogger<ShelterFunctions> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// GET /shelters - List shelters near a location
    /// Returns shelters within specified radius, ordered by proximity and availability
    /// </summary>
    [Function("ListShelters")]
    public async Task<HttpResponseData> ListShelters(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "shelters")] HttpRequestData req)
    {
        _logger.LogInformation("Listing shelters");

        // TODO: Implement shelter listing logic
        // 1. Parse query parameters: latitude, longitude, radiusKm (default 25km, max 300km)
        // 2. Calculate geohash from query location
        // 3. Query temporary_shelters where:
        //    - Geohash prefix matches (for proximity)
        //    - Status IN ('open', 'emergency_only')
        //    - Available_until > NOW() OR available_until IS NULL (ongoing)
        // 4. Calculate haversine distance for each shelter
        // 5. Filter by radiusKm
        // 6. Order by:
        //    a. Status (open before emergency_only)
        //    b. Has available capacity (capacity > current_occupancy)
        //    c. Distance (closer first)
        // 7. Return array of Shelter objects with capacity info

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new[]
        {
            new
            {
                shelterId = Guid.NewGuid().ToString(),
                name = "Community Center Emergency Shelter",
                location = new
                {
                    latitude = 34.0522,
                    longitude = -118.2437,
                    geohash = "9q5ct2",
                    address = "123 Main St, Los Angeles, CA 90012"
                },
                capacity = new
                {
                    total = 100,
                    available = 45
                },
                shelterType = "community_center",
                acceptsPets = true,
                petCapacity = 20,
                wheelchairAccessible = true,
                amenities = new[] { "kitchen", "showers", "medical", "wifi", "pet_area" },
                status = "open",
                contactPhone = "+13105551234",
                distanceKm = 2.3
            },
            new
            {
                shelterId = Guid.NewGuid().ToString(),
                name = "St. Mary's Church Shelter",
                location = new
                {
                    latitude = 34.0622,
                    longitude = -118.2537,
                    geohash = "9q5ct3",
                    address = "456 Oak Ave, Los Angeles, CA 90013"
                },
                capacity = new
                {
                    total = 50,
                    available = 12
                },
                shelterType = "church",
                acceptsPets = false,
                petCapacity = 0,
                wheelchairAccessible = true,
                amenities = new[] { "kitchen", "restrooms", "cots" },
                status = "open",
                contactPhone = "+13105555678",
                distanceKm = 3.7
            }
        });

        return response;
    }

    /// <summary>
    /// POST /shelters - Register a new temporary shelter
    /// Allows providers to register shelters (home, church, business, etc.)
    /// </summary>
    [Function("RegisterShelter")]
    public async Task<HttpResponseData> RegisterShelter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "shelters")] HttpRequestData req)
    {
        _logger.LogInformation("Registering new shelter");

        // TODO: Implement shelter registration logic
        // 1. Parse Shelter creation payload from request body
        // 2. Validate provider authentication
        // 3. Check Idempotency-Key header
        // 4. Validate shelter details:
        //    - shelter_type (home, apartment, church, school, business, etc.)
        //    - location (lat/lng)
        //    - capacity (total occupancy)
        //    - amenities, accessibility features
        // 5. Calculate geohash from location
        // 6. Create temporary_shelters record with status 'open'
        // 7. Set current_occupancy = 0
        // 8. Return Shelter object

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            shelterId = Guid.NewGuid().ToString(),
            providerId = Guid.NewGuid(),
            name = "Private Home Shelter",
            location = new
            {
                latitude = 34.0722,
                longitude = -118.2637,
                geohash = "9q5ct4",
                address = "789 Elm St, Los Angeles, CA 90014"
            },
            capacity = new
            {
                total = 8,
                available = 8
            },
            shelterType = "home",
            acceptsPets = true,
            petCapacity = 2,
            wheelchairAccessible = false,
            amenities = new[] { "kitchen", "showers", "wifi", "laundry" },
            status = "open",
            contactPhone = "+13105559012",
            availableFrom = DateTime.UtcNow,
            availableUntil = DateTime.UtcNow.AddDays(7),
            createdAt = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// POST /shelters/{shelterId}/check-in - Check into a shelter
    /// Records evacuee check-in and updates shelter capacity
    /// </summary>
    [Function("CheckInToShelter")]
    public async Task<HttpResponseData> CheckInToShelter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "shelters/{shelterId}/check-in")] HttpRequestData req,
        string shelterId)
    {
        _logger.LogInformation("Checking in to shelter: {ShelterId}", shelterId);

        // TODO: Implement shelter check-in logic
        // 1. Parse check-in payload from request body (evacueeId, party_size, has_pets, pet_count, special_needs)
        // 2. Validate evacuee authentication
        // 3. Check Idempotency-Key header
        // 4. Parse shelterId as Guid
        // 5. Retrieve temporary_shelters record
        // 6. Verify shelter status is 'open' or 'emergency_only'
        // 7. Verify sufficient capacity:
        //    - current_occupancy + party_size <= capacity
        //    - If has_pets, verify pet capacity available
        // 8. Create shelter_check_ins record (check_in_time = NOW(), check_out_time = NULL)
        // 9. Update temporary_shelters.current_occupancy += party_size
        // 10. Trigger database trigger to check if shelter is now full (capacity reached)
        // 11. Send notification to shelter provider about new arrival
        // 12. Return check-in confirmation with check_in_id

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            checkInId = Guid.NewGuid(),
            shelterId,
            evacueeId = Guid.NewGuid(),
            partySize = 3,
            hasPets = true,
            petCount = 1,
            checkInTime = DateTime.UtcNow,
            shelter = new
            {
                name = "Community Center Emergency Shelter",
                address = "123 Main St, Los Angeles, CA 90012",
                capacity = new
                {
                    total = 100,
                    available = 42 // Updated after check-in
                }
            }
        });

        return response;
    }

    /// <summary>
    /// POST /shelters/{shelterId}/check-out - Check out of a shelter
    /// Records evacuee check-out and frees up shelter capacity
    /// </summary>
    [Function("CheckOutOfShelter")]
    public async Task<HttpResponseData> CheckOutOfShelter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "shelters/{shelterId}/check-out")] HttpRequestData req,
        string shelterId)
    {
        _logger.LogInformation("Checking out of shelter: {ShelterId}", shelterId);

        // TODO: Implement shelter check-out logic
        // 1. Parse check-out payload from request body (evacueeId)
        // 2. Validate evacuee authentication
        // 3. Check Idempotency-Key header
        // 4. Parse shelterId as Guid
        // 5. Retrieve active shelter_check_ins record for evacuee at this shelter (check_out_time IS NULL)
        // 6. Update shelter_check_ins.check_out_time = NOW()
        // 7. Update temporary_shelters.current_occupancy -= party_size
        // 8. Trigger database trigger to update shelter status if was 'full'
        // 9. Send notification to shelter provider about departure
        // 10. Return check-out confirmation

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            checkInId = Guid.NewGuid(),
            shelterId,
            evacueeId = Guid.NewGuid(),
            checkOutTime = DateTime.UtcNow,
            durationHours = 36.5,
            shelter = new
            {
                name = "Community Center Emergency Shelter",
                capacity = new
                {
                    total = 100,
                    available = 45 // Updated after check-out
                }
            }
        });

        return response;
    }

    /// <summary>
    /// GET /shelters/{shelterId}/capacity - Get current shelter capacity
    /// Real-time capacity check for shelter status
    /// </summary>
    [Function("GetShelterCapacity")]
    public async Task<HttpResponseData> GetShelterCapacity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "shelters/{shelterId}/capacity")] HttpRequestData req,
        string shelterId)
    {
        _logger.LogInformation("Getting shelter capacity: {ShelterId}", shelterId);

        // TODO: Implement shelter capacity retrieval logic
        // 1. Validate authentication (optional - public endpoint)
        // 2. Parse shelterId as Guid
        // 3. Retrieve temporary_shelters record
        // 4. Return capacity details (total, available, current_occupancy, status)
        // 5. Include pet capacity if applicable
        // 6. Return last updated timestamp

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            shelterId,
            capacity = new
            {
                total = 100,
                available = 45,
                currentOccupancy = 55,
                percentFull = 55.0
            },
            petCapacity = new
            {
                total = 20,
                available = 12,
                currentOccupancy = 8
            },
            status = "open",
            lastUpdated = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// PATCH /shelters/{shelterId} - Update shelter details
    /// Allows provider to update capacity, status, or amenities
    /// </summary>
    [Function("UpdateShelter")]
    public async Task<HttpResponseData> UpdateShelter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "shelters/{shelterId}")] HttpRequestData req,
        string shelterId)
    {
        _logger.LogInformation("Updating shelter: {ShelterId}", shelterId);

        // TODO: Implement shelter update logic
        // 1. Parse update payload from request body (capacity, status, amenities, available_until, etc.)
        // 2. Validate provider authentication and ownership
        // 3. Check Idempotency-Key header
        // 4. Parse shelterId as Guid
        // 5. Validate new capacity >= current_occupancy (can't reduce below current occupancy)
        // 6. Update temporary_shelters record
        // 7. If status changed to 'closed', notify any checked-in evacuees
        // 8. Return updated Shelter object

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            shelterId,
            providerId = Guid.NewGuid(),
            name = "Community Center Emergency Shelter",
            capacity = new
            {
                total = 120, // Increased capacity
                available = 65
            },
            status = "open",
            amenities = new[] { "kitchen", "showers", "medical", "wifi", "pet_area", "generator" }, // Added generator
            updatedAt = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// Timer trigger to update shelter statuses and send capacity alerts
    /// Runs every 10 minutes to check shelter capacity and send alerts
    /// Corresponds to "Shelter Capacity Monitor" background job in todo.md
    /// </summary>
    [Function("ShelterCapacityMonitor")]
    public async Task MonitorShelterCapacity(
        [TimerTrigger("0 */10 * * * *")] TimerInfo timer,
        FunctionContext context)
    {
        _logger.LogInformation("Running shelter capacity monitor");

        // TODO: Implement shelter capacity monitoring
        // 1. Query temporary_shelters where status = 'open' or 'emergency_only'
        // 2. For each shelter:
        //    a. Calculate percent_full = (current_occupancy / capacity) * 100
        //    b. If percent_full > 80%, flag as "nearing capacity"
        //    c. If current_occupancy >= capacity and status != 'full', update status to 'full'
        //    d. If current_occupancy < capacity and status = 'full', update status to 'open'
        // 3. Identify geographic areas with insufficient shelter capacity:
        //    a. Group shelters by geohash prefix (regional aggregation)
        //    b. Calculate total_capacity and total_occupancy per region
        //    c. If all shelters in region are >80% full, send alert to HQ
        //    d. Recommend recruitment of additional shelter providers in that area
        // 4. Log capacity metrics for analytics dashboard

        _logger.LogInformation("Shelter capacity monitoring complete");
    }
}
