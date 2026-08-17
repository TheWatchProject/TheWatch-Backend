# Azure Functions Implementation - Location, Notifications, and Real-time Sync

## Overview

This implementation adds five new Azure Functions modules to The Watch platform:

1. **LocationFunctions.cs** - Location tracking and geospatial queries
2. **NotificationFunctions.cs** - Push notifications, SMS, and device management
3. **RealtimeFunctions.cs** - SignalR real-time communication
4. **HqBroadcastFunctions.cs** - HQ "Voice of God" broadcasts
5. **DisasterZoneFunctions.cs** - Disaster zone management and evacuation routing

## Implementation Summary

### 1. LocationFunctions.cs

Implements endpoints from `APIS/location-api.yaml`:

#### Endpoints Implemented:
- `PUT /users/{userId}/location` - Update user location
  - High-frequency endpoint (500 req/min limit per user)
  - Calculates 9-character geohash (~5m accuracy)
  - 1-hour TTL on location data
  - Streams to incident timeline if user in active incident

- `GET /users/{userId}/location` - Get last known location
  - Retrieves from Redis cache
  - Returns 404 if expired or not found

- `GET /users/{userId}/location/history` - Get location breadcrumbs
  - Historical location points for post-incident analysis
  - Respects 1-hour TTL unless incident-associated

- `GET /spatial/nearby-users` - Find nearby users
  - Used by dispatch system to find eligible responders
  - Geohash-based proximity search
  - Filters by user type (responder, community_member, all)
  - Haversine distance calculation

- `GET /spatial/geohash` - Calculate geohash utility
  - Client-side geohash calculation helper
  - Configurable precision (1-12 chars)

- `POST /location/batch` - Batch location updates
  - For offline sync scenarios
  - Requires idempotency keys

#### Key Features:
- Ephemeral data with 1-hour TTL
- Geohash-first queries for efficiency
- Redis cache for real-time access
- Rate limiting (500 req/min)

### 2. NotificationFunctions.cs

Implements endpoints from `APIS/notifications-api.yaml`:

#### Endpoints Implemented:
- `POST /notifications/send` - Send notification to user
  - Multi-channel support (push, SMS)
  - Priority levels (normal, high, critical)
  - Async delivery via Service Bus

- `POST /notifications/broadcast` - Broadcast to multiple users
  - HQ/Admin only
  - Audience targeting (all_users, all_responders, admins)

- `POST /notifications/incidents/{incidentId}/responders` - Alert incident responders
  - High-priority incident-scoped alerts
  - Idempotency support
  - SMS fallback option
  - TTS audio support

- `POST /sms/send` - Send SMS
  - E.164 phone number format
  - Purpose tracking (incident_alert, verification, general)

- `POST /devices/register` - Register device token
  - APNs (iOS) and FCM (Android/Web)
  - Device metadata tracking

- `DELETE /devices/{deviceId}` - Unregister device

#### Service Bus Trigger:
- `ProcessNotificationQueue` - Async notification delivery
  - Priority-based queue processing
  - Retry logic with exponential backoff
  - Delivery confirmation tracking

#### Key Features:
- Multi-channel delivery (push + SMS)
- Priority-based queues
- Delivery confirmation tracking
- Automatic retry (max 3 attempts)

### 3. RealtimeFunctions.cs

Implements endpoints from `APIS/realtime-sync-api.yaml`:

#### Endpoints Implemented:
- `POST /negotiate` - SignalR connection negotiation
  - Returns SignalR endpoint + access token
  - 5-minute TTL on access token

- `POST /subscriptions/incidents/{incidentId}` - Subscribe to incident
  - Real-time incident updates
  - Authorization checks (HQ/admin, assigned responder, summoner)

- `DELETE /subscriptions/incidents/{incidentId}` - Unsubscribe from incident

- `POST /subscriptions/hq-broadcasts` - Subscribe to HQ broadcasts
  - Responders only
  - "Voice of God" critical alerts

- `DELETE /subscriptions/hq-broadcasts` - Unsubscribe from HQ broadcasts

- `POST /subscriptions/evacuations/{evacuationId}` - Subscribe to evacuation
  - Real-time evacuation tracking

- `POST /streaming/location/{sessionId}` - Start location streaming
  - Ephemeral location streaming during incidents
  - 1-hour TTL on location data
  - Rate limit: 1 update per 5 seconds

