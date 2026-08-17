# Azure Functions TODO Implementation Guide

## Completed Work

### ✅ Created Utilities
1. **JwtUtilities.cs** - Helper class for extracting JWT claims from HTTP requests
   - `ExtractUserIdFromToken(HttpRequestData)` - Extract user ID from Bearer token
   - `ExtractRolesFromToken(HttpRequestData)` - Extract user roles
   - `HasRole(HttpRequestData, string)` - Check for specific role
   - `HasAnyRole(HttpRequestData, params string[])` - Check for any of multiple roles
   - `ExtractClaim(HttpRequestData, string)` - Extract custom claims

### ✅ Updated Core Entities
1. **User.cs** - Added `PasswordHash` property for authentication

### ✅ Updated Core Interfaces
1. **IDuressPinService.cs** - Added `RemoveDuressPinAsync` and `RemoveSafePinAsync` methods

### ✅ Updated Infrastructure Services
1. **DuressPinService.cs** - Implemented `RemoveDuressPinAsync` and `RemoveSafePinAsync`

### ✅ Completed Functions
1. **SafetySettingsFunctions.cs** - All TODOs completed:
   - JWT extraction using `JwtUtilities.ExtractUserIdFromToken`
   - Password verification using `ICryptographyService.VerifyPassword`
   - Proper authentication checks before sensitive operations

## Remaining TODOs by Priority

### Priority 1: Authentication & User Management

#### UserProfileFunctions.cs (5 TODOs)
All JWT extraction TODOs - Replace with:
```csharp
var userId = JwtUtilities.ExtractUserIdFromToken(req);
if (userId == null)
{
    var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
    await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
    return unauthorized;
}
```

Then use `userId.Value` when passing to repositories.

**Specific TODO** at line 212:
```csharp
// TODO: Query actual job status from database/queue
// Implementation: Query WatchDbContext for deletion job status
var deletionJob = await _dbContext.UserDeletionJobs
    .FirstOrDefaultAsync(j => j.UserId == userId.Value);
```

#### ResponderOnboardingFunctions.cs (4 TODOs)
All JWT extraction TODOs - Same pattern as UserProfileFunctions.

**Specific TODO** at line 83:
```csharp
// TODO: Parse multipart/form-data for file upload
// Implementation:
var formData = await req.ReadFormAsync();
var file = formData.Files["background_check_document"];
if (file != null)
{
    using var stream = file.OpenReadStream();
    // Upload to Azure Blob Storage via EvidenceStorageService
    var blobUrl = await _evidenceStorageService.UploadFileAsync(stream, file.FileName, "background-checks");
}
```

#### SignupFunctions.cs (2 TODOs)

**Line 194 - Send verification code:**
```csharp
// TODO: Send verification code via email/SMS
await _notificationService.SendVerificationCodeAsync(requestBody.Email, verificationCode);
```

**Line 336 - Hash and store password:**
```csharp
// TODO: Hash and store password
var passwordHash = _cryptographyService.HashPassword(requestBody.Password);
user.PasswordHash = passwordHash;
await _userRepository.UpdateUserAsync(user);
```

### Priority 2: Location & Geospatial

#### LocationFunctions.cs (6 TODOs)

**Pattern for all location TODOs:**
```csharp
// For location updates - use Cosmos DB via ILocationService
var geohash = _geohashService.Encode(latitude, longitude, precision: 9);
await _locationService.UpdateLocationAsync(userId, latitude, longitude, geohash);
```

**Line 365 - Geohash calculation:**
```csharp
// TODO: Replace with actual geohash calculation
private string CalculateGeohash(double latitude, double longitude)
{
    var geohashService = new GeohashService();
    return geohashService.Encode(latitude, longitude, 9);
}
```

