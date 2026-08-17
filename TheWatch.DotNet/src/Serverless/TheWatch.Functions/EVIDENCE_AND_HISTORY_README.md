# Evidence and History Management Azure Functions

## Overview

This document describes the Azure Functions implementation for Evidence and History management in The Watch platform. These functions implement the API specifications defined in:
- `APIS/post-incident-evidence-api.yaml` - Evidence upload and chain of custody
- `APIS/history-api.yaml` - Incident history and analytics
- `APIS/live-video-api.yaml` - Real-time video streaming

## Implemented Functions

### 1. EvidenceFunctions.cs

HTTP-triggered functions for evidence management operations.

#### Endpoints

| Function | Route | Method | Purpose |
|----------|-------|--------|---------|
| `UploadEvidence` | `/incidents/{incidentId}/evidence` | POST | Upload photo/video/audio evidence |
| `ListIncidentEvidence` | `/incidents/{incidentId}/evidence` | GET | List all evidence for incident |
| `GetEvidenceDetails` | `/incidents/{incidentId}/evidence/{evidenceId}` | GET | Get evidence metadata and details |
| `DownloadEvidenceFile` | `/incidents/{incidentId}/evidence/{evidenceId}/file` | GET | Download actual evidence file |
| `PlaceLegalHold` | `/incidents/{incidentId}/evidence/{evidenceId}/hold` | POST | Place legal hold on evidence |
| `RemoveLegalHold` | `/incidents/{incidentId}/evidence/{evidenceId}/hold` | DELETE | Remove legal hold |
| `TransferEvidence` | `/incidents/{incidentId}/evidence/{evidenceId}/transfer` | POST | Transfer to law enforcement |
| `GetChainOfCustody` | `/incidents/{incidentId}/evidence/{evidenceId}/chain-of-custody` | GET | Get complete custody log |
| `VerifyEvidenceIntegrity` | `/incidents/{incidentId}/evidence/{evidenceId}/integrity` | GET | Verify SHA-256 hash |
| `UpdateEvidenceMetadata` | `/incidents/{incidentId}/evidence/{evidenceId}/metadata` | PATCH | Update description/tags |
| `ExportEvidencePackage` | `/incidents/{incidentId}/evidence/export` | POST | Export all evidence as ZIP |
| `GetEvidenceSummary` | `/incidents/{incidentId}/evidence-summary` | GET | Get evidence statistics |

#### Key Features

- **SHA-256 Integrity Verification**: All evidence files are hashed for integrity verification
- **Chain of Custody**: Every access, modification, and transfer is logged immutably
- **Legal Hold**: Evidence can be placed on legal hold to prevent deletion
- **Azure Blob Storage**: Evidence stored in `/evidence/{incidentId}/{evidenceId}.{ext}`
- **7-Year Retention**: Standard retention period (2555 days) unless on legal hold
- **Law Enforcement Transfer**: Secure evidence transfer with confirmation tracking

### 2. EvidenceProcessingFunctions.cs

Background processing functions triggered by Service Bus queues and timers.

#### Functions

| Function | Trigger | Purpose |
|----------|---------|---------|
| `VerifyEvidenceIntegrityBackground` | Service Bus Queue | Calculate SHA-256 hash, verify integrity |
| `GenerateEvidenceThumbnails` | Service Bus Queue | Create compressed and thumbnail versions of photos |
| `EnforceEvidenceRetention` | Timer (daily 2 AM) | Archive/delete evidence past retention period |
| `AuditEvidenceIntegrity` | Timer (monthly 1st) | Periodic integrity audit (random sampling) |

#### Key Features

- **Automatic Hash Calculation**: SHA-256 hash computed on upload
- **Thumbnail Generation**: 200x200 thumbnails for photo evidence
- **Compressed Versions**: 50% quality compressed versions for bandwidth optimization
- **Retention Enforcement**: Automatic archival to cold storage after 7 years
- **Integrity Auditing**: Monthly random sampling to detect corruption
- **Retry Logic**: Service Bus retries with exponential backoff (3 attempts: 1m, 5m, 15m)

### 3. HistoryFunctions.cs

HTTP-triggered functions for incident history and analytics.

#### Responder History Endpoints