- `DELETE /streaming/location/{sessionId}` - Stop location streaming

- `POST /streaming/location/{sessionId}/update` - Push location update
  - Broadcasts to all session subscribers

#### Service Bus Trigger:
- `BroadcastIncidentEvent` - Broadcast events to incident subscribers
  - Triggered by incident state changes

#### Key Features:
- Azure SignalR Service (Serverless mode)
- JWT authentication
- Group-based subscriptions
- Automatic cleanup of stale connections

### 4. HqBroadcastFunctions.cs

Implements HQ broadcast functionality:

#### Endpoints Implemented:
- `POST /hq/broadcast` - Send HQ broadcast
  - Critical alerts to all incident responders
  - Text + optional TTS audio
  - SMS fallback
  - Delivery confirmation tracking

- `GET /hq/broadcast/{broadcastId}/status` - Get delivery status
  - Shows which responders received/acknowledged
  - Used by HQ for verification

- `POST /hq/broadcast/{broadcastId}/confirm` - Confirm delivery
  - Called by responder clients
  - Tracks acknowledgment timestamp

- `POST /hq/incident/{incidentId}/command` - Send incident command
  - Predefined safety commands:
    - RETREAT: Immediate withdrawal
    - STAND_DOWN: Situation resolved
    - WEAPON_SPOTTED: Extreme caution
    - POLICE_ARRIVING: Law enforcement en route
    - MEDICAL_NEEDED: Medical assistance requested

#### Background Jobs:
- `RetryFailedBroadcasts` - Retry failed deliveries
  - Processes dead-letter queue
  - Max 3 attempts with exponential backoff
  - SMS fallback after failures

- `MonitorBroadcastDeliveries` - Monitor pending confirmations
  - Runs every minute
  - Alerts HQ for critical broadcasts without confirmation
  - Triggers SMS fallback for unconfirmed deliveries

#### Key Features:
- Critical priority notifications
- Delivery confirmation tracking
- Automatic retry with exponential backoff
- SMS fallback for failed push
- TTS audio support
- Predefined safety commands

### 5. DisasterZoneFunctions.cs

Implements disaster zone management from `APIS/evacuation-api.yaml` and `APIS/hq-admin-api.yaml`:

#### HTTP Endpoints Implemented:
- `GET /disaster-zones` - List active disaster zones (public)
  - Filters: active (default true), disaster_type, severity
  - Orders by severity DESC (catastrophic > mandatory_evacuation > warning > watch > advisory)
  - Returns GeoJSON boundaries with geohash prefixes

- `POST /hq/disaster-zones` - Create disaster zone (HQ/Admin only)
  - Validates GeoJSON polygon/multipolygon boundaries
  - Extracts geohash prefixes from boundary for spatial indexing
  - Calculates center point and radius
  - Estimates affected population from user geohash queries
  - Triggers notifications to users in affected geohash prefixes

- `PATCH /hq/disaster-zones/{zoneId}` - Update disaster zone (HQ/Admin only)
  - Updates severity, evacuation_order, expires_at
  - Sends critical notification if severity/evacuation order increased

- `DELETE /hq/disaster-zones/{zoneId}` - Deactivate disaster zone (HQ/Admin only)
  - Marks zone as inactive (is_active = false)
  - Sends "all clear" notification to affected users
  - Archives zone data for historical records

- `POST /hq/disaster-zones/{zoneId}/notify` - Send notification to zone users
  - Queries users by geohash prefix matching zone boundaries
  - Sends high-priority push notifications via notification-queue
  - Returns notification delivery stats

- `POST /routes/evacuation-route` - Calculate safe evacuation route
  - Accepts origin, destination, disaster types to avoid
  - Queries active disaster zones to build avoidance polygons
  - Calls Azure Maps/Google Maps API for route calculation
  - Optionally includes fuel stops and shelter waypoints
  - Returns route polyline, ETA, distance, warnings

#### Timer Trigger:
- `DisasterZoneExpirationChecker` (hourly, cron: `0 0 * * * *`)
  - Queries zones where is_active = true AND expires_at < NOW()
  - Deactivates expired zones
  - Sends "all clear" notifications to affected users
  - Archives zone data for historical records

