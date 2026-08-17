# Azure Functions Implementation Summary

## Overview

Three comprehensive Azure Functions classes have been implemented for The Watch emergency response platform:

1. **DispatchFunctions.cs** - Core dispatch orchestration
2. **IncidentLifecycleFunctions.cs** - Incident state management
3. **ResponderFunctions.cs** - Responder operations and safety

## Implementation Details

### 1. DispatchFunctions.cs

**Purpose**: Emergency dispatch orchestration with geohash-based proximity queries and multi-tier escalation.

**Key Functions**:
- `CreateEmergencyDispatch` (POST /dispatch/alert)
  - Calculates geohash prefix from incident location
  - Finds nearby responders using geohash proximity (precision 9 = ~50m radius)
  - Queues dispatch notifications via Service Bus
  - Schedules auto-expansion if threshold not met within time window (default: 180s)

- `GetDispatchStatus` (GET /dispatch/{dispatchId}/status)
  - Returns current dispatch state including:
    - Current geohash ring (1-4 before escalation)
    - Responders contacted/accepted/declined per ring
    - Time to response threshold
    - Next expansion schedule

- `ExpandDispatchRadius` (POST /dispatch/{dispatchId}/expand)
  - Expands search to next geohash ring (reduces precision by 1)
  - Escalates to 911 if max rings (4) exceeded
  - Tracks expansion history for metrics

- `DispatchExpansionMonitor` (Timer: every 30 seconds)
  - Monitors active dispatches
  - Auto-expands if response threshold not met after time window
  - Logs expansion decisions for analytics

- `ProcessDispatchNotification` (Service Bus: dispatch-queue)
  - Sends push notifications to responders
  - Tracks delivery confirmations
  - Handles retries via Service Bus dead-letter queue

**Geohash Strategy**:
- Precision 9 (~50m) for initial dispatch ring
- Precision 8 (~200m) for ring 2
- Precision 7 (~1km) for ring 3
- Precision 6 (~5km) for ring 4
- Escalate to 911 if ring 4 exhausted

**Key Features**:
- Geohash-based proximity queries (STARTSWITH on geohash prefix)
- Multi-tier escalation (up to 4 rings before 911)
- Auto-expansion with configurable time windows
- Comprehensive dispatch metrics and history

---

### 2. IncidentLifecycleFunctions.cs

**Purpose**: Manage incident state machine with strict transition validation and responder coordination.

**Key Functions**:
- `UpdateIncidentStatus` (PATCH /incidents/{incidentId}/status)
  - Validates state machine transitions
  - Broadcasts status updates to subscribed clients
  - Supports idempotent updates

- `AcceptDispatch` (POST /incidents/{incidentId}/accept)
  - Assigns First or Second role based on acceptance order
  - First acceptance = "First" responder (primary assessment)
  - Second acceptance = "Second" responder (validation/disagreement authority)
  - Notifies summoner that responder is en route
  - Prevents more than 2 responders per incident

- `DeclineDispatch` (POST /incidents/{incidentId}/decline)
  - Marks responder as declined
  - Triggers dispatch to next candidate in queue
  - Logs decline reason for analytics

- `MarkEnRoute` (POST /incidents/{incidentId}/en-route)
  - Updates responder status to en_route
  - Initiates live video stream from summoner to HQ
  - Starts response timeline for metrics

- `MarkOnScene` (POST /incidents/{incidentId}/on-scene)
  - Updates responder status to on_scene
  - Calculates response time (acceptance → arrival)
  - Notifies summoner that help has arrived

- `MarkResolved` (POST /incidents/{incidentId}/resolved)
  - Only First responder can resolve incidents
  - Sets status to "resolved" and records resolution time
  - Triggers video termination and photo cleanup
  - Initiates post-incident review workflow

**State Machine**:
Valid transitions enforced:
```
dispatch_in_progress → awaiting_response, escalation_required
awaiting_response → en_route, escalation_required
en_route → on_scene
on_scene → de_escalating, resolved
de_escalating → resolved, escalation_required
resolved → (terminal state)
escalation_required → resolved
```

**Key Features**:
- Strict state machine validation
- Role-based responder assignment (First/Second)
- Response time calculation
- Real-time notifications to summoner
- Idempotent updates supported

---

### 3. ResponderFunctions.cs

