using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Cloud.R2;

public class CloudflareR2StorageAdapter : ICloudStoragePort
{
    private readonly HttpClient _httpClient;
    private readonly string _accountId;
    private readonly ILogger<CloudflareR2StorageAdapter> _logger;

    public CloudflareR2StorageAdapter(HttpClient httpClient, string accountId, ILogger<CloudflareR2StorageAdapter> logger)
    {
        _httpClient = httpClient;
        _accountId = accountId;
        _logger = logger;
    }

    public async Task<string> UploadObjectAsync(string containerOrBucket, string objectKey, Stream data, string contentType = "application/octet-stream", CancellationToken ct = default)
    {
        var endpoint = $"https://{_accountId}.r2.cloudflarestorage.com/{containerOrBucket}/{objectKey}";
        using var content = new StreamContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var response = await _httpClient.PutAsync(endpoint, content, ct);
        _logger.LogInformation("Uploaded video evidence to Cloudflare R2: {Key} (Status: {Status})", objectKey, response.StatusCode);
        return endpoint;
    }

    public async Task<Stream?> DownloadObjectAsync(string containerOrBucket, string objectKey, CancellationToken ct = default)
    {
        var endpoint = $"https://{_accountId}.r2.cloudflarestorage.com/{containerOrBucket}/{objectKey}";
        var response = await _httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task<bool> DeleteObjectAsync(string containerOrBucket, string objectKey, CancellationToken ct = default)
    {
        var endpoint = $"https://{_accountId}.r2.cloudflarestorage.com/{containerOrBucket}/{objectKey}";
        var response = await _httpClient.DeleteAsync(endpoint, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<string> GenerateDownloadUrlAsync(string containerOrBucket, string objectKey, TimeSpan validity, CancellationToken ct = default)
    {
        return await Task.FromResult($"https://pub-{_accountId}.r2.dev/{containerOrBucket}/{objectKey}");
    }
}
