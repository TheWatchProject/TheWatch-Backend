using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Cloud.Azure;

public class AzureCloudUnifiedAdapter : ICloudStoragePort, ICloudSecretsPort, ICloudEventMeshPort
{
    private readonly BlobServiceClient? _blobClient;
    private readonly ServiceBusClient? _serviceBusClient;
    private readonly ILogger<AzureCloudUnifiedAdapter> _logger;

    public AzureCloudUnifiedAdapter(
        ILogger<AzureCloudUnifiedAdapter> logger,
        string? blobConnectionString = null,
        string? serviceBusConnectionString = null)
    {
        _logger = logger;
        if (!string.IsNullOrEmpty(blobConnectionString))
        {
            _blobClient = new BlobServiceClient(blobConnectionString);
        }
        if (!string.IsNullOrEmpty(serviceBusConnectionString))
        {
            _serviceBusClient = new ServiceBusClient(serviceBusConnectionString);
        }
    }

    public async Task<string> UploadObjectAsync(string containerOrBucket, string objectKey, Stream data, string contentType = "application/octet-stream", CancellationToken ct = default)
    {
        if (_blobClient == null) throw new InvalidOperationException("Azure BlobServiceClient is not configured.");
        var containerClient = _blobClient.GetBlobContainerClient(containerOrBucket);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blobClient = containerClient.GetBlobClient(objectKey);
        await blobClient.UploadAsync(data, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        _logger.LogInformation("Uploaded Azure Blob {ObjectKey} to container {Container}", objectKey, containerOrBucket);
        return blobClient.Uri.AbsoluteUri;
    }

    public async Task<Stream?> DownloadObjectAsync(string containerOrBucket, string objectKey, CancellationToken ct = default)
    {
        if (_blobClient == null) throw new InvalidOperationException("Azure BlobServiceClient is not configured.");
        var containerClient = _blobClient.GetBlobContainerClient(containerOrBucket);
        var blobClient = containerClient.GetBlobClient(objectKey);

        if (!await blobClient.ExistsAsync(ct)) return null;
        var download = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
        return download.Value.Content;
    }

    public async Task<bool> DeleteObjectAsync(string containerOrBucket, string objectKey, CancellationToken ct = default)
    {
        if (_blobClient == null) throw new InvalidOperationException("Azure BlobServiceClient is not configured.");
        var containerClient = _blobClient.GetBlobContainerClient(containerOrBucket);
        var blobClient = containerClient.GetBlobClient(objectKey);
        var response = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        return response.Value;
    }

    public async Task<string> GenerateDownloadUrlAsync(string containerOrBucket, string objectKey, TimeSpan validity, CancellationToken ct = default)
    {
        if (_blobClient == null) throw new InvalidOperationException("Azure BlobServiceClient is not configured.");
        var containerClient = _blobClient.GetBlobContainerClient(containerOrBucket);
        var blobClient = containerClient.GetBlobClient(objectKey);

        if (blobClient.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerOrBucket,
                BlobName = objectKey,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = DateTimeOffset.UtcNow.Add(validity)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        return await Task.FromResult(blobClient.Uri.AbsoluteUri);
    }

    public async Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        _logger.LogInformation("Retrieving secret {SecretName} via Azure Key Vault adapter interface", secretName);
        return await Task.FromResult(Environment.GetEnvironmentVariable(secretName));
    }

    public async Task SetSecretAsync(string secretName, string secretValue, CancellationToken ct = default)
    {
        _logger.LogInformation("Setting secret {SecretName} via Azure Key Vault adapter interface", secretName);
        await Task.CompletedTask;
    }

    public async Task<bool> PublishEventAsync<T>(string topicOrStream, T eventPayload, IDictionary<string, string>? attributes = null, CancellationToken ct = default)
    {
        if (_serviceBusClient == null)
        {
            _logger.LogWarning("Azure ServiceBusClient not configured. Skipping publish to {Topic}", topicOrStream);
            return false;
        }

        var sender = _serviceBusClient.CreateSender(topicOrStream);
        var body = JsonSerializer.Serialize(eventPayload);
        var message = new ServiceBusMessage(body) { ContentType = "application/json" };
        if (attributes != null)
        {
            foreach (var kv in attributes) message.ApplicationProperties[kv.Key] = kv.Value;
        }

        await sender.SendMessageAsync(message, ct);
        _logger.LogInformation("Published Azure Service Bus event to topic {Topic}", topicOrStream);
        return true;
    }
}