| Function | Route | Method | Purpose |
|----------|-------|--------|---------|
| `GetResponderIncidents` | `/responder/incidents` | GET | Get responder's incident history |
| `GetResponderIncidentView` | `/responder/incidents/{incidentId}` | GET | Detailed view of specific incident |
| `GetResponderStats` | `/responder/stats` | GET | Performance statistics |
| `GetResponderReliability` | `/responder/reliability` | GET | Reliability metrics and rating |
| `GetAccountability` | `/responder/accountability` | GET | Disagreements and training needs |

#### Summoner History Endpoints

| Function | Route | Method | Purpose |
|----------|-------|--------|---------|
| `GetSummonerIncidents` | `/summoner/incidents` | GET | Get summoner's reported incidents |
| `GetSummonerIncidentView` | `/summoner/incidents/{incidentId}` | GET | Detailed view of specific incident |

#### Admin Endpoints

| Function | Route | Method | Purpose |
|----------|-------|--------|---------|
| `GetAdminResponderHistory` | `/admin/responder/{responderId}/history` | GET | Complete responder history (HQ view) |
| `GetIncidentTimeline` | `/incidents/{incidentId}/timeline` | GET | Chronological timeline of all events |
| `ExportHistory` | `/history/export` | GET | Export history as CSV/JSON/PDF |

#### Key Features

- **Role-Based Views**: Responders see their incidents, summoners see theirs, HQ sees all
- **Performance Metrics**: Response rate, avg response time, outcome distribution
- **Reliability Rating**: Calculated score (excellent, good, fair, poor, unproven)
- **Accountability Tracking**: Disagreements, concerns, pattern analysis
- **Timeline Events**: Complete audit trail with actor anonymization support
- **Export Formats**: CSV, JSON, PDF export for personal records

### 4. VideoFunctions.cs

HTTP-triggered functions for live video streaming management.

#### Video Stream Management

| Function | Route | Method | Purpose |
|----------|-------|--------|---------|
| `InitiateVideoStream` | `/streams` | POST | Start video stream (auto on incident) |
| `GetStreamStatus` | `/streams/{streamId}` | GET | Get stream status (HQ only) |
| `TerminateStream` | `/streams/{streamId}/terminate` | POST | Manually stop stream |
| `ChallengeAllClear` | `/streams/{streamId}/all-clear-challenge` | POST | Duress detection password challenge |
| `GetDuressFlag` | `/streams/{streamId}/duress-flag` | GET | Check if duress detected |

#### HQ Monitoring

| Function | Route | Method | Purpose |
|----------|-------|--------|---------|
| `GetActiveStreams` | `/hq/active-streams` | GET | List all active streams |
| `AddStreamNote` | `/hq/stream/{streamId}/notes` | POST | Add observation notes |
| `ManualDuressAlert` | `/hq/stream/{streamId}/duress-alert` | POST | Manually flag duress |

#### Recording & Playback

| Function | Route | Method | Purpose |
|----------|-------|--------|---------|
| `GetRecordingStatus` | `/streams/{streamId}/recording` | GET | Get recording info |
| `GetPlaybackUrl` | `/streams/{streamId}/playback` | GET | Get time-limited playback URL |
| `GetStreamQuality` | `/streams/{streamId}/quality` | GET | Get quality metrics |

#### Key Features

- **Automatic Initiation**: Video stream starts automatically when incident reported
- **HQ-Only Viewing**: Only HQ can see streams (invisible to responders and perpetrator)
- **Duress Detection**: Wrong password on "All Clear" = silent duress alert
- **Deceptive UI**: UI shows "All Clear" to attacker, but HQ alerted and recording continues
- **WebSocket Streaming**: Real-time video transmission using WebSocket connections
- **Azure Blob Storage**: Recordings stored in `/live-video-streams/{incidentId}/{streamId}/`
- **Quality Metrics**: Resolution, bitrate, frame rate, latency monitoring
- **HQ Notes**: Real-time observation notes during stream

## Domain Entities

### Evidence (existing)
- Location: `TheWatch.Core/Entities/Evidence.cs`
- Properties: EvidenceId, IncidentId, UploadedByResponderId, EvidenceType, FileName, StorageLocation, Sha256Hash, LegalHold
- Purpose: Represents evidence collected during incident

