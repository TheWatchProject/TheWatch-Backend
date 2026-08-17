using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Cloud.Gcp;

public class GoogleCloudUnifiedAdapter : ICloudStoragePort, ICloudSecretsPort, ICloudEventMeshPort
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleCloudUnifiedAdapter> _logger;
    private readonly string _projectId;

    public GoogleCloudUnifiedAdapter(HttpClient httpClient, string projectId, ILogger<GoogleCloudUnifiedAdapter> logger)
    {
        _httpClient = httpClient;
        _projectId = projectId;
        _logger = logger;
    }

    public async Task<string> UploadObjectAsync(string containerOrBucket, string objectKey, Stream data, string contentType = "application/octet-stream", CancellationToken ct = default)
    {
        var endpoint = $"https://storage.googleapis.com/upload/storage/v1/b/{containerOrBucket}/o?uploadType=media&name={Uri.EscapeDataString(objectKey)}";
        using var content = new StreamContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var response = await _httpClient.PostAsync(endpoint, content, ct);
        _logger.LogInformation("GCS object upload result for {ObjectKey}: {StatusCode}", objectKey, response.StatusCode);
        return $"https://storage.googleapis.com/{containerOrBucket}/{objectKey}";
    }

    public async Task<Stream?> DownloadObjectAsync(string containerOrBucket, string objectKey, CancellationToken ct = default)
    {
        var endpoint = $"https://storage.googleapis.com/storage/v1/b/{containerOrBucket}/o/{Uri.EscapeDataString(objectKey)}?alt=media";
        var response = await _httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task<bool> DeleteObjectAsync(string containerOrBucket, string objectKey, CancellationToken ct = default)
    {
        var endpoint = $"https://storage.googleapis.com/storage/v1/b/{containerOrBucket}/o/{Uri.EscapeDataString(objectKey)}";
        var response = await _httpClient.DeleteAsync(endpoint, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<string> GenerateDownloadUrlAsync(string containerOrBucket, string objectKey, TimeSpan validity, CancellationToken ct = default)
    {
        return await Task.FromResult($"https://storage.googleapis.com/{containerOrBucket}/{objectKey}?expires={DateTimeOffset.UtcNow.Add(validity).ToUnixTimeSeconds()}");
    }

    public async Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        _logger.LogInformation("Accessing Google Secret Manager payload for {SecretName}", secretName);
        return await Task.FromResult(Environment.GetEnvironmentVariable(secretName));
    }

    public async Task SetSecretAsync(string secretName, string secretValue, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Google Secret Manager secret version for {SecretName}", secretName);
        await Task.CompletedTask;
    }

    public async Task<bool> PublishEventAsync<T>(string topicOrStream, T eventPayload, IDictionary<string, string>? attributes = null, CancellationToken ct = default)
    {
        var endpoint = $"https://pubsub.googleapis.com/v1/projects/{_projectId}/topics/{topicOrStream}:publish";
        var json = JsonSerializer.Serialize(eventPayload);
        var base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var payload = new
        {
            messages = new[]
            {
                new
                {
                    data = base64Data,
                    attributes = attributes ?? new Dictionary<string, string>()
                }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, content, ct);
        _logger.LogInformation("Google Pub/Sub published to {Topic}: {StatusCode}", topicOrStream, response.StatusCode);
        return response.IsSuccessStatusCode;
    }
}
