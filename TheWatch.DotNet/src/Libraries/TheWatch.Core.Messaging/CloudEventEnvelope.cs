using System;
using System.Text.Json.Serialization;

namespace TheWatch.Core.Messaging;

/// <summary>
/// Standard CloudEvents 1.0 specification compliant message envelope.
/// </summary>
/// <typeparam name="TData">Strongly typed event payload type.</typeparam>
public class CloudEventEnvelope<TData>
{
    /// <summary>
    /// Gets or sets the CloudEvents specification version (must be "1.0").
    /// </summary>
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the unique event identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the URI identifying the source of the event.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "urn:thewatch:platform";

    /// <summary>
    /// Gets or sets the event type identifier.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC timestamp of event creation.
    /// </summary>
    [JsonPropertyName("time")]
    public DateTime Time { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the strongly typed payload data.
    /// </summary>
    [JsonPropertyName("data")]
    public TData? Data { get; set; }

    /// <summary>
    /// Gets or sets the MIME content type of data.
    /// </summary>
    [JsonPropertyName("datacontenttype")]
    public string DataContentType { get; set; } = "application/json";

    /// <summary>
    /// Gets or sets the distributed tracing correlation identifier.
    /// </summary>
    [JsonPropertyName("correlationid")]
    public string? CorrelationId { get; set; }
}