### EvidenceChainOfCustody (existing)
- Location: `TheWatch.Core/Entities/EvidenceChainOfCustody.cs`
- Properties: CustodyEventId, EvidenceId, EventType, ActorId, Timestamp, Details, Signature
- Purpose: Immutable audit trail of evidence access

### VideoStream (new)
- Location: `TheWatch.Core/Entities/VideoStream.cs`
- Properties: StreamId, IncidentId, DeviceId, Status, DurationSeconds, DuressFlagged, StorageLocation
- Purpose: Represents live video stream from summoner to HQ

### VideoStreamNote (new)
- Location: `TheWatch.Core/Entities/VideoStreamNote.cs`
- Properties: NoteId, StreamId, Content, NoteType, RecordedById, Timestamp
- Purpose: HQ observation notes during video stream

## Implementation Details

### Evidence Upload Flow

1. **HTTP POST** to `/incidents/{incidentId}/evidence`
2. Parse multipart/form-data (file + metadata)
3. Validate responder is assigned to incident
4. Upload file to Azure Blob Storage
5. Create evidence record in database
6. **Queue message** to `evidence-processing-queue`
7. **Background function** calculates SHA-256 hash
8. **Background function** generates thumbnails (photos only)
9. Log chain of custody event (upload)

### Evidence Integrity Verification

1. Evidence uploaded → message queued
2. `VerifyEvidenceIntegrityBackground` triggered
3. Download file from Azure Blob Storage
4. Calculate SHA-256 hash
5. Compare with stored hash (if exists)
6. Update evidence record with hash
7. Log chain of custody event (integrity_verified)
8. If mismatch detected → flag integrity violation → alert HQ

### Chain of Custody Logging

Every evidence operation logs an event:

```csharp
await LogChainOfCustodyEvent(evidenceId, "access", new
{
    access_type = "download",
    format = "original",
    client_ip = req.Headers["X-Forwarded-For"]
});
```

Event types:
- `upload` - Evidence uploaded
- `access` - Evidence viewed or downloaded
- `metadata_update` - Description/tags changed
- `tag_added` - Tags added
- `transfer` - Transferred to law enforcement
- `legal_hold_placed` - Legal hold placed
- `legal_hold_released` - Legal hold removed
- `export` - Included in evidence export package
- `integrity_verified` - SHA-256 hash verified

### Duress Detection Flow

1. Summoner clicks "All Clear" on device
2. **UI shows password prompt** (hidden from attacker)
3. Summoner enters password
4. **POST** to `/streams/{streamId}/all-clear-challenge` with `password_attempt`
5. **Server compares** with stored duress password hash
6. **If correct**:
   - Stream terminated normally
   - UI shows "Recording stopped"
   - Return `{ correct: true, stream_terminated: true }`
7. **If incorrect** (DURESS DETECTED):
   - Stream continues in background (HQ still recording)
   - Flag duress in database
   - Alert HQ and assigned responders ("Do not leave scene")
   - Return fake success to summoner: `{ correct: false, stream_terminated: true, message: "Recording stopped" }`
   - UI shows "All Clear" (deceptive mode)

### Responder Reliability Calculation

Algorithm from `todo.md`:

```
Reliability Score = Weighted average of:
- Acceptance Rate (30%): incidents_accepted / incidents_dispatched
- Response Time (25%): normalized against target (7 min = 100%)
- On-Time Arrivals (20%): arrivals within expected timeframe
- Disagreement Rate (15%): 100% - (disagreements / incidents * 100)
- Evidence Collection (10%): evidence collected vs expected
```

Ratings:
- **Excellent**: Score >= 90
- **Good**: Score 75-89
- **Fair**: Score 60-74
- **Poor**: Score < 60
- **Unproven**: < 5 incidents

## Service Bus Queue Configuration

### evidence-processing-queue

**Messages**: Evidence upload notifications
**Consumers**:
- `VerifyEvidenceIntegrityBackground`
- `GenerateEvidenceThumbnails`

**Message Format**:
```json
{
  "EvidenceId": "uuid",
  "IncidentId": "uuid",
  "EvidenceType": "photo|video|audio",
  "StorageLocation": "/evidence/incident_id/evidence_id.jpg",
  "FileSizeBytes": 1024000,
  "UploadTimestamp": "2025-01-15T12:00:00Z"
}
```