**Proximity search pattern:**
```csharp
// Calculate geohash for search center
var centerGeohash = _geohashService.Encode(centerLat, centerLon, 6); // Precision 6 for ~600m
var neighbors = _geohashService.GetNeighbors(centerGeohash);

// Query Cosmos DB with geohash prefix
var nearbyLocations = await _locationService.GetLocationsNearGeohashAsync(centerGeohash, neighbors);

// Filter by actual distance
var results = nearbyLocations
    .Select(loc => new {
        Location = loc,
        Distance = _geohashService.CalculateDistance(centerLat, centerLon, loc.Latitude, loc.Longitude)
    })
    .Where(x => x.Distance <= radiusMeters)
    .OrderBy(x => x.Distance)
    .ToList();
```

### Priority 3: Realtime & Notifications

#### RealtimeFunctions.cs (8 TODOs)

**All JWT extraction TODOs** - Same pattern as other functions.

**SignalR negotiation (line 55):**
```csharp
// TODO: Implement SignalR negotiation logic
var connectionInfo = await _realtimeService.GetConnectionInfoAsync(userId.Value);
var response = req.CreateResponse(HttpStatusCode.OK);
await response.WriteAsJsonAsync(new
{
    url = connectionInfo.Url,
    accessToken = connectionInfo.AccessToken
});
return response;
```

**Subscription management:**
```csharp
// Subscribe to incident updates
await _realtimeService.SubscribeToIncidentAsync(userId.Value, incidentId);

// Unsubscribe
await _realtimeService.UnsubscribeFromIncidentAsync(userId.Value, incidentId);
```

#### NotificationFunctions.cs (7 TODOs)

**Send notification:**
```csharp
// TODO: Implement notification sending logic
await _notificationService.SendPushNotificationAsync(
    userId: recipientUserId,
    title: requestBody.Title,
    body: requestBody.Body,
    data: requestBody.Data
);
```

**Broadcast to HQ:**
```csharp
// TODO: Validate HQ/Admin role
if (!JwtUtilities.HasAnyRole(req, "hq", "admin"))
{
    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
    await forbidden.WriteAsJsonAsync(new { code = "FORBIDDEN", message = "Insufficient permissions" });
    return forbidden;
}
```

**Device registration:**
```csharp
// TODO: Implement device registration logic
var deviceToken = new DeviceToken
{
    TokenId = Guid.NewGuid(),
    UserId = userId.Value,
    DeviceType = requestBody.DeviceType, // "ios" or "android"
    Token = requestBody.Token,
    CreatedAt = DateTime.UtcNow,
    LastUsedAt = DateTime.UtcNow
};
await _deviceTokenRepository.CreateAsync(deviceToken);
```

### Priority 4: Incident Management

#### IncidentFunctions.cs (3 TODOs)

**Create incident:**
```csharp
// TODO: Implement incident creation logic
var incident = new Incident
{
    IncidentId = Guid.NewGuid(),
    SummonerId = userId.Value,
    TriggerPhraseId = requestBody.TriggerPhraseId,
    DetectedAt = DateTime.UtcNow,
    Status = "active",
    Priority = requestBody.Priority ?? "medium",
    Latitude = requestBody.Latitude,
    Longitude = requestBody.Longitude,
    Geohash = _geohashService.Encode(requestBody.Latitude, requestBody.Longitude, 9),
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};
await _incidentRepository.CreateAsync(incident);
```

**Offline sync:**
```csharp
// TODO: Implement offline sync logic
// Process each action with idempotency key
foreach (var action in requestBody.Actions)
{
    // Check if already processed
    var existing = await _offlineActionRepository.GetByIdempotencyKeyAsync(action.IdempotencyKey);
    if (existing != null) continue; // Skip duplicate

    // Process based on action type
    switch (action.ActionType)
    {
        case "incident_update":
            await _incidentRepository.UpdateStatusAsync(action.IncidentId, action.NewStatus);
            break;
        case "evidence_upload":
            await _evidenceRepository.CreateAsync(action.Evidence);
            break;
    }

    // Mark as processed
    await _offlineActionRepository.MarkProcessedAsync(action.IdempotencyKey);
}
```

