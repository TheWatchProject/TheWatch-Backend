# Phrase Detection Functions - Quick Reference

## Endpoints

### Trigger Phrase Management

```http
POST   /users/{userId}/trigger-phrases        # Create phrase
GET    /users/{userId}/trigger-phrases        # List phrases
GET    /users/{userId}/trigger-phrases/{id}   # Get phrase
PUT    /users/{userId}/trigger-phrases/{id}   # Update phrase
DELETE /users/{userId}/trigger-phrases/{id}   # Delete phrase
```

### Detection Sessions

```http
POST /detection/start                # Start listening
POST /detection/{sessionId}/stop     # Stop listening
GET  /detection/{sessionId}/status   # Get status
```

### Trigger & Cancel

```http
POST /detection/trigger   # Process detected phrase
POST /detection/cancel    # Cancel with PIN
```

## Request Examples

### Create Trigger Phrase

```json
POST /users/{userId}/trigger-phrases
{
  "phrase": "I need help now",
  "alternativePhrases": ["Help me now", "I need assistance"],
  "responseType": "community_only",
  "priority": "high",
  "confirmationRequired": false,
  "feedbackMode": "deceptive",
  "deceptiveAppDisguise": "maps"
}
```

### Start Detection

```json
POST /detection/start
{
  "userId": "guid",
  "detectionMode": "both",
  "location": {
    "latitude": 37.7749,
    "longitude": -122.4194
  },
  "defaultFeedbackMode": "haptic_only"
}
```

### Process Trigger

```json
POST /detection/trigger
{
  "sessionId": "guid",
  "detectedPhrase": "help me now",
  "matchedPhraseId": "guid",
  "matchConfidence": 0.92,
  "location": {
    "latitude": 37.7749,
    "longitude": -122.4194,
    "accuracy": 10
  }
}
```

### Cancel with PIN (CRITICAL: Duress Detection)

```json
POST /detection/cancel
{
  "incidentId": "guid",
  "userId": "guid",
  "cancellationReason": "accidental_trigger",
  "cancellationCode": "1234"  // Safe or Duress PIN
}
```

**Important**: Returns 200 OK for both safe and duress PINs. Server handles escalation silently.

## Feedback Modes

| Mode | Visual | Audio | Haptic | Use Case |
|------|--------|-------|--------|----------|
| **standard** | ✅ | ✅ | ✅ | Normal operation |
| **silent** | ❌ | ❌ | ❌ | Complete stealth |
| **haptic_only** | ❌ | ❌ | ✅ | Subtle confirmation |
| **deceptive** | ✅ (fake) | ❌ | ❌ | Active disguise |

## Deceptive Disguises

- `maps` - Shows "Finding nearby locations..."
- `weather` - Shows "Loading weather forecast..."
- `music` - Shows "Loading your music library..."
- `calculator` - Shows calculator UI
- `notes` - Shows "Opening notes..."

## Fuzzy Matching Sensitivity

| Level | Threshold | Use Case |
|-------|-----------|----------|
| `low` | 60% | Very tolerant, noisy environments |
| `medium` | 75% | Default, balanced |
| `high` | 90% | Strict, requires near-exact match |

## Common Phonetic Substitutions

- "help" → "kelp", "held"
- "now" → "know", "no"
- "call" → "coal", "col"
- "emergency" → "emergencies", "emerge and see"

## Security Notes

### Duress PIN Flow

```
User enters duress PIN
→ Server validates (timing-attack resistant)
→ IF DURESS:
    ✅ Returns 200 OK (appears successful to attacker)
    🚨 Silently escalates to HQ + Police
    📝 Sets incident.DuressFlag = true
  ELSE:
    ✅ Returns 200 OK (actually cancelled)
    📧 Notifies responders of cancellation
```

### Authentication

All endpoints require `Authorization: Bearer <JWT>` header with:
- Valid JWT token
- `sub` claim matching `{userId}` in URL
- Non-expired token

### PII Protection

**NEVER log**:
- Actual phrase text
- User names/emails
- PIN values
- Exact coordinates (only geohash)

## Error Codes

