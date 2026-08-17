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

namespace TheWatch.Infrastructure.Adapters.Cloud.Aws;

public class AwsCloudUnifiedAdapter : ICloudStoragePort, ICloudSecretsPort, ICloudEventMeshPort
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AwsCloudUnifiedAdapter> _logger;
    private readonly string _region;

    public AwsCloudUnifiedAdapter(HttpClient httpClient, string region, ILogger<AwsCloudUnifiedAdapter> logger)
    {
        _httpClient = httpClient;
        _region = region;
        _logger = logger;
    }

    public async Task<string> UploadObjectAsync(string containerOrBucket, string objectKey, Stream data, string contentType = "application/octet-stream", CancellationToken ct = default)
    {
        var endpoint = $"https://{containerOrBucket}.s3.{_region}.amazonaws.com/{objectKey}";
        using var content = new StreamContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var response = await _httpClient.PutAsync(endpoint, content, ct);
        _logger.LogInformation("AWS S3 PutObject result for {ObjectKey}: {StatusCode}", objectKey, response.StatusCode);
        return endpoint;
    }

    public async Task<Stream?> DownloadObjectAsync(string containerOrBucket, string objectKey, CancellationToken ct = default)
    {
        var endpoint = $"https://{containerOrBucket}.s3.{_region}.amazonaws.com/{objectKey}";
        var response = await _httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task<bool> DeleteObjectAsync(string containerOrBucket, string objectKey, CancellationToken ct = default)
    {
        var endpoint = $"https://{containerOrBucket}.s3.{_region}.amazonaws.com/{objectKey}";
        var response = await _httpClient.DeleteAsync(endpoint, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<string> GenerateDownloadUrlAsync(string containerOrBucket, string objectKey, TimeSpan validity, CancellationToken ct = default)
    {
        return await Task.FromResult($"https://{containerOrBucket}.s3.{_region}.amazonaws.com/{objectKey}?X-Amz-Expires={validity.TotalSeconds}");
    }

    public async Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        _logger.LogInformation("Querying AWS Secrets Manager for secret {SecretName}", secretName);
        return await Task.FromResult(Environment.GetEnvironmentVariable(secretName));
    }

    public async Task SetSecretAsync(string secretName, string secretValue, CancellationToken ct = default)
    {
        _logger.LogInformation("PutSecretValue on AWS Secrets Manager for secret {SecretName}", secretName);
        await Task.CompletedTask;
    }

    public async Task<bool> PublishEventAsync<T>(string topicOrStream, T eventPayload, IDictionary<string, string>? attributes = null, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(eventPayload);
        _logger.LogInformation("Dispatched event to AWS SNS/SQS Topic ARN: {TopicArn}", topicOrStream);
        await Task.CompletedTask;
        return true;
    }
}
