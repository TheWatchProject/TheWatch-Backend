using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Azure Blob Storage implementation for file storage operations.
/// Handles evidence, summoner photos, and video stream storage.
/// </summary>
public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(BlobServiceClient blobServiceClient, ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    public async Task<string> UploadBlobAsync(
        string containerName,
        string blobName,
        Stream content,
        string contentType,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            },
            Metadata = metadata
        };

        await blobClient.UploadAsync(content, options, cancellationToken);

        _logger.LogInformation("Uploaded blob {BlobName} to container {ContainerName}", blobName, containerName);

        return blobClient.Uri.ToString();
    }

    public async Task<Stream> DownloadBlobAsync(string storageLocation, CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(storageLocation);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);

        _logger.LogDebug("Downloaded blob from {StorageLocation}", storageLocation);

        return response.Value.Content;
    }

    public async Task DeleteBlobAsync(string storageLocation, CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(storageLocation);

        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        _logger.LogInformation("Deleted blob at {StorageLocation}", storageLocation);
    }

    public async Task<bool> BlobExistsAsync(string storageLocation, CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(storageLocation);
        return await blobClient.ExistsAsync(cancellationToken);
    }

    public async Task<BlobMetadata> GetBlobMetadataAsync(string storageLocation, CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(storageLocation);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

        return new BlobMetadata
        {
            SizeBytes = properties.Value.ContentLength,
            ContentType = properties.Value.ContentType,
            LastModified = properties.Value.LastModified.UtcDateTime,
            Metadata = properties.Value.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            ETag = properties.Value.ETag.ToString()
        };
    }

    public async Task<long> GetBlobSizeAsync(string storageLocation, CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(storageLocation);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

        return properties.Value.ContentLength;
    }

    public async Task<string> GenerateSasUrlAsync(
        string storageLocation,
        TimeSpan? expiresIn = null,
        string permissions = "r",
        CancellationToken cancellationToken = default)
    {
        var blobClient = GetBlobClient(storageLocation);

        // Default to 1 hour expiration
        var expiresOn = DateTimeOffset.UtcNow.Add(expiresIn ?? TimeSpan.FromHours(1));

        // Convert permissions string to BlobSasPermissions
        var sasPermissions = new BlobSasPermissions();
        if (permissions.Contains('r')) sasPermissions |= BlobSasPermissions.Read;
        if (permissions.Contains('w')) sasPermissions |= BlobSasPermissions.Write;
        if (permissions.Contains('d')) sasPermissions |= BlobSasPermissions.Delete;

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5), // Allow 5 min clock skew
            ExpiresOn = expiresOn
        };
        sasBuilder.SetPermissions(sasPermissions);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);

        _logger.LogDebug("Generated SAS URL for {StorageLocation} expiring at {ExpiresOn}", storageLocation, expiresOn);

        return sasUri.ToString();
    }

    public async Task<string> CopyBlobAsync(
        string sourceLocation,
        string destinationContainer,
        string destinationBlobName,
        CancellationToken cancellationToken = default)
    {
        var sourceBlobClient = GetBlobClient(sourceLocation);
        var destinationContainerClient = _blobServiceClient.GetBlobContainerClient(destinationContainer);
        await destinationContainerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var destinationBlobClient = destinationContainerClient.GetBlobClient(destinationBlobName);

        var copyOperation = await destinationBlobClient.StartCopyFromUriAsync(sourceBlobClient.Uri, cancellationToken: cancellationToken);

        // Wait for copy to complete
        await copyOperation.WaitForCompletionAsync(cancellationToken);

        _logger.LogInformation("Copied blob from {SourceLocation} to {DestinationContainer}/{DestinationBlobName}",
            sourceLocation, destinationContainer, destinationBlobName);

        return destinationBlobClient.Uri.ToString();
    }

    private BlobClient GetBlobClient(string storageLocation)
    {
        // If storageLocation is a full URI, parse it
        if (Uri.TryCreate(storageLocation, UriKind.Absolute, out var uri))
        {
            return new BlobClient(uri);
        }

        // Otherwise, assume it's a container/blob path
        var parts = storageLocation.Split('/', 2);
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid storage location format: {storageLocation}. Expected 'container/blobname' or full URI.");
        }

        var containerClient = _blobServiceClient.GetBlobContainerClient(parts[0]);
        return containerClient.GetBlobClient(parts[1]);
    }
}