#### Key Features:
- GeoJSON boundary validation (polygon/multipolygon)
- Geohash prefix indexing (precision 6, ~1.2km grid)
- Fast user-in-zone queries via geohash prefix matching
- Severity-ordered zone listing
- Safe evacuation route calculation with disaster avoidance
- Fuel station and shelter integration for evacuation routes
- Automated zone expiration with all-clear notifications

## Supporting Files Created

### Domain Entities (`TheWatch.Core/Entities/`):

1. **LocationRecord.cs** - User location with geohash
   - Ephemeral data (1-hour TTL)
   - Optional incident/evacuation association

2. **HqBroadcast.cs** - HQ broadcast message
   - Delivery confirmation tracking
   - Includes `BroadcastDeliveryConfirmation` child entity

### Service Interfaces (`TheWatch.Core/Interfaces/`):

1. **ILocationService.cs** - Location tracking operations
   - Location update/retrieval
   - Proximity searches
   - Geohash calculation
   - Batch updates

2. **IRealtimeService.cs** - SignalR operations
   - Subscription management
   - Event broadcasting
   - Location streaming

3. **IHqBroadcastService.cs** - HQ broadcast operations
   - Broadcast sending
   - Delivery tracking
   - Command management

4. **IDisasterZoneService.cs** - Disaster zone operations
   - Zone CRUD operations
   - GeoJSON boundary validation
   - Geohash prefix extraction
   - User-in-zone queries
   - Evacuation route calculation

### Utility Services (`TheWatch.Core/Services/`):

1. **GeohashService.cs** - Geohash calculation and geospatial utilities
   - Encode/decode geohashes
   - Get neighboring geohashes
   - Haversine distance calculation
   - Precision recommendations

## Next Steps

### 1. Implementation Requirements

The functions contain TODO comments marking areas requiring full implementation:

#### LocationFunctions.cs:
- [ ] Implement JWT authentication validation
- [ ] Add Redis cache integration for location storage
- [ ] Implement rate limiting (500 req/min per user)
- [ ] Add incident stream integration
- [ ] Implement proximity search with geohash neighbors
- [ ] Add database storage for incident-associated locations

#### NotificationFunctions.cs:
- [ ] Integrate APNs/FCM providers
- [ ] Add Twilio/Azure Communication Services for SMS
- [ ] Implement Service Bus queue producers
- [ ] Add device token database storage
- [ ] Implement delivery confirmation tracking
- [ ] Add retry logic with exponential backoff

#### RealtimeFunctions.cs:
- [ ] Configure Azure SignalR Service connection
- [ ] Implement JWT token extraction
- [ ] Add SignalR group management
- [ ] Implement subscription database storage
- [ ] Add rate limiting for location updates
- [ ] Implement connection cleanup

#### HqBroadcastFunctions.cs:
- [ ] Implement role-based authorization (HQ/Admin)
- [ ] Add database storage for broadcasts
- [ ] Implement delivery confirmation tracking
- [ ] Add responder query by incident
- [ ] Implement retry logic
- [ ] Add monitoring and alerting for failed deliveries

#### DisasterZoneFunctions.cs:
- [ ] Implement JWT authentication validation for HQ endpoints
- [ ] Add role-based authorization (HQ/Admin) for create/update/delete/notify
- [ ] Implement GeoJSON polygon validation
- [ ] Add geohash prefix extraction from GeoJSON boundaries
- [ ] Calculate center point and radius from polygon
- [ ] Implement disaster zone CRUD database operations
- [ ] Add user query by geohash prefix for affected population estimate
- [ ] Implement zone notification via notification-queue
- [ ] Integrate Azure Maps / Google Maps API for route calculation
- [ ] Add disaster zone avoidance polygons to route requests
- [ ] Query fuel stations along evacuation routes
- [ ] Query shelters with available capacity for route waypoints
- [ ] Implement zone expiration timer trigger logic
- [ ] Add "all clear" notification on zone deactivation
- [ ] Archive expired zone data for historical records
- [ ] Add Idempotency-Key validation for evacuation route requests

### 2. Database Schema Updates

Add these tables to your database (see `todo.md` for full schema):

- `Location_Records` - Ephemeral location data (optional, can use Redis only)
- `Device_Tokens` - Already in schema
- `SignalR_Subscriptions` - Already in schema
- `Hq_Broadcasts` - HQ broadcast records
- `Broadcast_Delivery_Confirmations` - Delivery tracking