#### DispatchFunctions.cs (7 TODOs)

**Geohash-based responder matching:**
```csharp
// TODO: Get incident location and calculate new geohash prefix
var incident = await _incidentRepository.GetByIdAsync(incidentId);
var geohash = incident.Geohash!.Substring(0, currentRing + 4); // Expand search area
var neighbors = _geohashService.GetNeighbors(geohash);

// Query responders in geohash area
var nearbyResponders = await _responderRepository.GetAvailableInGeohashAsync(geohash, neighbors);
```

**911 escalation:**
```csharp
// TODO: Trigger 911 escalation
await _dispatchService.DispatchTo911Async(incidentId, incident.Latitude, incident.Longitude);
```

#### ResponderFunctions.cs (5 TODOs)

**Distance calculation:**
```csharp
// TODO: Calculate actual distance
var distance = _geohashService.CalculateDistance(
    incident.Latitude, incident.Longitude,
    responder.LastKnownLatitude, responder.LastKnownLongitude
);
```

**Estimated arrival time:**
```csharp
// TODO: Calculate based on distance
const double averageSpeedMetersPerMinute = 833.33; // 50 km/h = 833.33 m/min
var estimatedMinutes = (int)Math.Ceiling(distance / averageSpeedMetersPerMinute);
```

### Priority 5: Evidence & History

#### EvidenceFunctions.cs (11 TODOs)

**Upload evidence:**
```csharp
// TODO: Implement evidence upload logic
using var stream = req.Body;
var blobUrl = await _evidenceStorageService.UploadEvidenceAsync(
    stream,
    requestBody.FileName,
    incidentId
);

var evidence = new Evidence
{
    EvidenceId = Guid.NewGuid(),
    IncidentId = incidentId,
    UploadedBy = userId.Value,
    Type = requestBody.Type, // "photo", "video", "audio", "document"
    StorageUrl = blobUrl,
    FileSize = requestBody.FileSize,
    Hash = await _cryptographyService.ComputeSha256HashAsync(stream),
    UploadedAt = DateTime.UtcNow
};
await _evidenceRepository.CreateAsync(evidence);
```

**Download evidence:**
```csharp
// TODO: Stream actual file bytes
var blobStream = await _evidenceStorageService.DownloadEvidenceAsync(evidence.StorageUrl);
await blobStream.CopyToAsync(response.Body);
```

**Legal hold:**
```csharp
// TODO: Implement legal hold placement
await _evidenceRepository.PlaceLegalHoldAsync(evidenceId, reason, retentionYears: 7);
```

#### HistoryFunctions.cs (9 TODOs)

**Responder history:**
```csharp
// TODO: Implement responder history retrieval
var incidents = await _incidentRepository.GetByResponderIdAsync(userId.Value);
var history = incidents.Select(i => new {
    incident_id = i.IncidentId,
    triggered_at = i.DetectedAt,
    status = i.Status,
    your_role = i.FirstResponderId == userId.Value ? "First" : "Second"
}).ToList();
```

**Stats calculation:**
```csharp
// TODO: Implement responder stats
var totalResponses = await _incidentRepository.CountByResponderIdAsync(userId.Value);
var completedResponses = await _incidentRepository.CountByResponderIdAndStatusAsync(userId.Value, "resolved");
var averageResponseTime = await _incidentRepository.GetAverageResponseTimeAsync(userId.Value);
```

### Priority 6: Video & HQ Admin

#### VideoFunctions.cs (15 TODOs)
#### HqBroadcastFunctions.cs (6 TODOs)
#### HqAdminFunctions.cs (10 TODOs)
#### EmergencyResponseCoreFunctions.cs (9 TODOs)
#### IncidentLifecycleFunctions.cs (4 TODOs)

These are more complex and require deeper integration with Azure Media Services, SignalR, and Service Bus. Implementations should follow similar patterns:
1. Extract userId with JwtUtilities
2. Validate permissions for HQ/Admin functions
3. Use appropriate service (IRealtimeService, IHqBroadcastService, etc.)
4. Return proper HTTP status codes

