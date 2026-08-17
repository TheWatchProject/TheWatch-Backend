namespace TheWatch.Microservices.Evidence.AuditService.Models;

public enum AuditCategory
{
    SecurityAuth,
    IncidentManagement,
    DispatchAction,
    MedicalTriage,
    TelemetryLocation,
    NotificationDelivery,
    AiInference,
    SystemCompliance
}

public class AuditRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public AuditCategory Category { get; set; } = AuditCategory.IncidentManagement;
    public string TargetEntityId { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string IpAddress { get; set; } = "127.0.0.1";
    public string PreviousHash { get; set; } = "GENESIS_ROOT";
    public string IntegrityHash { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class LogAuditRequest
{
    public string CorrelationId { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public AuditCategory Category { get; set; } = AuditCategory.IncidentManagement;
    public string TargetEntityId { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
}

public class AuditQueryRequest
{
    public string? ActorId { get; set; }
    public string? CorrelationId { get; set; }
    public AuditCategory? Category { get; set; }
    public string? TargetEntityId { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int Limit { get; set; } = 50;
}

public class IntegrityVerificationResult
{
    public string RecordId { get; set; } = string.Empty;
    public bool IsTamperEvidentValid { get; set; }
    public string StoredHash { get; set; } = string.Empty;
    public string ComputedHash { get; set; } = string.Empty;
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AuditSummaryReport
{
    public int TotalAuditRecords { get; set; }
    public Dictionary<string, int> RecordsByCategory { get; set; } = new();
    public int UniqueActorsCount { get; set; }
    public bool CryptographicChainIntact { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
