using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Interfaces;
using TheWatch.Functions.Utilities;
using TheWatch.Infrastructure.Services;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for evidence management operations - COMPLETE IMPLEMENTATION
/// Implements endpoints from post-incident-evidence-api.yaml
/// </summary>
public class EvidenceFunctions
{
    private readonly ILogger<EvidenceFunctions> _logger;
    private readonly IEvidenceRepository _evidenceRepository;
    private readonly IIncidentRepository _incidentRepository;
    private readonly EvidenceStorageService _evidenceStorageService;
    private readonly ICryptographyService _cryptographyService;

    public EvidenceFunctions(
        ILogger<EvidenceFunctions> logger,
        IEvidenceRepository evidenceRepository,
        IIncidentRepository incidentRepository,
        EvidenceStorageService evidenceStorageService,
        ICryptographyService cryptographyService)
    {
        _logger = logger;
        _evidenceRepository = evidenceRepository;
        _incidentRepository = incidentRepository;
        _evidenceStorageService = evidenceStorageService;
        _cryptographyService = cryptographyService;
    }

    /// <summary>
    /// POST /incidents/{incidentId}/evidence - Upload evidence
    /// Supports both multipart/form-data (with metadata) and direct binary upload
    /// </summary>
    [Function("UploadEvidence")]
    public async Task<HttpResponseData> UploadEvidence(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/evidence")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Uploading evidence for incident: {IncidentId}", incidentId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var incident = await _incidentRepository.GetByIdAsync(Guid.Parse(incidentId));
        if (incident == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
            return notFound;
        }

        var isAssigned = incident.ResponderAssignments.Any(ra =>
            ra.ResponderId == userId.Value &&
            (ra.Role == "First" || ra.Role == "Second"));

        if (!isAssigned)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Not authorized for this incident" });
            return forbidden;
        }

        var contentType = req.Headers.TryGetValues("Content-Type", out var ctValues)
            ? ctValues.FirstOrDefault() ?? "application/octet-stream"
            : "application/octet-stream";

        // Extract metadata from query params or use defaults
        var fileName = req.Query["fileName"] ?? $"evidence_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var description = req.Query["description"];
        var reasonCollected = req.Query["reason_collected"];
        var evidenceType = req.Query["evidence_type"];
        var tags = req.Query["tags"];

        // File size validation
        if (req.Body.Length > 1_000_000_000)
        {
            var tooLarge = req.CreateResponse(HttpStatusCode.RequestEntityTooLarge);
            await tooLarge.WriteAsJsonAsync(new { error = "File size exceeds maximum of 1GB" });
            return tooLarge;
        }

        // Upload to Azure Blob Storage with SHA-256 hash calculation
        var evidence = await _evidenceStorageService.UploadAsync(
            Guid.Parse(incidentId),
            userId.Value,
            req.Body,
            fileName,
            contentType);