**Retry Policy**: 3 attempts (1m, 5m, 15m exponential backoff)
**Dead Letter Queue**: Failed messages after 3 retries

## Azure Blob Storage Structure

### Evidence Storage

```
/evidence/
  {incident_id}/
    {evidence_id}.jpg          # Original file
    {evidence_id}/
      compressed.jpg           # 50% quality compressed
      thumbnail.jpg            # 200x200 thumbnail
```

### Video Stream Storage

```
/live-video-streams/
  {incident_id}/
    {stream_id}/
      recording.mp4            # Full recording
      metadata.json            # Stream metadata
```

## Timer Triggers

| Function | Schedule | Purpose |
|----------|----------|---------|
| `EnforceEvidenceRetention` | `0 0 2 * * *` | Daily at 2 AM UTC - Archive/delete old evidence |
| `AuditEvidenceIntegrity` | `0 0 3 1 * *` | Monthly on 1st at 3 AM - Integrity audit (10% sample) |

CRON format: `{second} {minute} {hour} {day} {month} {day-of-week}`

## Security Considerations

### Authentication
- All endpoints require JWT bearer token authentication
- Role-based access control (responder, summoner, hq, admin)
- Step-up authentication required for legal hold and transfers

### Authorization Checks
- **Evidence upload**: Must be First or Second responder assigned to incident
- **Evidence download**: Responders on incident, HQ, or law enforcement only
- **Legal hold**: HQ or admin only
- **Evidence transfer**: HQ or admin only
- **Video streams**: HQ only (except summoner for own stream)

### Chain of Custody
- Every access logged with: actor, timestamp, IP address, user agent
- Immutable audit trail (append-only table)
- Digital signatures supported for legal chain of custody
- Actor anonymization for Right to be Forgotten compliance

### Data Privacy
- Evidence auto-deleted after 7 years (unless legal hold)
- Summoner photos auto-deleted on incident close
- Video streams visible only to HQ (not responders or perpetrator)
- PII scrubbing support in timeline events (actor_ref.pii_state)

## TODO: Implementation Checklist

Each function contains `// TODO:` comments for full implementation. Required steps:

### Database Integration
- [ ] Register DbContext in Program.cs
- [ ] Create repositories (IEvidenceRepository, IVideoStreamRepository, etc.)
- [ ] Implement entity configurations in WatchDbContext

### Azure Service Integration
- [ ] Azure Blob Storage client (upload, download, SAS URL generation)
- [ ] Azure Service Bus client (queue message publishing)
- [ ] Azure SignalR Service (WebSocket connections for video)

### Business Logic
- [ ] SHA-256 hash calculation
- [ ] Thumbnail generation (System.Drawing or ImageSharp)
- [ ] Video transcoding (FFmpeg or Azure Media Services)
- [ ] Reliability score calculation algorithm
- [ ] Duress password hashing (bcrypt/argon2)

### Security
- [ ] JWT token validation
- [ ] Role-based authorization checks
- [ ] WebSocket authentication for video streaming
- [ ] Legal hold enforcement in queries

### Testing
- [ ] Unit tests for business logic
- [ ] Integration tests with TestServer
- [ ] Service Bus queue processing tests
- [ ] Timer trigger tests

## Related Documentation

- [APIS/post-incident-evidence-api.yaml](../../APIS/post-incident-evidence-api.yaml) - Evidence API specification
- [APIS/history-api.yaml](../../APIS/history-api.yaml) - History API specification
- [APIS/live-video-api.yaml](../../APIS/live-video-api.yaml) - Video streaming API specification
- [todo.md](../../todo.md) - Database schema and background jobs
- [docs/architecture/security-model.md](../../docs/architecture/security-model.md) - Security architecture

## Next Steps

1. **Implement repository layer** in TheWatch.Infrastructure
2. **Add Azure service clients** (Blob Storage, Service Bus, SignalR)
3. **Configure DI** in Program.cs
4. **Implement authentication/authorization** middleware
5. **Add unit and integration tests**
6. **Deploy to Azure** using Bicep templates from `/infra`

## Questions?

See [CLAUDE.md](../CLAUDE.md) for project overview and development guidelines.
