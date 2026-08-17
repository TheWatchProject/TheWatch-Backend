using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TheWatch.Domain.Models.Mobile;

namespace TheWatch.MobileBff.Swarm;

public sealed class SwarmOptions
{
    public const string SectionName = "Swarm";
    public string Provider { get; set; } = "Simulation";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int MaxConcurrentDomains { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed record SwarmDomainDefinition(string DomainId, string Domain, string AgentId, string CodeName);

public interface ISwarmExecutionProvider
{
    string Name { get; }
    Task<string> ExecuteAsync(SwarmDomainDefinition domain, string objective, CancellationToken cancellationToken);
}

public sealed class SimulationSwarmExecutionProvider : ISwarmExecutionProvider
{
    public string Name => "Simulation";

    public async Task<string> ExecuteAsync(SwarmDomainDefinition domain, string objective, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        return $"Simulation result for {domain.Domain}: objective accepted for analysis.";
    }
}

public sealed class AzureOpenAiSwarmExecutionProvider : ISwarmExecutionProvider
{
    private readonly HttpClient _httpClient;
    private readonly SwarmOptions _options;

    public AzureOpenAiSwarmExecutionProvider(HttpClient httpClient, IOptions<SwarmOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string Name => "AzureOpenAI";

    public async Task<string> ExecuteAsync(SwarmDomainDefinition domain, string objective, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Azure OpenAI provider is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.Endpoint.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(_options.Model)}/chat/completions?api-version=2024-10-21")
        {
            Content = JsonContent.Create(new
            {
                messages = new[]
                {
                    new { role = "system", content = $"You are the {domain.Domain} swarm specialist ({domain.CodeName}). Return concise evidence and recommendations." },
                    new { role = "user", content = objective }
                },
                temperature = 0.1
            })
        };
        request.Headers.Add("api-key", _options.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure OpenAI returned {(int)response.StatusCode}.");

        var payload = System.Text.Json.JsonSerializer.Deserialize<ChatCompletionResponse>(body);
        return payload?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("Azure OpenAI returned no completion.");
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public Message? Message { get; set; }
    }

    private sealed class Message
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}

public interface ISwarmOrchestrator
{
    IReadOnlyList<SwarmDomainDefinition> Domains { get; }
    Task<SwarmTaskView> SubmitAsync(SwarmTaskSubmission submission, string submittedBy, CancellationToken cancellationToken);
    bool TryGet(string taskId, out SwarmTaskView? task);
}

public sealed class SwarmOrchestrator : ISwarmOrchestrator
{
    private readonly ISwarmExecutionProvider _provider;
    private readonly SwarmOptions _options;
    private readonly ILogger<SwarmOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, SwarmTaskView> _tasks = new();
    private readonly ConcurrentDictionary<string, string> _idempotency = new();

    private static readonly IReadOnlyList<SwarmDomainDefinition> DomainCatalog =
        Enumerable.Range(1, 15).Select(i => new SwarmDomainDefinition(
            $"{i:00}-domain", $"Domain {i:00}", $"agent-{i:000}", $"swarm-agent-{i:00}")).ToArray();

    public SwarmOrchestrator(ISwarmExecutionProvider provider, IOptions<SwarmOptions> options, ILogger<SwarmOrchestrator> logger)
    {
        _provider = provider;
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<SwarmDomainDefinition> Domains => DomainCatalog;

    public async Task<SwarmTaskView> SubmitAsync(SwarmTaskSubmission submission, string submittedBy, CancellationToken cancellationToken)
    {
        Validate(submission);
        if (!string.IsNullOrWhiteSpace(submission.IdempotencyKey) &&
            _idempotency.TryGetValue(submission.IdempotencyKey, out var existingId) &&
            _tasks.TryGetValue(existingId, out var existing))
            return existing;

        var selected = submission.DomainIds is { Count: > 0 }
            ? DomainCatalog.Where(d => submission.DomainIds.Contains(d.DomainId, StringComparer.OrdinalIgnoreCase)).ToArray()
            : DomainCatalog.ToArray();
        if (selected.Length == 0) throw new ArgumentException("No valid domain IDs were supplied.", nameof(submission));

        var taskId = Guid.NewGuid().ToString("N");
        var submittedAt = DateTimeOffset.UtcNow;
        var results = new ConcurrentBag<SwarmDomainResult>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 300)));

        try
        {
            await Parallel.ForEachAsync(selected, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(_options.MaxConcurrentDomains, 1, selected.Length),
                CancellationToken = timeout.Token
            }, async (domain, token) =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var output = await _provider.ExecuteAsync(domain, submission.Objective.Trim(), token);
                    results.Add(new SwarmDomainResult(domain.DomainId, domain.Domain, domain.AgentId, "COMPLETED", output, null, stopwatch.Elapsed.TotalMilliseconds));
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    results.Add(new SwarmDomainResult(domain.DomainId, domain.Domain, domain.AgentId, "TIMED_OUT", null, "Execution timed out.", stopwatch.Elapsed.TotalMilliseconds));
                }
                catch (Exception)
                {
                    results.Add(new SwarmDomainResult(domain.DomainId, domain.Domain, domain.AgentId, "FAILED", null, "Provider execution failed.", stopwatch.Elapsed.TotalMilliseconds));
                }
            });
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            foreach (var domain in selected.Where(d => results.All(r => r.DomainId != d.DomainId)))
                results.Add(new SwarmDomainResult(domain.DomainId, domain.Domain, domain.AgentId, "TIMED_OUT", null, "Execution timed out.", 0));
        }

        var orderedResults = results.OrderBy(r => r.DomainId).ToArray();
        var completed = orderedResults.Count(r => r.Status == "COMPLETED");
        var view = new SwarmTaskView(taskId, submission.Objective.Trim(),
            completed == orderedResults.Length ? "COMPLETED" : completed == 0 ? "FAILED" : "PARTIAL",
            _provider.Name, submittedBy, submittedAt, DateTimeOffset.UtcNow,
            (double)completed / orderedResults.Length, orderedResults);
        _tasks[taskId] = view;
        if (!string.IsNullOrWhiteSpace(submission.IdempotencyKey)) _idempotency[submission.IdempotencyKey] = taskId;
        _logger.LogInformation(
            "Swarm task {TaskId} completed. Provider={Provider}, SubmittedBy={SubmittedBy}, Domains={DomainCount}, Status={Status}, Consensus={ConsensusScore}",
            taskId, _provider.Name, submittedBy, orderedResults.Length, view.Status, view.ConsensusScore);
        return view;
    }

    public bool TryGet(string taskId, out SwarmTaskView? task) => _tasks.TryGetValue(taskId, out task);

    private void Validate(SwarmTaskSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Objective) || submission.Objective.Length > 4000)
            throw new ArgumentException("Objective is required and must be at most 4000 characters.", nameof(submission));
        if (submission.DomainIds?.Count > DomainCatalog.Count)
            throw new ArgumentException("Too many domain IDs were supplied.", nameof(submission));
    }
}
