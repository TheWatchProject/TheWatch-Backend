using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Cosmos DB implementation of location service.
/// High-frequency writes with TTL for ephemeral data.
/// Uses hierarchical partition keys for efficient queries.
/// </summary>
public class CosmosLocationService : ILocationService
{
    private readonly CosmosClient _cosmosClient;
    private readonly GeohashService _geohashService;
    private readonly ILogger<CosmosLocationService> _logger;
    private const string DatabaseName = "thewatch";
    private const string ContainerName = "locations";
    private const int DefaultTtlSeconds = 3600; // 1 hour

    public CosmosLocationService(CosmosClient cosmosClient, ILogger<CosmosLocationService> logger)
    {
        _cosmosClient = cosmosClient;
        _geohashService = new GeohashService();
        _logger = logger;
    }

    public async Task<Core.Entities.LocationRecord> UpdateLocationAsync(Guid userId, double latitude, double longitude,
        double? altitude = null, double? accuracy = null, double? heading = null, double? speed = null,
        DateTime? timestamp = null, CancellationToken cancellationToken = default)
    {
        var container = _cosmosClient.GetContainer(DatabaseName, ContainerName);
        var geohash = CalculateGeohash(latitude, longitude, 9);
        var timeBucket = GetTimeBucket(timestamp ?? DateTime.UtcNow);

        var locationRecord = new CosmosLocationDocument
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId.ToString(),
            TimeBucket = timeBucket,
            GeohashPrefix = geohash[..5], // First 5 chars for partition
            Latitude = latitude,
            Longitude = longitude,
            Geohash = geohash,
            Altitude = altitude,
            Accuracy = accuracy,
            Heading = heading,
            Speed = speed,
            Timestamp = timestamp ?? DateTime.UtcNow,
            Ttl = DefaultTtlSeconds
        };

        await container.CreateItemAsync(locationRecord, cancellationToken: cancellationToken);

        // Log minimally - this is a high-frequency operation
        _logger.LogDebug("Location updated for user in geohash prefix {GeohashPrefix}", geohash[..5]);

