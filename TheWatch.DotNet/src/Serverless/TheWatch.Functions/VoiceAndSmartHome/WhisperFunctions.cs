using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace TheWatch.Functions;

/// <summary>
/// Serverless Azure Functions handling Whisper Transcription and Phrase Spotting (whisper-local-api.yaml).
/// </summary>
public class WhisperFunctions
{
    private readonly ILogger<WhisperFunctions> _logger;

    public WhisperFunctions(ILogger<WhisperFunctions> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("WhisperHealth")]
    public async Task<HttpResponseData> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "whisper/health")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "healthy",
            model = "whisper-base-en",
            device = "cpu",
            timestamp = DateTime.UtcNow
        });
        return response;
    }

    [Function("WhisperTranscribe")]
    public async Task<HttpResponseData> Transcribe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "whisper/v1/transcriptions")] HttpRequestData req)
    {
        _logger.LogInformation("Processing Whisper audio transcription request");
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            text = "I need help right now.",
            language = "en",
            duration = 2.4,
            segments = new[]
            {
                new
                {
                    id = 0,
                    start = 0.0,
                    end = 2.4,
                    text = "I need help right now."
                }
            }
        });
        return response;
    }

    [Function("WhisperTranslate")]
    public async Task<HttpResponseData> Translate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "whisper/v1/translations")] HttpRequestData req)
    {
        _logger.LogInformation("Processing Whisper translation request");
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            text = "Emergency alert.",
            language = "en",
            duration = 1.8
        });
        return response;
    }

    [Function("WhisperSpotPhrases")]
    public async Task<HttpResponseData> SpotPhrases(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "whisper/v1/phrases/spot")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            matched = true,
            detectedPhrase = "red umbrella",
            confidence = 0.96,
            timestamp = DateTime.UtcNow
        });
        return response;
    }
}
