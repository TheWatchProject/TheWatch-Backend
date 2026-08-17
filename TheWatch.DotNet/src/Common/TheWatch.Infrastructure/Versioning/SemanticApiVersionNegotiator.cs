using System;
using System.Collections.Generic;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Versioning;

/// <summary>
/// Semantic version negotiator and breaking-change detector for microservices, mobile apps, and contract interfaces.
/// </summary>
public sealed class SemanticApiVersionNegotiator
{
    public ContractCompatibilityReport EvaluateCompatibility(
        string serviceName,
        ApiVersionDescriptor clientVersion,
        ApiVersionDescriptor serverVersion)
    {
        var breakingChanges = new List<string>();

        // Major version mismatch indicates breaking changes
        if (clientVersion.Major != serverVersion.Major)
        {
            breakingChanges.Add($"Major version mismatch: Client is on v{clientVersion.Major}.{clientVersion.Minor}, Server is on v{serverVersion.Major}.{serverVersion.Minor}");
        }

        // Client asking for newer minor version than server supports
        if (clientVersion.Major == serverVersion.Major && clientVersion.Minor > serverVersion.Minor)
        {
            breakingChanges.Add($"Client requires features from v{clientVersion.Major}.{clientVersion.Minor}, but server only implements v{serverVersion.Major}.{serverVersion.Minor}");
        }

        if (serverVersion.IsDeprecated)
        {
            breakingChanges.Add($"Server version v{serverVersion.Major}.{serverVersion.Minor} is deprecated and slated for sunset on {serverVersion.SunsetDateUtc:yyyy-MM-dd}.");
        }

        bool isCompatible = breakingChanges.Count == 0;

        return new ContractCompatibilityReport(
            ServiceName: serviceName,
            ClientVersion: clientVersion,
            ServerVersion: serverVersion,
            IsCompatible: isCompatible,
            BreakingChanges: breakingChanges
        );
    }
}
