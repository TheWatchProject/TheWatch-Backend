using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Service implementation for evidence storage and management.
/// Uses Azure Blob Storage with integrity verification, SAS token generation,
/// and chain of custody tracking for legal proceedings.
/// </summary>
public class EvidenceStorageService : IEvidenceStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IEvidenceRepository _evidenceRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<EvidenceStorageService> _logger;
    private const string ContainerName = "evidence";

    public EvidenceStorageService(
        BlobServiceClient blobServiceClient,
        IEvidenceRepository evidenceRepository,
        IBlobStorageService blobStorageService,
        ILogger<EvidenceStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _evidenceRepository = evidenceRepository;
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<Evidence> UploadAsync(Guid incidentId, Guid uploaderId, Stream fileStream, 
        string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var evidenceId = Guid.NewGuid();
        var blobName = $"{incidentId}/{evidenceId}/{fileName}";

        // Compute SHA-256 hash for integrity
        var hash = await ComputeHashAsync(fileStream, cancellationToken);
        fileStream.Position = 0; // Reset stream position

        // Upload to blob storage
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);
        
        var blobHttpHeaders = new BlobHttpHeaders
        {
            ContentType = contentType
        };

        var metadata = new Dictionary<string, string>
        {
            { "incidentId", incidentId.ToString() },
            { "uploaderId", uploaderId.ToString() },
            { "originalFileName", fileName },
            { "sha256Hash", hash }
        };

        await blobClient.UploadAsync(fileStream, new BlobUploadOptions
        {
            HttpHeaders = blobHttpHeaders,
            Metadata = metadata
        }, cancellationToken);

        _logger.LogInformation("Evidence uploaded: {EvidenceId} for incident {IncidentId}", 
            evidenceId, incidentId);

        // Create evidence record using correct entity properties
        var evidence = new Evidence
        {
            EvidenceId = evidenceId,
            IncidentId = incidentId,
            UploadedByResponderId = uploaderId,
            FileName = fileName,
            EvidenceType = GetEvidenceTypeFromContentType(contentType),
            StorageLocation = blobClient.Uri.ToString(),
            Sha256Hash = hash,
            FileSizeBytes = fileStream.Length,
            UploadTimestamp = DateTime.UtcNow
        };

        return await _evidenceRepository.CreateAsync(evidence, cancellationToken);
    }

    public async Task<Stream> DownloadAsync(Guid evidenceId, Guid accessorId, string reason, 
        CancellationToken cancellationToken = default)
    {
        var evidence = await _evidenceRepository.GetByIdAsync(evidenceId, cancellationToken);
        if (evidence == null)
        {
            throw new InvalidOperationException($"Evidence {evidenceId} not found");
        }

        // Log access for chain of custody
        await _evidenceRepository.LogAccessAsync(evidenceId, accessorId, reason, cancellationToken);

        // Download from blob storage
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobClient = containerClient.GetBlobClient(new Uri(evidence.StorageLocation).AbsolutePath.TrimStart('/'));

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        
        _logger.LogInformation("Evidence downloaded: {EvidenceId} by {AccessorId}", 
            evidenceId, accessorId);

        return response.Value.Content;
    }

    public async Task<bool> VerifyIntegrityAsync(Guid evidenceId, CancellationToken cancellationToken = default)
    {
        var evidence = await _evidenceRepository.GetByIdAsync(evidenceId, cancellationToken);
        if (evidence == null || string.IsNullOrEmpty(evidence.Sha256Hash))
        {
            return false;
        }

        // Download and re-compute hash
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobClient = containerClient.GetBlobClient(new Uri(evidence.StorageLocation).AbsolutePath.TrimStart('/'));

        using var stream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
        var currentHash = await ComputeHashAsync(stream, cancellationToken);

        var isValid = evidence.Sha256Hash.Equals(currentHash, StringComparison.OrdinalIgnoreCase);

        if (!isValid)
        {
            _logger.LogWarning("Evidence integrity check failed: {EvidenceId}. Expected: {Expected}, Got: {Actual}",
                evidenceId, evidence.Sha256Hash, currentHash);
        }

        return isValid;
    }

    public async Task DeleteAsync(Guid evidenceId, Guid deletedById, CancellationToken cancellationToken = default)
    {
        var evidence = await _evidenceRepository.GetByIdAsync(evidenceId, cancellationToken);
        if (evidence == null)
        {
            return;
        }

        if (evidence.LegalHold)
        {
            throw new InvalidOperationException($"Cannot delete evidence {evidenceId} - under legal hold");
        }

        // Delete from blob storage
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobClient = containerClient.GetBlobClient(new Uri(evidence.StorageLocation).AbsolutePath.TrimStart('/'));
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        // Mark as deleted in repository
        await _evidenceRepository.MarkAsDeletedAsync(evidenceId, deletedById, cancellationToken);

        _logger.LogInformation("Evidence deleted: {EvidenceId} by {DeletedById}",
            evidenceId, deletedById);
    }

    /// <summary>
    /// Generates a time-limited SAS URL for secure evidence access.
    /// </summary>
    public async Task<string> GenerateSasUrlAsync(Guid evidenceId, Guid accessorId,
        TimeSpan? expiresIn = null, CancellationToken cancellationToken = default)
    {
        var evidence = await _evidenceRepository.GetByIdAsync(evidenceId, cancellationToken);
        if (evidence == null)
        {
            throw new InvalidOperationException($"Evidence {evidenceId} not found");
        }

        // Log access for chain of custody
        await _evidenceRepository.LogAccessAsync(evidenceId, accessorId, "sas_url_generated", cancellationToken);

        // Generate SAS URL using BlobStorageService
        var sasUrl = await _blobStorageService.GenerateSasUrlAsync(
            evidence.StorageLocation,
            expiresIn ?? TimeSpan.FromHours(1),
            "r",
            cancellationToken);

        _logger.LogInformation("Generated SAS URL for evidence: {EvidenceId} for accessor: {AccessorId}",
            evidenceId, accessorId);

        return sasUrl;
    }

    /// <summary>
    /// Transfers evidence to law enforcement by creating a secure copy with extended SAS token.
    /// </summary>
    public async Task<string> TransferToLawEnforcementAsync(Guid evidenceId, Guid transferredById,
        string receivingEntity, CancellationToken cancellationToken = default)
    {
        var evidence = await _evidenceRepository.GetByIdAsync(evidenceId, cancellationToken);
        if (evidence == null)
        {
            throw new InvalidOperationException($"Evidence {evidenceId} not found");
        }

        // Create a transfer record in chain of custody
        await _evidenceRepository.LogChainOfCustodyEventAsync(
            evidenceId,
            transferredById,
            "transfer",
            $"Evidence transferred to {receivingEntity}",
            cancellationToken);

        // Generate long-lived SAS URL for law enforcement access (7 days)
        var transferUrl = await GenerateSasUrlAsync(evidenceId, transferredById, TimeSpan.FromDays(7), cancellationToken);

        _logger.LogInformation("Evidence transferred to law enforcement: {EvidenceId} to {ReceivingEntity}",
            evidenceId, receivingEntity);

        return transferUrl;
    }

    private static string GetEvidenceTypeFromContentType(string contentType)
    {
        if (contentType.StartsWith("image/")) return "photo";
        if (contentType.StartsWith("video/")) return "video";
        if (contentType.StartsWith("audio/")) return "audio";
        return "other";
    }

    private static async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

/// <summary>
/// Interface for evidence storage operations.
/// </summary>
public interface IEvidenceStorageService
{
    Task<Evidence> UploadAsync(Guid incidentId, Guid uploaderId, Stream fileStream,
        string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(Guid evidenceId, Guid accessorId, string reason,
        CancellationToken cancellationToken = default);
    Task<bool> VerifyIntegrityAsync(Guid evidenceId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid evidenceId, Guid deletedById, CancellationToken cancellationToken = default);
    Task<string> GenerateSasUrlAsync(Guid evidenceId, Guid accessorId, TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default);
    Task<string> TransferToLawEnforcementAsync(Guid evidenceId, Guid transferredById, string receivingEntity,
        CancellationToken cancellationToken = default);
}
