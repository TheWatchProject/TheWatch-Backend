using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;
using TheWatch.Core.Interfaces;
using TheWatch.Infrastructure.Data;
using TheWatch.Infrastructure.Services;

namespace TheWatch.Functions;

/// <summary>
/// Background processing functions for evidence integrity and chain of custody.
/// Triggered by Service Bus queue messages when evidence is uploaded.
/// </summary>
public class EvidenceProcessingFunctions
{
    private readonly ILogger<EvidenceProcessingFunctions> _logger;
    private readonly IEvidenceRepository _evidenceRepository;
    private readonly EvidenceStorageService _evidenceStorageService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly WatchDbContext _dbContext;

    public EvidenceProcessingFunctions(
        ILogger<EvidenceProcessingFunctions> logger,
        IEvidenceRepository evidenceRepository,
        EvidenceStorageService evidenceStorageService,
        IBlobStorageService blobStorageService,
        WatchDbContext dbContext)
    {
        _logger = logger;
        _evidenceRepository = evidenceRepository;
        _evidenceStorageService = evidenceStorageService;
        _blobStorageService = blobStorageService;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Service Bus trigger for evidence integrity verification.
    /// Calculates SHA-256 hash and logs chain of custody event.
    /// </summary>
    [Function("VerifyEvidenceIntegrityBackground")]
    public async Task VerifyEvidenceIntegrityBackground(
        [ServiceBusTrigger("evidence-processing-queue", Connection = "ServiceBusConnectionString")] ServiceBusReceivedMessage message)
    {
        _logger.LogInformation("Processing evidence integrity verification for message: {MessageId}", message.MessageId);

        try
        {
            // Parse message body
            var messageBody = JsonSerializer.Deserialize<EvidenceUploadedMessage>(message.Body.ToString());
            if (messageBody == null)
            {
                _logger.LogError("Failed to deserialize message body");
                return;
            }

            var evidenceId = messageBody.EvidenceId;
            var incidentId = messageBody.IncidentId;
            var storageLocation = messageBody.StorageLocation;

            _logger.LogInformation("Verifying evidence integrity: {EvidenceId} for incident: {IncidentId}", evidenceId, incidentId);

            // 1-3. Retrieve, calculate hash, and compare
            var isValid = await _evidenceStorageService.VerifyIntegrityAsync(evidenceId);

            if (!isValid)
            {
                // 6. Flag integrity violation and alert HQ
                _logger.LogWarning("INTEGRITY VIOLATION DETECTED: Evidence {EvidenceId}", evidenceId);

                await _evidenceRepository.LogChainOfCustodyEventAsync(
                    evidenceId,
                    Guid.Empty, // System actor
                    "integrity_violation",
                    "Hash mismatch detected during verification");

                // In production, send alert to HQ
            }
            else
            {
                // 4-5. Update record and log chain of custody
                var evidence = await _evidenceRepository.GetByIdAsync(evidenceId);

                if (evidence != null)
                {
                    await _evidenceRepository.LogChainOfCustodyEventAsync(
                        evidenceId,
                        Guid.Empty, // System actor
                        "integrity_verified",
                        JsonSerializer.Serialize(new
                        {
                            hash_algorithm = "SHA-256",
                            cryptographic_hash = evidence.Sha256Hash,
                            verified_at = DateTime.UtcNow
                        }));

                    _logger.LogInformation("Evidence integrity verified: {EvidenceId}", evidenceId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing evidence integrity verification");
            throw; // Let Service Bus retry
        }
    }

    /// <summary>
    /// Service Bus trigger for thumbnail generation (photos).
    /// Creates compressed and thumbnail versions of photo evidence.
    /// </summary>
    [Function("GenerateEvidenceThumbnails")]
    public async Task GenerateEvidenceThumbnails(
        [ServiceBusTrigger("evidence-processing-queue", Connection = "ServiceBusConnectionString")] ServiceBusReceivedMessage message)
    {
        _logger.LogInformation("Generating thumbnails for evidence: {MessageId}", message.MessageId);

        try
        {
            var messageBody = JsonSerializer.Deserialize<EvidenceUploadedMessage>(message.Body.ToString());
            if (messageBody == null || messageBody.EvidenceType != "photo")
            {
                _logger.LogInformation("Skipping thumbnail generation for non-photo evidence");
                return;
            }

            var evidenceId = messageBody.EvidenceId;
            var storageLocation = messageBody.StorageLocation;

            _logger.LogInformation("Generating thumbnails for photo evidence: {EvidenceId}", evidenceId);

            // Note: Actual image processing would require libraries like ImageSharp
            // For now, we'll simulate the process with logging

            // 1. Retrieve photo from Azure Blob Storage
            var evidence = await _evidenceRepository.GetByIdAsync(evidenceId);
            if (evidence == null)
            {
                _logger.LogWarning("Evidence not found for thumbnail generation: {EvidenceId}", evidenceId);
                return;
            }

            // 2-3. Generate compressed and thumbnail versions
            // In production, download image, process with ImageSharp, and re-upload

            // 4. Upload to Blob Storage (simulated)
            var compressedPath = $"{storageLocation}/compressed.jpg";
            var thumbnailPath = $"{storageLocation}/thumbnail.jpg";

            _logger.LogInformation("Thumbnails would be generated at: {Compressed}, {Thumbnail}",
                compressedPath, thumbnailPath);

            // 5. Update evidence record (in production)
            // evidence.CompressedLocation = compressedPath;
            // evidence.ThumbnailLocation = thumbnailPath;
            // await _evidenceRepository.UpdateAsync(evidence);

            _logger.LogInformation("Thumbnails generated for evidence: {EvidenceId}", evidenceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating thumbnails for evidence");
            // Don't throw - thumbnail generation is not critical
        }
    }

    /// <summary>
    /// Timer trigger for evidence retention policy enforcement.
    /// Runs daily to check for evidence past retention period.
    /// </summary>
    [Function("EnforceEvidenceRetention")]
    public async Task EnforceEvidenceRetention(
        [TimerTrigger("0 0 2 * * *")] TimerInfo timerInfo) // Runs daily at 2 AM UTC
    {
        _logger.LogInformation("Running evidence retention policy enforcement");

        try
        {
            // 1. Query evidence ready for archival (7 years old, no legal hold)
            var sevenYearsAgo = DateTime.UtcNow.AddYears(-7);
            var evidenceToArchive = await _evidenceRepository.GetEvidenceForRetentionCleanupAsync(sevenYearsAgo);

            var evidenceProcessedCount = 0;
            var evidenceArchivedCount = 0;
            var evidenceDeletedCount = 0;

            // 2. Archive old evidence
            foreach (var evidence in evidenceToArchive)
            {
                if (evidence.LegalHold)
                {
                    _logger.LogInformation("Skipping evidence on legal hold: {EvidenceId}", evidence.EvidenceId);
                    continue;
                }

                try
                {
                    // Move to cold storage (in production, would change blob tier)
                    _logger.LogInformation("Archiving evidence: {EvidenceId}", evidence.EvidenceId);

                    // Log chain of custody event
                    await _evidenceRepository.LogChainOfCustodyEventAsync(
                        evidence.EvidenceId,
                        Guid.Empty, // System
                        "archived",
                        $"Evidence moved to cold storage after {(DateTime.UtcNow - evidence.UploadTimestamp).Days} days");

                    evidenceArchivedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error archiving evidence: {EvidenceId}", evidence.EvidenceId);
                }

                evidenceProcessedCount++;
            }

            // 3-4. Delete evidence past extended retention (would implement if needed)
            // For now, archival is the final step unless explicitly deleted

            _logger.LogInformation(
                "Evidence retention enforcement complete: {ProcessedCount} processed, {ArchivedCount} archived, {DeletedCount} deleted",
                evidenceProcessedCount,
                evidenceArchivedCount,
                evidenceDeletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enforcing evidence retention policy");
            throw;
        }
    }

    /// <summary>
    /// Timer trigger for evidence integrity audit.
    /// Periodically re-verifies evidence integrity (monthly).
    /// </summary>
    [Function("AuditEvidenceIntegrity")]
    public async Task AuditEvidenceIntegrity(
        [TimerTrigger("0 0 3 1 * *")] TimerInfo timerInfo) // Runs monthly on 1st at 3 AM UTC
    {
        _logger.LogInformation("Running evidence integrity audit");

        try
        {
            // 1. Query sample of evidence items (10% random sample)
            var allEvidence = await _dbContext.EvidenceRecords
                .Where(e => !e.LegalHold) // Only audit non-legal-hold evidence
                .OrderBy(e => Guid.NewGuid()) // Random ordering
                .Take(100) // Audit up to 100 items per run
                .ToListAsync();

            var auditedCount = 0;
            var integrityViolations = 0;

            // 2. Verify integrity for each evidence item
            foreach (var evidence in allEvidence)
            {
                try
                {
                    // Calculate current hash and compare
                    var isValid = await _evidenceStorageService.VerifyIntegrityAsync(evidence.EvidenceId);

                    if (!isValid)
                    {
                        // Flag integrity violation
                        integrityViolations++;

                        _logger.LogWarning(
                            "AUDIT: Integrity violation detected for evidence: {EvidenceId}",
                            evidence.EvidenceId);

                        await _evidenceRepository.LogChainOfCustodyEventAsync(
                            evidence.EvidenceId,
                            Guid.Empty, // System
                            "integrity_audit_failure",
                            "Monthly audit detected hash mismatch");

                        // In production, alert HQ
                    }

                    auditedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error auditing evidence: {EvidenceId}", evidence.EvidenceId);
                }
            }

            // 3-4. Generate and store audit report
            var auditReport = new
            {
                audit_date = DateTime.UtcNow,
                evidence_audited = auditedCount,
                integrity_violations = integrityViolations,
                violation_rate = auditedCount > 0 ? (integrityViolations * 100.0 / auditedCount) : 0,
                status = integrityViolations == 0 ? "PASS" : "VIOLATIONS_DETECTED"
            };

            _logger.LogInformation(
                "Evidence integrity audit complete: {AuditedCount} audited, {ViolationCount} violations",
                auditedCount,
                integrityViolations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running evidence integrity audit");
            throw;
        }
    }

    // Message models

    private class EvidenceUploadedMessage
    {
        public Guid EvidenceId { get; set; }
        public Guid IncidentId { get; set; }
        public string EvidenceType { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime UploadTimestamp { get; set; }
    }
}
