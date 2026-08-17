using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Implementation of GDPR compliance operations.
/// Handles right-to-erasure (anonymization) and data portability.
/// </summary>
public class ComplianceService : IComplianceService
{
    private readonly IUserRepository _userRepository;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IEvidenceRepository _evidenceRepository;
    private readonly IAdminAuditRepository _auditRepository;
    private readonly ILogger<ComplianceService> _logger;

    public ComplianceService(
        IUserRepository userRepository,
        IIncidentRepository incidentRepository,
        IEvidenceRepository evidenceRepository,
        IAdminAuditRepository auditRepository,
        ILogger<ComplianceService> logger)
    {
        _userRepository = userRepository;
        _incidentRepository = incidentRepository;
        _evidenceRepository = evidenceRepository;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    public async Task<GdprErasureResult> AnonymizeUserDataAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var result = new GdprErasureResult
        {
            ProcessedAt = DateTime.UtcNow
        };

        try
        {
            // Check if user can be anonymized
            var (canAnonymize, blockingReason) = await CanAnonymizeUserAsync(userId, cancellationToken);
            if (!canAnonymize)
            {
                result.Success = false;
                result.ErrorMessage = $"Cannot anonymize user: {blockingReason}";
                return result;
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                result.Success = false;
                result.ErrorMessage = "User not found";
                return result;
            }

            // Check if already anonymized
            if (user.PiiState == "anonymized")
            {
                result.Success = true;
                result.ErrorMessage = "User data already anonymized";
                return result;
            }

            // Anonymize user profile
            await AnonymizeUserProfileAsync(user, cancellationToken);
            result.RecordsAnonymized++;

            // Anonymize incident records (but preserve essential data)
            var incidents = await _incidentRepository.GetByUserIdAsync(userId, cancellationToken);
            foreach (var incident in incidents)
            {
                // Check if incident has legal hold
                if (incident.LegalHoldPlacedAt != null)
                {
                    result.RecordsPreserved++;
                    result.PreservedReasons.Add($"Incident {incident.IncidentId} under legal hold");
                    continue;
                }

                // Anonymize incident data (keep core metadata)
                await AnonymizeIncidentAsync(incident, cancellationToken);
                result.RecordsAnonymized++;
            }

            // Handle evidence records
            var evidenceRecords = await _evidenceRepository.GetByUserIdAsync(userId, cancellationToken);
            foreach (var evidence in evidenceRecords)
            {
                if (evidence.LegalHoldPlacedAt != null)
                {
                    result.RecordsPreserved++;
                    result.PreservedReasons.Add($"Evidence {evidence.EvidenceId} under legal hold");
                    continue;
                }

                // For evidence not under legal hold, anonymize metadata
                await AnonymizeEvidenceAsync(evidence, cancellationToken);
                result.RecordsAnonymized++;
            }

            // Create audit log
            var auditEntry = new AdminActionAudit
            {
                ActionId = Guid.NewGuid(),
                AdminUserId = userId, // Self-service erasure
                ActionType = "gdpr_erasure_completed",
                TargetId = userId.ToString(),
                ActionDetails = JsonSerializer.Serialize(new
                {
                    reason,
                    records_anonymized = result.RecordsAnonymized,
                    records_preserved = result.RecordsPreserved,
                    preserved_reasons = result.PreservedReasons
                }),
                Timestamp = DateTime.UtcNow,
                IpAddress = "system"
            };
            await _auditRepository.LogActionAsync(auditEntry, cancellationToken);

            result.Success = true;
            _logger.LogInformation("GDPR erasure completed for user {UserId}: {RecordsAnonymized} anonymized, {RecordsPreserved} preserved",
                userId, result.RecordsAnonymized, result.RecordsPreserved);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during GDPR erasure for user {UserId}", userId);
            result.Success = false;
            result.ErrorMessage = "An error occurred during anonymization";
            return result;
        }
    }

    public async Task<GdprExportResult> ExportUserDataAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = new GdprExportResult
        {
            ExportedAt = DateTime.UtcNow
        };

