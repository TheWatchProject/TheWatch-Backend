# Phrase Detection Setup Instructions

## 1. Service Registration (Required)

Add to `TheWatch.Functions/Program.cs`:

```csharp
// Add after existing service registrations

// Phrase Detection Repositories
builder.Services.AddScoped<ITriggerPhraseRepository, TriggerPhraseRepository>();
builder.Services.AddScoped<IDetectionSessionRepository, DetectionSessionRepository>();

// Phrase Detection Services
builder.Services.AddScoped<IPhraseMatchingService, PhraseMatchingService>();
builder.Services.AddScoped<IFeedbackModeService, FeedbackModeService>();
```

Add to `TheWatch.Api/Program.cs` (if not already present):

```csharp
// Same registrations as above
builder.Services.AddScoped<ITriggerPhraseRepository, TriggerPhraseRepository>();
builder.Services.AddScoped<IDetectionSessionRepository, DetectionSessionRepository>();
builder.Services.AddScoped<IPhraseMatchingService, PhraseMatchingService>();
builder.Services.AddScoped<IFeedbackModeService, FeedbackModeService>();
```

## 2. Database Migration

Add to `TheWatch.Infrastructure/Data/WatchDbContext.cs`:

```csharp
public DbSet<DetectionSession> DetectionSessions { get; set; } = null!;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // Add DetectionSession configuration
    modelBuilder.Entity<DetectionSession>(entity =>
    {
        entity.HasKey(e => e.SessionId);
        entity.Property(e => e.SessionId).ValueGeneratedNever();

        // Convert enums to strings for readability
        entity.Property(e => e.Status)
            .HasConversion<string>()
            .IsRequired();

        entity.Property(e => e.Mode)
            .HasConversion<string>()
            .IsRequired();

        entity.Property(e => e.DefaultFeedbackMode)
            .HasConversion<string>()
            .IsRequired();

        // Index for querying active sessions by user
        entity.HasIndex(e => new { e.UserId, e.Status });

        // Foreign key to User
        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    });

    // Enhance TriggerPhrase indexes
    modelBuilder.Entity<TriggerPhrase>(entity =>
    {
        // Existing configuration...

        // Add composite index for active phrase queries
        entity.HasIndex(e => new { e.UserId, e.IsActive });
    });
}
```

## 3. Create EF Core Migration

```bash
cd src/TheWatch.Infrastructure
dotnet ef migrations add AddDetectionSessions --startup-project ../TheWatch.Api
dotnet ef database update --startup-project ../TheWatch.Api
```

## 4. Verify Dependencies

Ensure these services are already registered:

```csharp
// Should already exist from previous implementations
builder.Services.AddScoped<IDuressPinService, DuressPinService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddScoped<IIncidentRepository, IncidentRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
```

## 5. Application Settings

Ensure connection strings are configured in:

**`TheWatch.Functions/local.settings.json`**:
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "SqlConnectionString": "Server=...;Database=TheWatch;...",
    "AZURE_COSMOS_CONNECTION_STRING": "AccountEndpoint=...;",
    "AZURE_SERVICE_BUS_CONNECTION_STRING": "Endpoint=sb://...",
    "AZURE_SIGNALR_CONNECTION_STRING": "Endpoint=https://..."
  }
}
```

**`TheWatch.Api/appsettings.json`**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=TheWatch;...",
    "HangfireConnection": "Server=...;Database=TheWatch_Hangfire;..."
  }
}
```

## 6. Test the Implementation

### Verify Service Registration
```bash
cd src/TheWatch.Functions
dotnet build
```

Check for DI errors in build output.

### Test Endpoints Locally

Start the Functions runtime:
```bash
cd src/TheWatch.Functions
func start
```

Test with curl:
```bash
# Get user phrases (replace {userId} with actual GUID)
curl -X GET "http://localhost:7071/api/users/{userId}/trigger-phrases" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"

# Create trigger phrase
curl -X POST "http://localhost:7071/api/users/{userId}/trigger-phrases" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "phrase": "help me now",
    "responseType": "community_only",
    "priority": "high",
    "feedbackMode": "standard"
  }'

# Start detection session
curl -X POST "http://localhost:7071/api/detection/start" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "{userId}",
    "detectionMode": "both"
  }'
```

## 7. Deploy to Azure

### Prerequisites
- Azure Functions App created
- SQL Database configured
- Connection strings added to Azure App Settings

### Deploy Functions
```bash
cd src/TheWatch.Functions
func azure functionapp publish <function-app-name>
```

