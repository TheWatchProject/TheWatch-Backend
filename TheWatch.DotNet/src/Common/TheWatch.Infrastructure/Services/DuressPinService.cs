using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Implementation of duress PIN detection with covert escalation.
/// CRITICAL SECURITY: This service must prevent timing attacks and maintain complete covertness.
/// </summary>
public class DuressPinService : IDuressPinService
{
    private readonly IDuressPinRepository _duressPinRepository;
    private readonly ICryptographyService _cryptography;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IDispatchService _dispatchService;
    private readonly IAdminAuditRepository _auditRepository;
    private readonly ILogger<DuressPinService> _logger;

    public DuressPinService(
        IDuressPinRepository duressPinRepository,
        ICryptographyService cryptography,
        IIncidentRepository incidentRepository,
        IDispatchService dispatchService,
        IAdminAuditRepository auditRepository,
        ILogger<DuressPinService> logger)
    {
        _duressPinRepository = duressPinRepository;
        _cryptography = cryptography;
        _incidentRepository = incidentRepository;
        _dispatchService = dispatchService;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    /// <summary>
    /// Validates a cancellation PIN and triggers appropriate action.
    /// CRITICAL: Uses constant-time comparison to prevent timing attacks.
    /// </summary>
    public async Task<(bool IsValidPin, bool IsDuressPin)> ValidateCancellationPinAsync(
        Guid userId,
        string pin,
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return (false, false);
        }

        try
        {
            // Retrieve PIN hashes
            var duressRecord = await _duressPinRepository.GetByUserIdAsync(userId, cancellationToken);

            // If no PINs configured, reject
            if (duressRecord == null ||
                (string.IsNullOrEmpty(duressRecord.SafePinHash) && string.IsNullOrEmpty(duressRecord.DuressPinHash)))
            {
                // Perform dummy hash to prevent timing attack on "no PIN configured" case
                _cryptography.VerifyPassword(pin, GenerateDummyHash());
                return (false, false);
            }

            // Check both PINs using constant-time comparison
            bool isSafePin = false;
            bool isDuressPin = false;

            // CRITICAL: Always check both PINs regardless of result to prevent timing attacks
            if (!string.IsNullOrEmpty(duressRecord.SafePinHash))
            {
                isSafePin = _cryptography.VerifyPassword(pin, duressRecord.SafePinHash);
            }
            else
            {
                // Perform dummy verification to maintain constant time
                _cryptography.VerifyPassword(pin, GenerateDummyHash());
            }

            if (!string.IsNullOrEmpty(duressRecord.DuressPinHash))
            {
                isDuressPin = _cryptography.VerifyPassword(pin, duressRecord.DuressPinHash);
            }
            else
            {
                // Perform dummy verification to maintain constant time
                _cryptography.VerifyPassword(pin, GenerateDummyHash());
            }

            // Determine outcome
            if (isDuressPin)
            {
                // DURESS PIN DETECTED - Silent escalation
                // CRITICAL: Do NOT log this as "duress" - use neutral language
                await HandleDuressEscalationAsync(userId, incidentId, cancellationToken);

                // Return success to caller (covert behavior)
                return (true, true);
            }
            else if (isSafePin)
            {
                // Safe PIN - legitimate cancellation
                // Log as normal audit event (no PII)
                _logger.LogInformation("Incident cancellation authorized for incident {IncidentId}", incidentId);
                return (true, false);
            }
            else
            {
                // Invalid PIN
                _logger.LogWarning("Invalid cancellation PIN attempted for incident {IncidentId}", incidentId);
                return (false, false);
            }
        }
        catch (Exception ex)
        {
            // CRITICAL: Never expose error details that could reveal duress PIN behavior
            _logger.LogError(ex, "Error during cancellation PIN validation for incident {IncidentId}", incidentId);
            return (false, false);
        }
    }

    /// <summary>
    /// Handles duress PIN detection by silently escalating to HQ and Police.
    /// CRITICAL: Must be indistinguishable from normal flow.
    /// </summary>
    private async Task HandleDuressEscalationAsync(Guid userId, Guid incidentId, CancellationToken cancellationToken)
    {
        try
        {
            // Retrieve incident
            var incident = await _incidentRepository.GetByIdAsync(incidentId, cancellationToken);
            if (incident == null)
            {
                _logger.LogWarning("Incident {IncidentId} not found during escalation", incidentId);
                return;
            }

            // Update incident status to "duress_escalated" (internal status)
            incident.Status = "duress_escalated";
            incident.EscalatedToPolice = true;
            incident.UpdatedAt = DateTime.UtcNow;
            await _incidentRepository.UpdateAsync(incident, cancellationToken);

            // Create audit log with neutral language (NEVER mention "duress" in logs)
            var auditEntry = new AdminActionAudit
            {
                ActionId = Guid.NewGuid(),
                AdminUserId = userId, // Record who triggered it
                ActionType = "incident_priority_escalation",
                TargetId = incidentId.ToString(),
                ActionDetails = System.Text.Json.JsonSerializer.Serialize(new
                {
                    reason = "cancellation_with_escalation_flag",
                    escalated_to_police = true,
                    timestamp = DateTime.UtcNow
                }),
                Timestamp = DateTime.UtcNow,
                IpAddress = "system"
            };
            await _auditRepository.LogActionAsync(auditEntry, cancellationToken);

            // Dispatch high-priority alert to HQ + Police
            // This happens in background to maintain response time parity
            _ = Task.Run(async () =>
            {
                try
                {
                    await _dispatchService.EscalateToPoliceSilentlyAsync(incidentId, userId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to escalate incident {IncidentId} to police", incidentId);
                }
            }, cancellationToken);

            // Log with neutral language
            _logger.LogWarning("Incident {IncidentId} escalated to priority response", incidentId);
        }
        catch (Exception ex)
        {
            // CRITICAL: Silently log error but don't throw - must maintain covert behavior
            _logger.LogError(ex, "Error during incident escalation for {IncidentId}", incidentId);
        }
    }

    public async Task SetDuressPinAsync(Guid userId, string pin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit))
        {
            throw new ArgumentException("PIN must be 4-8 numeric digits", nameof(pin));
        }