### Priority 7: Evacuation System

#### EvacuationOfferFunctions.cs (5 TODOs)
#### EvacuationRequestFunctions.cs (4 TODOs)
#### EvacuationMatcherFunctions.cs (8 TODOs)
#### ShelterFunctions.cs (7 TODOs)

These require Cosmos DB queries with geohash-based proximity matching for disaster zone operations.

### Priority 8: Evidence Processing

#### EvidenceProcessingFunctions.cs (5 TODOs)

**Integrity verification:**
```csharp
// TODO: Implement integrity verification
var currentHash = await ComputeFileHashAsync(evidenceBlobUrl);
if (currentHash != evidence.Hash)
{
    // Evidence has been tampered with
    await _auditRepository.LogIntegrityViolationAsync(evidenceId);
}
```

**Thumbnail generation:**
```csharp
// TODO: Implement thumbnail generation
if (evidence.Type == "photo" || evidence.Type == "video")
{
    var thumbnailStream = await GenerateThumbnailAsync(evidenceBlobStream);
    var thumbnailUrl = await _evidenceStorageService.UploadThumbnailAsync(thumbnailStream, evidenceId);
    evidence.ThumbnailUrl = thumbnailUrl;
    await _evidenceRepository.UpdateAsync(evidence);
}
```

## Implementation Patterns Summary

### 1. JWT Extraction
```csharp
using TheWatch.Functions.Utilities;

var userId = JwtUtilities.ExtractUserIdFromToken(req);
if (userId == null)
{
    var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
    await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
    return unauthorized;
}
// Use userId.Value in subsequent calls
```

### 2. Role Validation
```csharp
if (!JwtUtilities.HasRole(req, "hq"))
{
    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
    await forbidden.WriteAsJsonAsync(new { code = "FORBIDDEN", message = "Insufficient permissions" });
    return forbidden;
}
```

### 3. Password Verification
```csharp
var user = await _userRepository.GetUserByIdAsync(userId.Value);
if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
    !_cryptographyService.VerifyPassword(requestBody.Password, user.PasswordHash))
{
    var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
    await unauthorized.WriteAsJsonAsync(new { code = "INVALID_PASSWORD", message = "Password is incorrect" });
    return unauthorized;
}
```

### 4. Geohash Operations
```csharp
var geohashService = new GeohashService();
var geohash = geohashService.Encode(latitude, longitude, 9);
var neighbors = geohashService.GetNeighbors(geohash);
var distance = geohashService.CalculateDistance(lat1, lon1, lat2, lon2);
```

### 5. Idempotency Handling
```csharp
if (req.Headers.TryGetValues("Idempotency-Key", out var keys))
{
    var idempotencyKey = keys.FirstOrDefault();
    var existing = await _idempotencyRepository.GetAsync(idempotencyKey);
    if (existing != null)
    {
        // Return cached response
        return existing.Response;
    }
}
```

## Next Steps

1. Apply JWT extraction pattern to all remaining functions (bulk find/replace possible)
2. Implement location and geohash operations in LocationFunctions
3. Complete SignupFunctions with password hashing and verification codes
4. Implement realtime subscription logic in RealtimeFunctions
5. Complete incident and dispatch management
6. Implement evidence upload/download with Azure Blob Storage
7. Add video streaming and HQ broadcast capabilities
8. Complete evacuation and shelter management

## Testing Checklist

- [ ] JWT extraction works with valid tokens
- [ ] JWT extraction rejects invalid/missing tokens
- [ ] Password verification correctly validates hashes
- [ ] Geohash calculations match expected precision
- [ ] Distance calculations are accurate
- [ ] Role-based authorization works correctly
- [ ] Idempotency keys prevent duplicate operations
- [ ] Offline sync processes queued actions
- [ ] Evidence uploads to Azure Blob Storage
- [ ] Legal holds prevent evidence deletion