### Verify Deployment
```bash
# Test production endpoint
curl -X GET "https://<function-app-name>.azurewebsites.net/api/users/{userId}/trigger-phrases" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

## 8. Monitoring Setup

### Application Insights

Add custom metrics to track:
- `phrase_detection_sessions_active`
- `phrase_triggers_per_hour`
- `phrase_match_confidence_distribution`
- `duress_pin_activations` (CRITICAL)

### Alert Rules

Create alerts for:
- Duress PIN activation (immediate PagerDuty)
- Phrase matching latency > 500ms
- High error rate on detection endpoints
- Failed dispatch attempts

## 9. Security Checklist

- [ ] JWT validation configured
- [ ] Connection strings stored in Azure Key Vault (production)
- [ ] TLS 1.3 enforced on all endpoints
- [ ] CORS configured appropriately
- [ ] Rate limiting enabled in API Management
- [ ] WAF rules applied
- [ ] PII redaction verified in logs
- [ ] Duress PIN timing attack resistance tested

## 10. Load Testing

Test with Azure Load Testing or k6:

```javascript
// k6 test script
import http from 'k6/http';
import { check } from 'k6';

export let options = {
  stages: [
    { duration: '2m', target: 100 },  // Ramp up to 100 users
    { duration: '5m', target: 100 },  // Stay at 100
    { duration: '2m', target: 0 },    // Ramp down
  ],
};

export default function () {
  // Start detection session
  let startRes = http.post('https://api.thewatch.app/v1/detection/start',
    JSON.stringify({
      userId: __ENV.USER_ID,
      detectionMode: 'both'
    }),
    { headers: { 'Authorization': `Bearer ${__ENV.JWT_TOKEN}` } }
  );

  check(startRes, {
    'session started': (r) => r.status === 201,
    'response time < 200ms': (r) => r.timings.duration < 200,
  });
}
```

Run test:
```bash
k6 run --env USER_ID=... --env JWT_TOKEN=... load-test.js
```

## 11. Troubleshooting

### Error: "Service not registered"
**Solution**: Verify service registration in Program.cs. Run `dotnet build` to check DI errors.

### Error: "Table DetectionSessions does not exist"
**Solution**: Run EF Core migration: `dotnet ef database update`

### Error: "No matching phrase found" (but phrase exists)
**Solution**:
1. Check phrase is active: `IsActive = true`
2. Verify user ID matches
3. Try lowering sensitivity level
4. Add alternative phrases

### Error: "Duress PIN not working"
**Solution**:
1. Verify IDuressPinService is implemented
2. Check DI registration
3. Verify PIN is stored in database
4. Test ValidateCancellationPinAsync directly

### High Latency on Phrase Matching
**Solution**:
1. Reduce number of active phrases
2. Cache user phrases in Redis
3. Use async/await properly
4. Profile with Application Insights

## 12. Next Steps

After setup is complete:

1. **Mobile Integration**: Integrate mobile apps with detection endpoints
2. **HQ Dashboard**: Add phrase monitoring to HQ dashboard
3. **Analytics**: Set up phrase usage analytics
4. **ML Training**: Collect phrase match data for ML model training
5. **Multi-language**: Add support for non-English phrases

## Files Created

**Core Interfaces** (TheWatch.Core/Interfaces/):
- ✅ IDetectionSessionRepository.cs
- ✅ IPhraseMatchingService.cs
- ✅ IFeedbackModeService.cs

**Infrastructure** (TheWatch.Infrastructure/):
- ✅ DetectionSessionRepository.cs
- ✅ PhraseMatchingService.cs
- ✅ FeedbackModeService.cs

**Functions** (TheWatch.Functions/):
- ✅ PhraseDetectionFunctions.cs (978 lines, 12 endpoints)

**Documentation**:
- ✅ PHRASE_DETECTION_IMPLEMENTATION_SUMMARY.md
- ✅ PHRASE_DETECTION_QUICK_REFERENCE.md
- ✅ PHRASE_DETECTION_SETUP.md (this file)

## Support

For questions or issues:
1. Check PHRASE_DETECTION_QUICK_REFERENCE.md for common scenarios
2. Review PHRASE_DETECTION_IMPLEMENTATION_SUMMARY.md for architecture details
3. Consult API spec: APIS/incident-detection-api.yaml
4. Check entity definitions: TheWatch.Core/Entities/

**This completes Priority 3 from todo3.md: Incident Detection API**