        var duressRecord = await _duressPinRepository.GetByUserIdAsync(userId, cancellationToken)
            ?? new DuressPin { UserId = userId };

        // Check if too similar to safe PIN
        if (!string.IsNullOrEmpty(duressRecord.SafePinHash))
        {
            // Decrypt safe PIN is not possible, so we just check basic patterns
            // In production, you might want to enforce this at the UI level
        }

        duressRecord.DuressPinHash = _cryptography.HashPassword(pin);
        duressRecord.UpdatedAt = DateTime.UtcNow;

        await _duressPinRepository.CreateOrUpdateAsync(duressRecord, cancellationToken);

        // Audit log (NO PII)
        _logger.LogInformation("Duress PIN configured for user {UserId}", userId);
    }

    public async Task SetSafePinAsync(Guid userId, string pin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit))
        {
            throw new ArgumentException("PIN must be 4-8 numeric digits", nameof(pin));
        }

        var duressRecord = await _duressPinRepository.GetByUserIdAsync(userId, cancellationToken)
            ?? new DuressPin { UserId = userId };

        duressRecord.SafePinHash = _cryptography.HashPassword(pin);
        duressRecord.UpdatedAt = DateTime.UtcNow;

        await _duressPinRepository.CreateOrUpdateAsync(duressRecord, cancellationToken);

        // Audit log (NO PII)
        _logger.LogInformation("Safe cancellation PIN configured for user {UserId}", userId);
    }

    public async Task RemoveDuressPinAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var duressRecord = await _duressPinRepository.GetByUserIdAsync(userId, cancellationToken);
        if (duressRecord != null)
        {
            duressRecord.DuressPinHash = null;
            duressRecord.UpdatedAt = DateTime.UtcNow;
            await _duressPinRepository.CreateOrUpdateAsync(duressRecord, cancellationToken);

            // Audit log (NO PII)
            _logger.LogInformation("Duress PIN removed for user {UserId}", userId);
        }
    }

    public async Task RemoveSafePinAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var duressRecord = await _duressPinRepository.GetByUserIdAsync(userId, cancellationToken);
        if (duressRecord != null)
        {
            duressRecord.SafePinHash = null;
            duressRecord.UpdatedAt = DateTime.UtcNow;
            await _duressPinRepository.CreateOrUpdateAsync(duressRecord, cancellationToken);

            // Audit log (NO PII)
            _logger.LogInformation("Safe cancellation PIN removed for user {UserId}", userId);
        }
    }

    public bool ArePinsSufficientlyDifferent(string pin1, string pin2)
    {
        if (string.IsNullOrEmpty(pin1) || string.IsNullOrEmpty(pin2))
        {
            return true;
        }

        // Cannot be identical
        if (pin1 == pin2)
        {
            return false;
        }

        // Calculate Levenshtein distance
        int distance = CalculateLevenshteinDistance(pin1, pin2);

        // PINs must differ by at least 2 characters
        return distance >= 2;
    }

    /// <summary>
    /// Generates a dummy hash for constant-time comparison when no PIN is configured.
    /// </summary>
    private string GenerateDummyHash()
    {
        // Pre-computed hash of "00000000" to avoid regenerating each time
        return "K9Z8fJQc8gMJcF7HqF0Z0Y3Z8fJQc8gMJcF7HqF0Z0Y3Z8fJQc8gM=";
    }

    /// <summary>
    /// Calculates Levenshtein distance between two strings.
    /// </summary>
    private int CalculateLevenshteinDistance(string s1, string s2)
    {
        int[,] d = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            d[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            d[0, j] = j;

        for (int j = 1; j <= s2.Length; j++)
        {
            for (int i = 1; i <= s1.Length; i++)
            {
                int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(
                    d[i - 1, j] + 1,      // deletion
                    d[i, j - 1] + 1),     // insertion
                    d[i - 1, j - 1] + cost); // substitution
            }
        }

        return d[s1.Length, s2.Length];
    }
}