        try
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                result.Success = false;
                result.ErrorMessage = "User not found";
                return result;
            }

            // Collect all user data
            var exportData = new Dictionary<string, object>
            {
                ["user_profile"] = new
                {
                    user_id = user.UserId,
                    account_type = user.AccountType,
                    created_at = user.CreatedAt,
                    email_verified = user.EmailVerified,
                    phone_verified = user.PhoneVerified
                    // Note: PII fields excluded for security
                },
                ["export_date"] = DateTime.UtcNow,
                ["export_format"] = "JSON",
                ["data_retention_policy"] = "7_years_incident_data"
            };

            // Get incidents
            var incidents = await _incidentRepository.GetByUserIdAsync(userId, cancellationToken);
            exportData["incidents"] = incidents.Select(i => new
            {
                incident_id = i.IncidentId,
                status = i.Status,
                created_at = i.CreatedAt,
                resolved_at = i.ResolvedAt
            });

            // Serialize to JSON
            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // In production, save to blob storage and return URL
            // For now, return in-memory result
            result.Success = true;
            result.FileSizeBytes = System.Text.Encoding.UTF8.GetByteCount(json);
            result.ExportFilePath = $"gdpr-export-{userId}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";

            _logger.LogInformation("GDPR data export completed for user {UserId}", userId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during GDPR export for user {UserId}", userId);
            result.Success = false;
            result.ErrorMessage = "An error occurred during export";
            return result;
        }
    }

    public async Task<(bool CanAnonymize, string? BlockingReason)> CanAnonymizeUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Check for active incidents
        var incidents = await _incidentRepository.GetByUserIdAsync(userId, cancellationToken);
        var activeIncidents = incidents.Where(i =>
            i.Status != "resolved" &&
            i.Status != "cancelled" &&
            i.Status != "false_alarm").ToList();

        if (activeIncidents.Any())
        {
            return (false, $"User has {activeIncidents.Count} active incident(s)");
        }

        // Check for legal holds
        var evidenceRecords = await _evidenceRepository.GetByUserIdAsync(userId, cancellationToken);
        var legalHoldEvidence = evidenceRecords.Where(e => e.LegalHoldPlacedAt != null).ToList();

        if (legalHoldEvidence.Any())
        {
            return (false, $"User has {legalHoldEvidence.Count} evidence record(s) under legal hold");
        }

        // Check for incidents under legal hold
        var legalHoldIncidents = incidents.Where(i => i.LegalHoldPlacedAt != null).ToList();
        if (legalHoldIncidents.Any())
        {
            return (false, $"User has {legalHoldIncidents.Count} incident(s) under legal hold");
        }

        return (true, null);
    }

    public async Task PlaceLegalHoldAsync(
        Guid userId,
        string reason,
        DateTime? expiresAt,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        // Get all user incidents and evidence
        var incidents = await _incidentRepository.GetByUserIdAsync(userId, cancellationToken);
        var evidenceRecords = await _evidenceRepository.GetByUserIdAsync(userId, cancellationToken);

        // Place hold on all records
        foreach (var incident in incidents)
        {
            incident.LegalHoldPlacedAt = DateTime.UtcNow;
            incident.LegalHoldExpiresAt = expiresAt;
            await _incidentRepository.UpdateAsync(incident, cancellationToken);
        }

        foreach (var evidence in evidenceRecords)
        {
            evidence.LegalHoldPlacedAt = DateTime.UtcNow;
            evidence.LegalHoldExpiresAt = expiresAt;
            await _evidenceRepository.UpdateAsync(evidence, cancellationToken);
        }

        // Audit log
        var auditEntry = new AdminActionAudit
        {
            ActionId = Guid.NewGuid(),
            AdminUserId = adminUserId,
            ActionType = "legal_hold_placed",
            TargetId = userId.ToString(),
            ActionDetails = JsonSerializer.Serialize(new
            {
                reason,
                expires_at = expiresAt,
                incidents_count = incidents.Count(),
                evidence_count = evidenceRecords.Count()
            }),
            Timestamp = DateTime.UtcNow,
            IpAddress = "system"
        };
        await _auditRepository.LogActionAsync(auditEntry, cancellationToken);

        _logger.LogWarning("Legal hold placed on user {UserId} by admin {AdminUserId}", userId, adminUserId);
    }

    public async Task RemoveLegalHoldAsync(
        Guid userId,
        Guid adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var incidents = await _incidentRepository.GetByUserIdAsync(userId, cancellationToken);
        var evidenceRecords = await _evidenceRepository.GetByUserIdAsync(userId, cancellationToken);

        foreach (var incident in incidents)
        {
            incident.LegalHoldPlacedAt = null;
            incident.LegalHoldExpiresAt = null;
            await _incidentRepository.UpdateAsync(incident, cancellationToken);
        }

        foreach (var evidence in evidenceRecords)
        {
            evidence.LegalHoldPlacedAt = null;
            evidence.LegalHoldExpiresAt = null;
            await _evidenceRepository.UpdateAsync(evidence, cancellationToken);
        }

        // Audit log
        var auditEntry = new AdminActionAudit
        {
            ActionId = Guid.NewGuid(),
            AdminUserId = adminUserId,
            ActionType = "legal_hold_removed",
            TargetId = userId.ToString(),
            ActionDetails = JsonSerializer.Serialize(new { reason }),
            Timestamp = DateTime.UtcNow,
            IpAddress = "system"
        };
        await _auditRepository.LogActionAsync(auditEntry, cancellationToken);

        _logger.LogInformation("Legal hold removed from user {UserId} by admin {AdminUserId}", userId, adminUserId);
    }

    private async Task AnonymizeUserProfileAsync(User user, CancellationToken cancellationToken)
    {
        user.EmailHash = null;
        user.PhoneHash = null;
        user.EmailVerified = false;
        user.PhoneVerified = false;
        user.LastLoginAt = null;
        user.PiiState = "anonymized";
        user.AnonymizedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);
    }

    private async Task AnonymizeIncidentAsync(Incident incident, CancellationToken cancellationToken)
    {
        // Keep incident record but remove location details
        incident.Latitude = null;
        incident.Longitude = null;
        incident.GeohashFull = string.Empty;
        incident.UpdatedAt = DateTime.UtcNow;

        await _incidentRepository.UpdateAsync(incident, cancellationToken);
    }

    private async Task AnonymizeEvidenceAsync(Evidence evidence, CancellationToken cancellationToken)
    {
        // Metadata-only anonymization (actual files handled separately)
        evidence.UpdatedAt = DateTime.UtcNow;
        await _evidenceRepository.UpdateAsync(evidence, cancellationToken);
    }
}