### 3. Infrastructure Configuration

#### Azure Resources Needed:
- Azure SignalR Service (Serverless mode)
- Azure Service Bus (notification queue)
- Azure Redis Cache (location data)
- Azure Storage (for dead-letter queues)

#### Environment Variables:
Add to `local.settings.json`:
```json
{
  "Values": {
    "AzureSignalRConnectionString": "...",
    "ServiceBusConnection": "...",
    "RedisConnectionString": "...",
    "ApnsKeyPath": "...",
    "FcmServerKey": "...",
    "TwilioAccountSid": "...",
    "TwilioAuthToken": "...",
    "AzureCommunicationServicesConnection": "..."
  }
}
```

### 4. Service Implementation

Create concrete service implementations in `TheWatch.Infrastructure/`:

- `LocationService.cs` - Implements `ILocationService`
- `RealtimeService.cs` - Implements `IRealtimeService`
- `HqBroadcastService.cs` - Implements `IHqBroadcastService`
- `DisasterZoneService.cs` - Implements `IDisasterZoneService`
- `NotificationService.cs` - Update existing to support new features

### 5. Dependency Injection

Update `TheWatch.Functions/Program.cs`:
```csharp
services.AddSingleton<GeohashService>();
services.AddScoped<ILocationService, LocationService>();
services.AddScoped<IRealtimeService, RealtimeService>();
services.AddScoped<IHqBroadcastService, HqBroadcastService>();
services.AddScoped<IDisasterZoneService, DisasterZoneService>();
```

### 6. NuGet Packages

Add these packages to `TheWatch.Functions.csproj`:
```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.SignalRService" Version="1.x" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.ServiceBus" Version="5.x" />
<PackageReference Include="StackExchange.Redis" Version="2.x" />
```

### 7. Testing

Create integration tests for:
- Location update rate limiting
- Geohash-based proximity searches
- Notification delivery via Service Bus
- SignalR subscription management
- HQ broadcast delivery confirmation
- Failed delivery retry logic
- Disaster zone GeoJSON polygon validation
- Geohash prefix extraction from boundaries
- User-in-zone queries by geohash prefix
- Evacuation route calculation with disaster avoidance
- Zone expiration timer trigger
- All-clear notification delivery

### 8. Monitoring

Set up Application Insights queries for:
- Location update rate (req/min per user)
- Notification delivery latency
- SignalR connection failures
- HQ broadcast delivery confirmation rates
- Failed notification retry counts
- Active disaster zones by type and severity
- Zone notification delivery success rate
- Evacuation route calculation latency
- Users notified per zone
- Zone expiration job execution metrics

## Architecture Notes

### Geohash Strategy
- Default 9-character precision (~5m accuracy)
- Server-side calculation for consistency
- Neighbors expansion for larger search radii
- See `GeohashService.cs` for implementation details

### Ephemeral Data
- Location data: 1-hour TTL (unless incident-associated)
- SignalR subscriptions: Expire with connection
- Broadcast confirmations: Retained for 7 days for audit

### Priority Queues
Notifications use priority-based routing:
- **Critical**: Responder distress, HQ broadcasts (RETREAT, WEAPON_SPOTTED)
- **High**: Dispatch alerts, incident coordination
- **Normal**: General notifications

### Delivery Confirmation
HQ broadcasts track delivery status:
1. Queued → Sent → Delivered → Acknowledged
2. Max 3 retry attempts (1m, 5m, 15m backoff)
3. SMS fallback after push failures
4. HQ dashboard shows real-time confirmation status

### Rate Limiting
- Location updates: 500 req/min per user
- Location streaming: 1 update per 5 seconds
- SignalR negotiation: Standard Azure Function limits

## References

- [APIS/location-api.yaml](../../APIS/location-api.yaml) - Location API spec
- [APIS/notifications-api.yaml](../../APIS/notifications-api.yaml) - Notifications API spec
- [APIS/realtime-sync-api.yaml](../../APIS/realtime-sync-api.yaml) - Real-time API spec
- [todo.md](../../todo.md) - Database schema and background jobs
- [infra/geospatial-queries.md](../../infra/geospatial-queries.md) - Geospatial query optimization
- [infra/rate-limiting.md](../../infra/rate-limiting.md) - Rate limiting strategies
