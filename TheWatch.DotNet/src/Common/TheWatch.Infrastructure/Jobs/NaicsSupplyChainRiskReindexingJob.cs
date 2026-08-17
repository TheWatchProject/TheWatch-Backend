using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TheWatch.Infrastructure.Jobs;

public sealed record IndustryRiskScore(string NaicsCode, string IndustryTitle, double CriticalityIndex, double HazardVulnerabilityIndex, string PriorityTier);

/// <summary>
/// Background job that recalculates supply chain dependencies and criticality risk indices for NAICS/NAPCS emergency infrastructure.
/// </summary>
public sealed class NaicsSupplyChainRiskReindexingJob
{
    public Task<List<IndustryRiskScore>> ReindexIndustryRisksAsync(
        IEnumerable<(string Code, string Title, double BaselineCrit, double ThreatFactor)> industries,
        CancellationToken cancellationToken = default)
    {
        var results = new List<IndustryRiskScore>();

        foreach (var (code, title, baseCrit, threat) in industries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            double vulnHazard = Math.Clamp(baseCrit * threat * 1.15, 0.0, 1.0);
            string tier = vulnHazard >= 0.8 ? "TIER_1_CRITICAL_INFRASTRUCTURE" :
                          vulnHazard >= 0.5 ? "TIER_2_ESSENTIAL_SERVICES" : "TIER_3_COMMERCIAL";

            results.Add(new IndustryRiskScore(
                NaicsCode: code,
                IndustryTitle: title,
                CriticalityIndex: baseCrit,
                HazardVulnerabilityIndex: Math.Round(vulnHazard, 4),
                PriorityTier: tier
            ));
        }

        return Task.FromResult(results);
    }
}