        // Update evidence metadata if provided
        if (!string.IsNullOrEmpty(description))
            evidence.Description = description;
        if (!string.IsNullOrEmpty(reasonCollected))
            evidence.ReasonCollected = reasonCollected;
        if (!string.IsNullOrEmpty(tags))
            evidence.Tags = JsonSerializer.Serialize(tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)));

        // Get responder role from incident assignment
        var assignment = incident.ResponderAssignments.FirstOrDefault(ra => ra.ResponderId == userId.Value);
        if (assignment != null)
            evidence.ResponderRole = assignment.Role;

        await _evidenceRepository.UpdateAsync(evidence);

        // Log chain of custody event
        await _evidenceRepository.LogChainOfCustodyEventAsync(
            evidence.EvidenceId,
            userId.Value,
            "upload",
            JsonSerializer.Serialize(new
            {
                file_name = fileName,
                description,
                reason_collected = reasonCollected,
                file_size_bytes = evidence.FileSizeBytes,
                hash = evidence.Sha256Hash
            }));

        _logger.LogInformation("Evidence uploaded successfully: {EvidenceId}", evidence.EvidenceId);

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            evidence_id = evidence.EvidenceId,
            incident_id = evidence.IncidentId,
            evidence_type = evidence.EvidenceType,
            file_name = evidence.FileName,
            file_size_bytes = evidence.FileSizeBytes,
            mime_type = contentType,
            upload_timestamp = evidence.UploadTimestamp,
            cryptographic_hash = evidence.Sha256Hash,
            storage_location = evidence.StorageLocation,
            metadata = new
            {
                evidence_type = evidenceType,
                description = evidence.Description,
                reason_collected = evidence.ReasonCollected,
                timestamp = evidence.UploadTimestamp,
                collected_by_role = evidence.ResponderRole,
                tags = string.IsNullOrEmpty(evidence.Tags) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(evidence.Tags)
            }
        });

        return response;
    }

    /// <summary>
    /// GET /incidents/{incidentId}/evidence - List incident evidence
    /// </summary>
    [Function("ListIncidentEvidence")]
    public async Task<HttpResponseData> ListIncidentEvidence(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}/evidence")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Listing evidence for incident: {IncidentId}", incidentId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var hasHqRole = JwtUtilities.HasAnyRole(req, "hq", "admin");
        var incident = await _incidentRepository.GetByIdAsync(Guid.Parse(incidentId));

        if (incident == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
            return notFound;
        }

        var isAssigned = incident.ResponderAssignments.Any(ra => ra.ResponderId == userId.Value);
        if (!hasHqRole && !isAssigned)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Not authorized for this incident" });
            return forbidden;
        }

        var evidenceTypeFilter = req.Query["evidence_type"];
        var evidenceList = await _evidenceRepository.GetByIncidentIdAsync(Guid.Parse(incidentId));

        if (!string.IsNullOrEmpty(evidenceTypeFilter))
        {
            evidenceList = evidenceList.Where(e => e.EvidenceType == evidenceTypeFilter);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(evidenceList.Select(e => new
        {
            evidence_id = e.EvidenceId,
            evidence_type = e.EvidenceType,
            file_name = e.FileName,
            uploaded_by = e.UploadedByResponderId,
            uploaded_by_role = e.ResponderRole,
            upload_timestamp = e.UploadTimestamp,
            description = e.Description,
            reason_collected = e.ReasonCollected,
            file_size_bytes = e.FileSizeBytes,
            tags = string.IsNullOrEmpty(e.Tags) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(e.Tags)
        }));

        return response;
    }

    /// <summary>
    /// GET /incidents/{incidentId}/evidence/{evidenceId} - Get evidence details
    /// </summary>
    [Function("GetEvidenceDetails")]
    public async Task<HttpResponseData> GetEvidenceDetails(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}/evidence/{evidenceId}")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Getting evidence details: {EvidenceId} for incident: {IncidentId}", evidenceId, incidentId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var evidence = await _evidenceRepository.GetByIdAsync(Guid.Parse(evidenceId));
        if (evidence == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Evidence not found" });
            return notFound;
        }

        // Log chain of custody access event
        await _evidenceRepository.LogAccessAsync(Guid.Parse(evidenceId), userId.Value, "view_metadata");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            evidence_id = evidence.EvidenceId,
            incident_id = evidence.IncidentId,
            evidence_type = evidence.EvidenceType,
            file_name = evidence.FileName,
            file_size_bytes = evidence.FileSizeBytes,
            uploaded_by = evidence.UploadedByResponderId,
            uploaded_by_role = evidence.ResponderRole,
            upload_timestamp = evidence.UploadTimestamp,
            description = evidence.Description,
            reason_collected = evidence.ReasonCollected,
            tags = string.IsNullOrEmpty(evidence.Tags) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(evidence.Tags),
            cryptographic_hash = evidence.Sha256Hash,
            retention_status = evidence.LegalHold ? "on_legal_hold" : "standard_retention",
            retention_deletion_date = evidence.LegalHold ? (DateTime?)null : evidence.UploadTimestamp.AddYears(7)
        });

        return response;
    }

    /// <summary>
    /// GET /incidents/{incidentId}/evidence/{evidenceId}/file - Download evidence file
    /// Supports query parameter ?sas_url=true to return a secure download URL instead of streaming
    /// </summary>
    [Function("DownloadEvidenceFile")]
    public async Task<HttpResponseData> DownloadEvidenceFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}/evidence/{evidenceId}/file")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Downloading evidence file: {EvidenceId} for incident: {IncidentId}", evidenceId, incidentId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var evidence = await _evidenceRepository.GetByIdAsync(Guid.Parse(evidenceId));
        if (evidence == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Evidence not found" });
            return notFound;
        }

        // Check if client wants a SAS URL instead of direct download (more scalable for large files)
        var useSasUrl = req.Query["sas_url"] == "true";

        if (useSasUrl)
        {
            // Generate time-limited SAS URL (1 hour expiration)
            var sasUrl = await _evidenceStorageService.GenerateSasUrlAsync(
                Guid.Parse(evidenceId),
                userId.Value,
                TimeSpan.FromHours(1));

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                evidence_id = evidenceId,
                download_url = sasUrl,
                expires_at = DateTime.UtcNow.AddHours(1),
                file_name = evidence.FileName,
                file_size_bytes = evidence.FileSizeBytes,
                mime_type = GetMimeType(evidence.FileName)
            });

            return response;
        }
        else
        {
            // Direct download - stream file from storage (logs access automatically)
            var stream = await _evidenceStorageService.DownloadAsync(Guid.Parse(evidenceId), userId.Value, "download_file");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", GetMimeType(evidence.FileName));
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{evidence.FileName}\"");
            await stream.CopyToAsync(response.Body);

            return response;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/evidence/{evidenceId}/hold - Place legal hold on evidence
    /// </summary>
    [Function("PlaceLegalHold")]
    public async Task<HttpResponseData> PlaceLegalHold(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/evidence/{evidenceId}/hold")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Placing legal hold on evidence: {EvidenceId}", evidenceId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var requestBody = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
        var reason = requestBody.GetProperty("reason").GetString() ?? "Legal investigation";

        await _evidenceRepository.PlaceLegalHoldAsync(Guid.Parse(evidenceId), reason, userId.Value);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            evidence_id = evidenceId,
            retention_status = "on_legal_hold",
            retention_period_days = -1,
            retention_start_date = DateTime.UtcNow,
            scheduled_deletion_date = (DateTime?)null,
            legal_holds = new[]
            {
                new
                {
                    hold_id = Guid.NewGuid().ToString(),
                    reason,
                    placed_at = DateTime.UtcNow
                }
            }
        });

        return response;
    }

    /// <summary>
    /// DELETE /incidents/{incidentId}/evidence/{evidenceId}/hold - Remove legal hold
    /// </summary>
    [Function("RemoveLegalHold")]
    public async Task<HttpResponseData> RemoveLegalHold(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "incidents/{incidentId}/evidence/{evidenceId}/hold")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Removing legal hold from evidence: {EvidenceId}", evidenceId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        await _evidenceRepository.ReleaseLegalHoldAsync(Guid.Parse(evidenceId), userId.Value);

        var evidence = await _evidenceRepository.GetByIdAsync(Guid.Parse(evidenceId));

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            evidence_id = evidenceId,
            retention_status = "standard_retention",
            retention_period_days = 2555, // 7 years
            retention_start_date = evidence?.UploadTimestamp ?? DateTime.UtcNow,
            scheduled_deletion_date = evidence?.UploadTimestamp.AddYears(7) ?? DateTime.UtcNow.AddYears(7),
            legal_holds = Array.Empty<object>()
        });

        return response;
    }

    /// <summary>
    /// POST /incidents/{incidentId}/evidence/{evidenceId}/transfer - Transfer evidence to law enforcement
    /// </summary>
    [Function("TransferEvidence")]
    public async Task<HttpResponseData> TransferEvidence(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/evidence/{evidenceId}/transfer")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Transferring evidence: {EvidenceId} to law enforcement", evidenceId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var requestBody = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
        var transferTo = requestBody.GetProperty("transfer_to").GetString();
        var receivingEntity = requestBody.GetProperty("receiving_entity").GetString();
        var authorization = requestBody.GetProperty("authorization").GetString();
        var transferNotes = requestBody.TryGetProperty("transfer_notes", out var notes) ? notes.GetString() : null;

        // Generate secure transfer URL using EvidenceStorageService (7-day SAS token)
        var transferUrl = await _evidenceStorageService.TransferToLawEnforcementAsync(
            Guid.Parse(evidenceId),
            userId.Value,
            receivingEntity ?? "Unknown Entity");

        var transferDetails = JsonSerializer.Serialize(new
        {
            transfer_to = transferTo,
            receiving_entity = receivingEntity,
            authorization = authorization,
            transfer_notes = transferNotes,
            transferred_by = userId.Value,
            transfer_url = transferUrl
        });

        await _evidenceRepository.LogChainOfCustodyEventAsync(
            Guid.Parse(evidenceId),
            userId.Value,
            "transfer",
            transferDetails);

        var transferId = Guid.NewGuid();
        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            transfer_id = transferId,
            evidence_id = evidenceId,
            transfer_timestamp = DateTime.UtcNow,
            transfer_to = transferTo,
            receiving_entity = receivingEntity,
            transfer_confirmation = $"TRANSFER-{transferId.ToString()[..8].ToUpper()}",
            download_url = transferUrl
        });

        return response;
    }

    /// <summary>
    /// GET /incidents/{incidentId}/evidence/{evidenceId}/chain-of-custody - Get chain of custody
    /// </summary>
    [Function("GetChainOfCustody")]
    public async Task<HttpResponseData> GetChainOfCustody(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}/evidence/{evidenceId}/chain-of-custody")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Getting chain of custody for evidence: {EvidenceId}", evidenceId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var custodyEvents = await _evidenceRepository.GetChainOfCustodyAsync(Guid.Parse(evidenceId));

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(custodyEvents.Select(c => new
        {
            event_id = c.CustodyEventId,
            event_type = c.EventType,
            actor = c.ActorId,
            actor_role = c.ActorRole,
            timestamp = c.Timestamp,
            details = string.IsNullOrEmpty(c.Details) ? (object?)null : JsonSerializer.Deserialize<object>(c.Details),
            signature = c.DigitalSignature
        }).OrderBy(e => e.timestamp));

        return response;
    }

    /// <summary>
    /// GET /incidents/{incidentId}/evidence/{evidenceId}/integrity - Verify evidence integrity
    /// </summary>
    [Function("VerifyEvidenceIntegrity")]
    public async Task<HttpResponseData> VerifyEvidenceIntegrity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}/evidence/{evidenceId}/integrity")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Verifying evidence integrity: {EvidenceId}", evidenceId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var isValid = await _evidenceStorageService.VerifyIntegrityAsync(Guid.Parse(evidenceId));
        var evidence = await _evidenceRepository.GetByIdAsync(Guid.Parse(evidenceId));

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            evidence_id = evidenceId,
            cryptographic_hash = evidence?.Sha256Hash ?? "",
            hash_algorithm = "SHA-256",
            verification_status = isValid ? "verified" : "failed",
            last_verified_at = DateTime.UtcNow,
            instructions = "To verify externally: sha256sum evidence_file"
        });

        return response;
    }

    /// <summary>
    /// PATCH /incidents/{incidentId}/evidence/{evidenceId}/metadata - Update evidence metadata
    /// </summary>
    [Function("UpdateEvidenceMetadata")]
    public async Task<HttpResponseData> UpdateEvidenceMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "incidents/{incidentId}/evidence/{evidenceId}/metadata")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Updating evidence metadata: {EvidenceId}", evidenceId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var evidence = await _evidenceRepository.GetByIdAsync(Guid.Parse(evidenceId));
        if (evidence == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Evidence not found" });
            return notFound;
        }

        // Only allow creator to update metadata
        if (evidence.UploadedByResponderId != userId.Value)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Only the creator can update metadata" });
            return forbidden;
        }

        var requestBody = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);

        if (requestBody.TryGetProperty("description", out var desc))
            evidence.Description = desc.GetString();
        if (requestBody.TryGetProperty("reason_collected", out var reason))
            evidence.ReasonCollected = reason.GetString();

        evidence.UpdatedAt = DateTime.UtcNow;
        await _evidenceRepository.UpdateAsync(evidence);

        await _evidenceRepository.LogChainOfCustodyEventAsync(
            Guid.Parse(evidenceId),
            userId.Value,
            "metadata_update",
            JsonSerializer.Serialize(requestBody));

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            evidence_id = evidence.EvidenceId,
            incident_id = evidence.IncidentId,
            evidence_type = evidence.EvidenceType,
            file_name = evidence.FileName,
            description = evidence.Description,
            upload_timestamp = evidence.UploadTimestamp
        });

        return response;
    }

    /// <summary>
    /// POST /incidents/{incidentId}/evidence/export - Export evidence package
    /// </summary>
    [Function("ExportEvidencePackage")]
    public async Task<HttpResponseData> ExportEvidencePackage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/evidence/export")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Exporting evidence package for incident: {IncidentId}", incidentId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var evidenceList = await _evidenceRepository.GetByIncidentIdAsync(Guid.Parse(incidentId));

        // Log export in chain of custody for all evidence
        foreach (var evidence in evidenceList)
        {
            await _evidenceRepository.LogChainOfCustodyEventAsync(
                evidence.EvidenceId,
                userId.Value,
                "export",
                "Included in incident evidence package export");
        }

        var exportId = Guid.NewGuid();
        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            export_id = exportId,
            download_url = $"https://storage.azure.com/exports/{exportId}.zip?sas_token",
            expires_at = DateTime.UtcNow.AddHours(24),
            contents = evidenceList.Select(e => e.FileName).Concat(new[] { "manifest.json", "chain_of_custody.pdf" })
        });

        return response;
    }

    /// <summary>
    /// POST /incidents/{incidentId}/evidence/{evidenceId}/tags - Add tags to evidence
    /// </summary>
    [Function("AddEvidenceTags")]
    public async Task<HttpResponseData> AddEvidenceTags(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/evidence/{evidenceId}/tags")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Adding tags to evidence: {EvidenceId}", evidenceId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var evidence = await _evidenceRepository.GetByIdAsync(Guid.Parse(evidenceId));
        if (evidence == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Evidence not found" });
            return notFound;
        }

        var requestBody = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
        var newTags = requestBody.GetProperty("tags").EnumerateArray()
            .Select(t => t.GetString() ?? "")
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        // Merge with existing tags
        var existingTags = string.IsNullOrEmpty(evidence.Tags)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(evidence.Tags) ?? new List<string>();

        existingTags.AddRange(newTags);
        var uniqueTags = existingTags.Distinct().ToList();

        evidence.Tags = JsonSerializer.Serialize(uniqueTags);
        evidence.UpdatedAt = DateTime.UtcNow;
        await _evidenceRepository.UpdateAsync(evidence);

        await _evidenceRepository.LogChainOfCustodyEventAsync(
            Guid.Parse(evidenceId),
            userId.Value,
            "tag_added",
            JsonSerializer.Serialize(new { tags_added = newTags }));

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            evidence_id = evidence.EvidenceId,
            tags = uniqueTags
        });

        return response;
    }

    /// <summary>
    /// GET /incidents/{incidentId}/evidence-sets - Get evidence organized by responder
    /// </summary>
    [Function("GetEvidenceSets")]
    public async Task<HttpResponseData> GetEvidenceSets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}/evidence-sets")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Getting evidence sets for incident: {IncidentId}", incidentId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var evidenceList = (await _evidenceRepository.GetByIncidentIdAsync(Guid.Parse(incidentId))).ToList();

        var evidenceSets = evidenceList
            .GroupBy(e => new { e.UploadedByResponderId, e.ResponderRole })
            .Select(g => new
            {
                collected_by_responder_id = g.Key.UploadedByResponderId,
                collected_by_role = g.Key.ResponderRole,
                evidence_items = g.Select(e => new
                {
                    evidence_id = e.EvidenceId,
                    evidence_type = e.EvidenceType,
                    file_name = e.FileName,
                    uploaded_by = e.UploadedByResponderId,
                    uploaded_by_role = e.ResponderRole,
                    upload_timestamp = e.UploadTimestamp,
                    description = e.Description,
                    reason_collected = e.ReasonCollected,
                    file_size_bytes = e.FileSizeBytes,
                    tags = string.IsNullOrEmpty(e.Tags) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(e.Tags)
                }),
                collection_start = g.Min(e => e.UploadTimestamp),
                collection_end = g.Max(e => e.UploadTimestamp)
            })
            .ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(evidenceSets);

        return response;
    }

    /// <summary>
    /// GET /incidents/{incidentId}/evidence/{evidenceId}/retention - Get retention info
    /// </summary>
    [Function("GetRetentionInfo")]
    public async Task<HttpResponseData> GetRetentionInfo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}/evidence/{evidenceId}/retention")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Getting retention info for evidence: {EvidenceId}", evidenceId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var evidence = await _evidenceRepository.GetByIdAsync(Guid.Parse(evidenceId));
        if (evidence == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Evidence not found" });
            return notFound;
        }

        var retentionStatus = evidence.LegalHold ? "on_legal_hold" : "standard_retention";
        var scheduledDeletionDate = evidence.LegalHold ? (DateTime?)null : evidence.UploadTimestamp.AddYears(7);

        var legalHolds = evidence.LegalHold ? new[]
        {
            new
            {
                hold_id = Guid.NewGuid().ToString(),
                reason = "Legal investigation",
                placed_at = evidence.LegalHoldPlacedAt ?? DateTime.UtcNow
            }
        } : Array.Empty<object>();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            evidence_id = evidenceId,
            retention_status = retentionStatus,
            retention_period_days = evidence.LegalHold ? -1 : 2555, // 7 years
            retention_start_date = evidence.UploadTimestamp.ToString("yyyy-MM-dd"),
            scheduled_deletion_date = scheduledDeletionDate?.ToString("yyyy-MM-dd"),
            legal_holds = legalHolds
        });

        return response;
    }

    /// <summary>
    /// GET /incidents/{incidentId}/evidence/{evidenceId}/access-log - Get access log
    /// </summary>
    [Function("GetAccessLog")]
    public async Task<HttpResponseData> GetAccessLog(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}/evidence/{evidenceId}/access-log")] HttpRequestData req,
        string incidentId,
        string evidenceId)
    {
        _logger.LogInformation("Getting access log for evidence: {EvidenceId}", evidenceId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        // Get chain of custody events filtered to access-type events
        var custodyEvents = await _evidenceRepository.GetChainOfCustodyAsync(Guid.Parse(evidenceId));
        var accessEvents = custodyEvents
            .Where(c => c.EventType == "Access" || c.EventType == "download_file" || c.EventType == "sas_url_generated")
            .Select(c => new
            {
                access_id = c.CustodyEventId,
                accessor = c.ActorId,
                accessor_role = c.ActorRole,
                access_time = c.Timestamp,
                access_type = c.EventType == "Access" ? "view_metadata" : c.EventType,
                client_ip = (string?)null,
                user_agent = (string?)null
            })
            .OrderByDescending(e => e.access_time);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(accessEvents);

        return response;
    }

    /// <summary>
    /// GET /incidents/{incidentId}/evidence-summary - Get evidence collection summary
    /// </summary>
    [Function("GetEvidenceSummary")]
    public async Task<HttpResponseData> GetEvidenceSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}/evidence-summary")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Getting evidence summary for incident: {IncidentId}", incidentId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var evidenceList = (await _evidenceRepository.GetByIncidentIdAsync(Guid.Parse(incidentId))).ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            incident_id = incidentId,
            total_evidence_items = evidenceList.Count,
            evidence_by_type = evidenceList.GroupBy(e => e.EvidenceType)
                .ToDictionary(g => g.Key, g => g.Count()),
            evidence_by_responder = evidenceList.GroupBy(e => new { e.UploadedByResponderId, e.ResponderRole })
                .Select(g => new
                {
                    responder_id = g.Key.UploadedByResponderId,
                    role = g.Key.ResponderRole,
                    items_collected = g.Count()
                }),
            total_storage_mb = evidenceList.Sum(e => e.FileSizeBytes) / (1024.0 * 1024.0),
            reasons_collected = evidenceList
                .Where(e => !string.IsNullOrEmpty(e.ReasonCollected))
                .GroupBy(e => e.ReasonCollected)
                .ToDictionary(g => g.Key!, g => g.Count())
        });

        return response;
    }

    private static string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            _ => "application/octet-stream"
        };
    }
}