| Code | Status | Meaning |
|------|--------|---------|
| `INVALID_REQUEST` | 400 | Missing required fields |
| `INVALID_PHRASE` | 400 | Phrase length invalid |
| `NO_ACTIVE_PHRASES` | 400 | User has no phrases configured |
| `NO_MATCH` | 400 | Detected phrase didn't match |
| `UNAUTHORIZED` | 401 | Invalid JWT or wrong user |
| `INVALID_PIN` | 401 | Cancellation PIN incorrect |
| `PHRASE_NOT_FOUND` | 404 | Phrase ID doesn't exist |
| `SESSION_NOT_FOUND` | 404 | Session ID doesn't exist |
| `INTERNAL_ERROR` | 500 | Server error |

## Performance Targets

- Phrase matching: **< 50ms** (p95)
- Trigger processing: **< 200ms** (p95)
- Detection start: **< 100ms** (p95)

## Monitoring

### Key Metrics
- `detection_sessions_active` - Current active sessions
- `phrase_triggers_per_hour` - Trigger rate
- `phrase_match_confidence_avg` - Average confidence
- `duress_pin_activations` - CRITICAL alert metric

### Critical Alerts
- 🚨 Duress PIN activated
- ⚠️ Phrase matching timeout (> 500ms)
- ⚠️ Multiple failed PIN attempts

## Service Dependencies

Required for PhraseDetectionFunctions:
- ✅ ITriggerPhraseRepository
- ✅ IDetectionSessionRepository
- ✅ IPhraseMatchingService
- ✅ IFeedbackModeService
- ✅ IDuressPinService
- ✅ IDispatchService
- ✅ IIncidentRepository
- ✅ INotificationService
- ✅ GeohashService

## Testing

### Unit Test Example
```csharp
[Fact]
public async Task ProcessTrigger_WithMatchingPhrase_CreatesIncident()
{
    // Arrange
    var mockPhraseMatching = new Mock<IPhraseMatchingService>();
    mockPhraseMatching
        .Setup(x => x.FindBestMatchAsync(It.IsAny<string>(), It.IsAny<IEnumerable<TriggerPhrase>>(), It.IsAny<string>(), default))
        .ReturnsAsync((phrase, 0.95));

    // Act
    var result = await _functions.ProcessTrigger(request);

    // Assert
    Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    _incidentRepository.Verify(x => x.CreateAsync(It.IsAny<Incident>(), default), Times.Once);
}
```

### Integration Test Example
```csharp
[Fact]
public async Task EndToEnd_CreatePhraseAndTrigger_Success()
{
    // 1. Create trigger phrase
    var createResponse = await _client.PostAsJsonAsync("/users/{userId}/trigger-phrases", new { phrase = "help me" });
    var phrase = await createResponse.Content.ReadFromJsonAsync<TriggerPhrase>();

    // 2. Start detection session
    var sessionResponse = await _client.PostAsJsonAsync("/detection/start", new { userId });
    var session = await sessionResponse.Content.ReadFromJsonAsync<DetectionSession>();

    // 3. Trigger incident
    var triggerResponse = await _client.PostAsJsonAsync("/detection/trigger", new {
        sessionId = session.SessionId,
        detectedPhrase = "help me",
        matchedPhraseId = phrase.PhraseId
    });

    Assert.Equal(HttpStatusCode.OK, triggerResponse.StatusCode);
}
```

## Common Issues

### Issue: "NO_MATCH" error despite phrase existing
**Solution**: Check sensitivity level. Try lowering to "low" or add alternative phrases.

### Issue: Duress PIN not escalating
**Solution**: Verify IDuressPinService is properly implemented and registered in DI.

### Issue: Slow phrase matching
**Solution**:
1. Reduce number of active phrases per user
2. Cache user phrases in Redis
3. Consider indexing phrase text

### Issue: Mobile app not receiving feedback
**Solution**: Verify FeedbackModeService returns correct configuration. Check mobile client implementation.

## Production Checklist

- [ ] Service registrations in DI container
- [ ] EF Core migration for DetectionSession table
- [ ] Application Insights custom metrics configured
- [ ] PagerDuty alerts for duress PIN
- [ ] Load testing completed (1000+ concurrent sessions)
- [ ] Security audit passed
- [ ] Mobile app integration tested
- [ ] Runbook updated with incident response procedures

## Support

For implementation questions, see:
- Full documentation: `PHRASE_DETECTION_IMPLEMENTATION_SUMMARY.md`
- API spec: `APIS/incident-detection-api.yaml`
- Entity definitions: `TheWatch.Core/Entities/`
