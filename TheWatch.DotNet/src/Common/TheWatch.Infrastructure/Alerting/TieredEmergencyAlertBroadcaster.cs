using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Alerting;

public sealed record AlertBroadcastReceipt(string AlertId, AlertJurisdictionLevel Jurisdiction, int TargetCiviliansNotified, string SignedProof, DateTime BroadcastAtUtc);

/// <summary>
/// Tiered Emergency Alert System (EAS / WEA / CAP) broadcaster handling Local, County, State, and National public safety alerts.
/// </summary>
public sealed class TieredEmergencyAlertBroadcaster
{
    public Task<AlertBroadcastReceipt> BroadcastCapAlertAsync(CommonAlertingProtocolMessage alert)
    {
        if (string.IsNullOrWhiteSpace(alert.Headline))
        {
            throw new ArgumentException("Alert headline cannot be empty.", nameof(alert));
        }

        // Calculate synthetic delivery reach based on jurisdiction tier
        int estimatedReach = alert.Jurisdiction switch
        {
            AlertJurisdictionLevel.LocalMunicipal => 15_000,
            AlertJurisdictionLevel.CountyRegional => 125_000,
            AlertJurisdictionLevel.StateProvincial => 2_500_000,
            AlertJurisdictionLevel.NationalFederal => 75_000_000,
            _ => 5_000
        };

        string proof = ComputeSha256($"{alert.Identifier}:{alert.SenderAgency}:{alert.SentUtc:O}:{alert.DigitalSignature}");

        var receipt = new AlertBroadcastReceipt(
            AlertId: alert.Identifier,
            Jurisdiction: alert.Jurisdiction,
            TargetCiviliansNotified: estimatedReach,
            SignedProof: proof,
            BroadcastAtUtc: DateTime.UtcNow
        );

        return Task.FromResult(receipt);
    }

    private static string ComputeSha256(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