**Purpose**: Responder queries, distress signals, and offline action synchronization.

**Key Functions**:
- `GetNearbyResponders` (GET /responders/nearby)
  - Query parameters: lat, lon, radius_meters, max_results
  - Calculates geohash precision based on search radius
  - Returns available responders with distance and ETA
  - Uses geohash STARTSWITH for efficient spatial queries

- `TriggerResponderDistress` (POST /responders/distress) **HIGH PRIORITY**
  - Verifies responder is assigned to incident
  - Sends CRITICAL priority HQ broadcast to all HQ personnel
  - Alerts other responder at scene (if present)
  - Immediately escalates incident to 911
  - Updates incident status to "escalation_required"
  - Logs distress alert with nature (under_attack, weapon_seen, medical_emergency, etc.)
  - **This is the panic button feature for responder safety**

- `SyncIncidentActions` (POST /sync/incident-actions)
  - Processes batched offline actions from mobile clients
  - Sorts actions by timestamp for chronological replay
  - Validates idempotency keys to prevent duplicates
  - Applies actions to incident state machine
  - Returns per-action success/failure results
  - Supports action types: en_route, on_scene, status_update, resolved
  - **Note**: Distress signals cannot be synced offline (must be sent immediately)

**Geohash Precision Calculation**:
```
Radius ≤ 50m    → Precision 9  (~50m)
Radius ≤ 200m   → Precision 8  (~200m)
Radius ≤ 1km    → Precision 7  (~1km)
Radius ≤ 5km    → Precision 6  (~5km)
Radius ≤ 20km   → Precision 5  (~20km)
Radius > 20km   → Precision 4  (~100km)
```

**Key Features**:
- Geohash-based spatial queries
- Critical responder distress handling
- Offline-first with idempotency key support
- Chronological action replay
- Comprehensive error handling

---

## Data Transfer Objects (DTOs)

All DTOs are defined to match OpenAPI specifications:

### DispatchFunctions.cs
- `EmergencyDispatchRequest`
- `EmergencyDispatchResponse`
- `DispatchStatus`
- `DispatchExpansionRequest`
- `DispatchExpansionResponse`
- `DispatchNotificationMessage`
- `LocationDto`

### IncidentLifecycleFunctions.cs
- `IncidentStatusUpdate`
- `AcceptDispatchRequest`
- `DeclineDispatchRequest`
- `StatusChangeRequest`
- `ResolveIncidentRequest`
- `ResponderRoleDto`
- `IncidentDetailsDto`

### ResponderFunctions.cs
- `NearbyRespondersResponse`
- `NearbyResponderDto`
- `ResponderDistressRequest`
- `ResponderDistressResponse`
- `IncidentActionSyncRequest`
- `OfflineIncidentAction`
- `IncidentActionSyncResponse`
- `ActionSyncResult`

---

## Dependencies Required

All functions depend on interfaces defined in `TheWatch.Core`:

1. **IIncidentRepository**
   - GetByIdAsync
   - CreateAsync
   - UpdateAsync
   - GetActiveIncidentsByGeohashAsync

2. **IDispatchService**
   - DispatchRespondersAsync
   - FindNearbyRespondersAsync

3. **INotificationService**
   - SendDispatchNotificationAsync
   - SendPushNotificationAsync
   - SendHqBroadcastAsync
   - BroadcastIncidentUpdateAsync

---

## Integration Points

### Service Bus Queues
- **dispatch-queue**: Dispatch notifications to responders
- **notification-queue**: Push notifications, SMS, incident alerts (with priority lanes)
- **offline-sync-queue**: Batched offline actions from mobile clients

### Azure Storage
- Live video streams (initiated on en-route)
- Summoner photos (scheduled for deletion on incident resolution)
- Evidence files (chain of custody tracked)

### External Services
- Push notifications (APNS/FCM) via INotificationService
- 911 integration on escalation
- HQ dashboard real-time updates (SignalR)

---

## Geohash Implementation Notes

The functions include simplified geohash calculation for demonstration. **Production deployment should use a proper geohash library** such as:
- **NGeoHash** (NuGet: NGeoHash)
- **Geohash.NET** (NuGet: Geohash)

These libraries provide:
- More efficient algorithms
- Neighbor calculation (for expanding search rings)
- Distance calculations
- Bounding box queries

