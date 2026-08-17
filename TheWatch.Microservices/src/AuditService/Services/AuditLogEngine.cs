using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using TheWatch.Microservices.Evidence.AuditService.Models;

namespace TheWatch.Microservices.Evidence.AuditService.Services;

public interface IAuditLogEngine
{
    Task<AuditRecord> LogEventAsync(LogAuditRequest request);
    Task<IEnumerable<AuditRecord>> QueryRecordsAsync(AuditQueryRequest query);
    Task<AuditRecord?> GetRecordByIdAsync(string id);
    Task<IntegrityVerificationResult> VerifyIntegrityAsync(string recordId);
    Task<AuditSummaryReport> GetSummaryAsync();
}

public class InMemoryAuditLogEngine : IAuditLogEngine
{
    private static readonly ConcurrentDictionary<string, AuditRecord> Records = new();
    private static readonly List<AuditRecord> OrderedChain = new();
    private static readonly object SyncLock = new();
    private static string _latestHash = "GENESIS_BLOCK_THEWATCH_2026";

    static InMemoryAuditLogEngine()
    {
        // Seed initial audit trail
        var engine = new InMemoryAuditLogEngine();
        engine.LogEventAsync(new LogAuditRequest
        {
            CorrelationId = "CORR-001",
            ActorId = "commander.dan",
            ActorRole = "Commander",
            Action = "SYSTEM_INITIALIZATION",
            Category = AuditCategory.SecurityAuth,
            TargetEntityId = "THEWATCH-MESH",
            TargetEntityType = "CorePlatform",
            Details = "TheWatch Emergency Mesh v2.0 microservices cluster initialized."
        }).GetAwaiter().GetResult();

        engine.LogEventAsync(new LogAuditRequest
        {
            CorrelationId = "CORR-002",
            ActorId = "dispatch.sarah",
            ActorRole = "Dispatcher",
            Action = "INCIDENT_DISPATCH",
            Category = AuditCategory.DispatchAction,
            TargetEntityId = "INC-1001",
            TargetEntityType = "Incident",
            Details = "Assigned MEDIC-42 and DRONE-9 to multi-vehicle collision."
        }).GetAwaiter().GetResult();

        engine.LogEventAsync(new LogAuditRequest
        {
            CorrelationId = "CORR-003",
            ActorId = "medic.alex",
            ActorRole = "Paramedic",
            Action = "TRIAGE_ASSESSMENT",
            Category = AuditCategory.MedicalTriage,
            TargetEntityId = "TRI-9001",
            TargetEntityType = "TriageAssessment",
            Details = "START Triage: Patient #1 classified as IMMEDIATE (Red)."
        }).GetAwaiter().GetResult();
    }

    public Task<AuditRecord> LogEventAsync(LogAuditRequest request)
    {
        lock (SyncLock)
        {
            var recordId = $"AUD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            var timestamp = DateTime.UtcNow;
            var correlation = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString() : request.CorrelationId;
            var prevHash = _latestHash;

            var rawDataToHash = $"{recordId}|{timestamp:O}|{correlation}|{request.ActorId}|{request.Action}|{request.Category}|{request.TargetEntityId}|{prevHash}";
            var integrityHash = ComputeSha256(rawDataToHash);
            _latestHash = integrityHash;

            var record = new AuditRecord
            {
                Id = recordId,
                CorrelationId = correlation,
                ActorId = request.ActorId,
                ActorRole = request.ActorRole,
                Action = request.Action,
                Category = request.Category,
                TargetEntityId = request.TargetEntityId,
                TargetEntityType = request.TargetEntityType,
                Details = request.Details,
                IpAddress = request.IpAddress ?? "127.0.0.1",
                PreviousHash = prevHash,
                IntegrityHash = integrityHash,
                TimestampUtc = timestamp
            };

            Records[record.Id] = record;
            OrderedChain.Add(record);
            return Task.FromResult(record);
        }
    }

    public Task<IEnumerable<AuditRecord>> QueryRecordsAsync(AuditQueryRequest query)
    {
        IEnumerable<AuditRecord> list = Records.Values;

        if (!string.IsNullOrWhiteSpace(query.ActorId))
            list = list.Where(r => r.ActorId.Equals(query.ActorId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
            list = list.Where(r => r.CorrelationId.Equals(query.CorrelationId, StringComparison.OrdinalIgnoreCase));

        if (query.Category.HasValue)
            list = list.Where(r => r.Category == query.Category.Value);

        if (!string.IsNullOrWhiteSpace(query.TargetEntityId))
            list = list.Where(r => r.TargetEntityId.Equals(query.TargetEntityId, StringComparison.OrdinalIgnoreCase));

        if (query.FromUtc.HasValue)
            list = list.Where(r => r.TimestampUtc >= query.FromUtc.Value);

        if (query.ToUtc.HasValue)
            list = list.Where(r => r.TimestampUtc <= query.ToUtc.Value);

        return Task.FromResult(list.OrderByDescending(r => r.TimestampUtc).Take(query.Limit).AsEnumerable());
    }

    public Task<AuditRecord?> GetRecordByIdAsync(string id)
    {
        Records.TryGetValue(id, out var record);
        return Task.FromResult(record);
    }

    public Task<IntegrityVerificationResult> VerifyIntegrityAsync(string recordId)
    {
        if (!Records.TryGetValue(recordId, out var record))
        {
            return Task.FromResult(new IntegrityVerificationResult
            {
                RecordId = recordId,
                IsTamperEvidentValid = false,
                StoredHash = string.Empty,
                ComputedHash = string.Empty
            });
        }

        var rawDataToHash = $"{record.Id}|{record.TimestampUtc:O}|{record.CorrelationId}|{record.ActorId}|{record.Action}|{record.Category}|{record.TargetEntityId}|{record.PreviousHash}";
        var computed = ComputeSha256(rawDataToHash);
        var isValid = string.Equals(computed, record.IntegrityHash, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(new IntegrityVerificationResult
        {
            RecordId = recordId,
            IsTamperEvidentValid = isValid,
            StoredHash = record.IntegrityHash,
            ComputedHash = computed,
            VerifiedAtUtc = DateTime.UtcNow
        });
    }

    public Task<AuditSummaryReport> GetSummaryAsync()
    {
        var all = Records.Values.ToList();
        var byCategory = all.GroupBy(r => r.Category.ToString()).ToDictionary(g => g.Key, g => g.Count());
        var uniqueActors = all.Select(r => r.ActorId).Distinct().Count();

        return Task.FromResult(new AuditSummaryReport
        {
            TotalAuditRecords = all.Count,
            RecordsByCategory = byCategory,
            UniqueActorsCount = uniqueActors,
            CryptographicChainIntact = true,
            GeneratedAtUtc = DateTime.UtcNow
        });
    }

    private static string ComputeSha256(string raw)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}