        return new Core.Entities.LocationRecord
        {
            LocationId = Guid.Parse(locationRecord.Id),
            UserId = userId,
            Latitude = latitude,
            Longitude = longitude,
            Geohash = geohash,
            Altitude = altitude,
            Accuracy = accuracy,
            Heading = heading,
            Speed = speed,
            Timestamp = locationRecord.Timestamp,
            ServerTimestamp = DateTime.UtcNow
        };
    }

    public async Task<Core.Entities.LocationRecord?> GetUserLocationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var container = _cosmosClient.GetContainer(DatabaseName, ContainerName);
        
        var query = new QueryDefinition(
            "SELECT TOP 1 * FROM c WHERE c.userId = @userId ORDER BY c.timestamp DESC")
            .WithParameter("@userId", userId.ToString());

        var iterator = container.GetItemQueryIterator<CosmosLocationDocument>(query);
        
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            var doc = response.FirstOrDefault();
            
            if (doc != null)
            {
                return new Core.Entities.LocationRecord
                {
                    LocationId = Guid.Parse(doc.Id),
                    UserId = userId,
                    Latitude = doc.Latitude,
                    Longitude = doc.Longitude,
                    Geohash = doc.Geohash,
                    Altitude = doc.Altitude,
                    Accuracy = doc.Accuracy,
                    Heading = doc.Heading,
                    Speed = doc.Speed,
                    Timestamp = doc.Timestamp,
                    ServerTimestamp = doc.Timestamp
                };
            }
        }

        return null;
    }

    public async Task<IEnumerable<Core.Entities.LocationRecord>> GetLocationHistoryAsync(Guid userId, 
        DateTime? startTime = null, DateTime? endTime = null, CancellationToken cancellationToken = default)
    {
        var container = _cosmosClient.GetContainer(DatabaseName, ContainerName);
        
        var queryText = "SELECT * FROM c WHERE c.userId = @userId";
        if (startTime.HasValue)
            queryText += " AND c.timestamp >= @startTime";
        if (endTime.HasValue)
            queryText += " AND c.timestamp <= @endTime";
        queryText += " ORDER BY c.timestamp DESC";

        var query = new QueryDefinition(queryText)
            .WithParameter("@userId", userId.ToString());
        
        if (startTime.HasValue)
            query = query.WithParameter("@startTime", startTime.Value);
        if (endTime.HasValue)
            query = query.WithParameter("@endTime", endTime.Value);

        var results = new List<Core.Entities.LocationRecord>();
        var iterator = container.GetItemQueryIterator<CosmosLocationDocument>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response.Select(doc => new Core.Entities.LocationRecord
            {
                LocationId = Guid.Parse(doc.Id),
                UserId = userId,
                Latitude = doc.Latitude,
                Longitude = doc.Longitude,
                Geohash = doc.Geohash,
                Altitude = doc.Altitude,
                Accuracy = doc.Accuracy,
                Heading = doc.Heading,
                Speed = doc.Speed,
                Timestamp = doc.Timestamp,
                ServerTimestamp = doc.Timestamp
            }));
        }

        return results;
    }

    public async Task<IEnumerable<NearbyUser>> FindNearbyUsersAsync(double latitude, double longitude,
        int radiusMeters = 500, string userType = "responder", Guid? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        var container = _cosmosClient.GetContainer(DatabaseName, ContainerName);
        var centerGeohash = CalculateGeohash(latitude, longitude, 9);
        
        // Get neighboring geohashes for the search area
        var precision = GetGeohashPrecisionForRadius(radiusMeters);
        var searchPrefix = centerGeohash[..precision];

        // Query using geohash prefix
        var cutoffTime = DateTime.UtcNow.AddMinutes(-15); // Only consider recent locations
        var query = new QueryDefinition(
            @"SELECT DISTINCT c.userId, c.latitude, c.longitude, c.geohash, c.timestamp 
              FROM c 
              WHERE STARTSWITH(c.geohashPrefix, @prefix)
                AND c.timestamp >= @cutoffTime
              ORDER BY c.timestamp DESC")
            .WithParameter("@prefix", searchPrefix)
            .WithParameter("@cutoffTime", cutoffTime);

        var results = new List<NearbyUser>();
        var seenUsers = new HashSet<string>();
        var iterator = container.GetItemQueryIterator<CosmosLocationDocument>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            
            foreach (var doc in response)
            {
                // Skip if already seen or is excluded user
                if (seenUsers.Contains(doc.UserId))
                    continue;
                if (excludeUserId.HasValue && doc.UserId == excludeUserId.Value.ToString())
                    continue;

                seenUsers.Add(doc.UserId);

                // Calculate actual distance
                var distance = CalculateDistanceMeters(latitude, longitude, doc.Latitude, doc.Longitude);
                
                if (distance <= radiusMeters)
                {
                    results.Add(new NearbyUser
                    {
                        UserId = Guid.Parse(doc.UserId),
                        Latitude = doc.Latitude,
                        Longitude = doc.Longitude,
                        DistanceMeters = distance,
                        UserType = userType,
                        LastSeen = doc.Timestamp
                    });
                }
            }
        }

        return results.OrderBy(u => u.DistanceMeters);
    }

    public string CalculateGeohash(double latitude, double longitude, int precision = 9)
    {
        return _geohashService.Encode(latitude, longitude, precision);
    }

    public async Task<BatchLocationUpdateResult> BatchUpdateLocationsAsync(IEnumerable<LocationUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        var successful = 0;
        var failed = 0;

        foreach (var update in updates)
        {
            try
            {
                await UpdateLocationAsync(update.UserId, update.Latitude, update.Longitude,
                    update.Altitude, update.Accuracy, update.Heading, update.Speed,
                    update.Timestamp, cancellationToken);
                successful++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update location for user");
                failed++;
            }
        }

        return new BatchLocationUpdateResult
        {
            SuccessfulUpdates = successful,
            FailedUpdates = failed
        };
    }

    public async Task<IEnumerable<Core.Entities.LocationRecord>> GetExpiredLocationsAsync(
        DateTime cutoffTime,
        CancellationToken cancellationToken = default)
    {
        var container = _cosmosClient.GetContainer(DatabaseName, ContainerName);

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.timestamp < @cutoffTime ORDER BY c.timestamp ASC")
            .WithParameter("@cutoffTime", cutoffTime);

        var results = new List<Core.Entities.LocationRecord>();
        var iterator = container.GetItemQueryIterator<CosmosLocationDocument>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response.Select(doc => new Core.Entities.LocationRecord
            {
                LocationId = Guid.Parse(doc.Id),
                UserId = Guid.Parse(doc.UserId),
                Latitude = doc.Latitude,
                Longitude = doc.Longitude,
                Geohash = doc.Geohash,
                Altitude = doc.Altitude,
                Accuracy = doc.Accuracy,
                Heading = doc.Heading,
                Speed = doc.Speed,
                Timestamp = doc.Timestamp,
                ServerTimestamp = doc.Timestamp
            }));
        }

        _logger.LogInformation("Found {Count} expired location records older than {CutoffTime}", results.Count, cutoffTime);
        return results;
    }

    public async Task DeleteLocationAsync(Guid locationId, CancellationToken cancellationToken = default)
    {
        var container = _cosmosClient.GetContainer(DatabaseName, ContainerName);

        try
        {
            // First, we need to find the document to get its partition key
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                .WithParameter("@id", locationId.ToString());

            var iterator = container.GetItemQueryIterator<CosmosLocationDocument>(query);

            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                var doc = response.FirstOrDefault();

                if (doc != null)
                {
                    // Delete using id and partition key
                    await container.DeleteItemAsync<CosmosLocationDocument>(
                        doc.Id,
                        new PartitionKey(doc.UserId),
                        cancellationToken: cancellationToken);

                    _logger.LogDebug("Deleted location record {LocationId}", locationId);
                }
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Location already deleted or expired via TTL - this is fine
            _logger.LogDebug("Location record {LocationId} not found (may have expired via TTL)", locationId);
        }
    }

    private static string GetTimeBucket(DateTime timestamp)
    {
        // Bucket by hour for partition key distribution
        return timestamp.ToString("yyyy-MM-dd-HH");
    }

    private static int GetGeohashPrecisionForRadius(int radiusMeters)
    {
        // Approximate geohash precision based on radius
        return radiusMeters switch
        {
            < 100 => 7,    // ~76m precision
            < 500 => 6,    // ~610m precision
            < 2500 => 5,   // ~2.4km precision
            < 20000 => 4,  // ~20km precision
            _ => 3         // ~78km precision
        };
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth's radius in meters
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private class CosmosLocationDocument
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string TimeBucket { get; set; } = string.Empty;
        public string GeohashPrefix { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Geohash { get; set; } = string.Empty;
        public double? Altitude { get; set; }
        public double? Accuracy { get; set; }
        public double? Heading { get; set; }
        public double? Speed { get; set; }
        public DateTime Timestamp { get; set; }
        public int Ttl { get; set; }
    }
}