---

## Next Steps

### 1. Implement Repository and Service Interfaces
File locations:
- `/src/TheWatch.Infrastructure/Data/Repositories/IncidentRepository.cs`
- `/src/TheWatch.Infrastructure/Services/DispatchService.cs`
- `/src/TheWatch.Infrastructure/Services/NotificationService.cs`

### 2. Add Missing Entity Properties
Some entities may need additional properties:
- `Incident.DispatchStatus` tracking
- `ResponderAssignment` with acceptance timestamps
- Timeline events for audit trail

### 3. Configure Service Bus
In `/src/TheWatch.Functions/Program.cs`:
```csharp
services.AddScoped<IDispatchService, DispatchService>();
services.AddScoped<INotificationService, NotificationService>();
services.AddScoped<IIncidentRepository, IncidentRepository>();
```

### 4. Add Authentication
All endpoints should validate JWT bearer tokens:
```csharp
[Function("CreateEmergencyDispatch")]
[Authorize(Roles = "hq,admin")]
public async Task<HttpResponseData> CreateEmergencyDispatch(...)
```

### 5. Install Geohash Library
Add to `TheWatch.Functions.csproj`:
```xml
<PackageReference Include="NGeoHash" Version="3.2.0" />
```

### 6. Add Integration Tests
Test files needed:
- `DispatchFunctionsTests.cs` - Verify geohash expansion logic
- `IncidentLifecycleFunctionsTests.cs` - Test state machine transitions
- `ResponderFunctionsTests.cs` - Test offline sync and distress handling

### 7. Configure Application Insights
Add custom metrics for:
- Dispatch expansion frequency by incident severity
- Response time distribution (acceptance → on-scene)
- Distress signal frequency and resolution
- Offline sync batch sizes and failure rates

---

## API Specification Compliance

These implementations adhere to:
- **incident-checkin-api.yaml** - Incident lifecycle endpoints
- **emergency-response-core-api.yaml** - Dispatch and response coordination
- **enroute-api.yaml** - Responder navigation and coordination

All DTOs match OpenAPI schema definitions. Idempotency-Key header support is implemented for offline resilience.

---

## Security Considerations

1. **Authentication**: All endpoints require JWT bearer token (to be implemented)
2. **Authorization**: Role-based access control (summoner, responder, hq, admin)
3. **Idempotency**: All write operations accept Idempotency-Key header
4. **Audit Logging**: All state changes logged to incident timeline (to be implemented)
5. **PII Protection**: Responder identities anonymized to summoner

---

## Performance Optimizations

1. **Geohash Prefix Queries**: Use database indexes on `LocationGeohash` column with STARTSWITH
2. **Service Bus Priority Lanes**:
   - Critical: Responder distress, HQ broadcasts
   - High: Dispatch notifications
   - Normal: General notifications
3. **Redis Caching**: Cache active responder locations with 5-minute TTL
4. **Batch Processing**: Offline sync processes multiple actions in single transaction

---

## Monitoring and Metrics

Key metrics to track:
- **Dispatch Success Rate**: % of incidents that receive 2 responders
- **Time to First Responder**: Incident creation → first acceptance
- **Expansion Ring Distribution**: Which ring usually achieves threshold
- **Response Time**: Acceptance → on-scene arrival
- **Distress Signal Frequency**: Critical safety metric
- **Offline Sync Success Rate**: % of actions synced without errors

---

## Production Readiness Checklist

- [ ] Replace simplified geohash with production library (NGeoHash)
- [ ] Implement repository and service interfaces
- [ ] Add JWT authentication middleware
- [ ] Configure Service Bus queues and topics
- [ ] Add Application Insights custom metrics
- [ ] Implement incident timeline event logging
- [ ] Add integration tests for all functions
- [ ] Configure database indexes for geohash queries
- [ ] Set up Redis cache for responder locations
- [ ] Implement 911 escalation webhook
- [ ] Add rate limiting for API endpoints
- [ ] Configure CORS for mobile clients
- [ ] Set up Application Insights alerts for distress signals
- [ ] Document API response codes and error handling
- [ ] Add OpenTelemetry distributed tracing

---

Generated: 2025-12-17
Version: 1.0.0
Platform: .NET 10 Isolated Worker Model
Azure Functions Runtime: v4
