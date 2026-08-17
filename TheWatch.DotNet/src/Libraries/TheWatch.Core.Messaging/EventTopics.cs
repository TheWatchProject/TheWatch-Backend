namespace TheWatch.Core.Messaging;

/// <summary>
/// Canonical topic names for Dapr pub/sub, Azure Service Bus, and RabbitMQ message broker exchange routing.
/// </summary>
public static class EventTopics
{
    public const string IncidentEvents = "thewatch.incidents";
    public const string DispatchEvents = "thewatch.dispatch";
    public const string TelemetryEvents = "thewatch.telemetry";
    public const string AlertEvents = "thewatch.alerts";
    public const string MeshRelayEvents = "thewatch.mesh";
    public const string BiometricAlertEvents = "thewatch.biometrics";
    public const string AiTriageEvents = "thewatch.ai.triage";
}
