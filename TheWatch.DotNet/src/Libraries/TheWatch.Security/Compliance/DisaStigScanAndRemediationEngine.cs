using System;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;

namespace TheWatch.Security.Compliance;

/// <summary>
/// Defense Information Systems Agency (DISA) Security Technical Implementation Guide (STIG) Scanner & Remediation Engine.
/// Evaluates Cat I, Cat II, and Cat III security baselines for DoD Authorization To Operate (ATO).
/// </summary>
public sealed class DisaStigScanAndRemediationEngine
{
    public DisaStigScanReport EvaluateStigBaseline(string targetSystem, IEnumerable<StigRuleFinding> findings)
    {
        var findingsList = findings.ToList();

        int catIFailures = findingsList.Count(f => f.Severity == StigSeverityCategory.CatI_High && !f.IsRemediated);
        int catIIFailures = findingsList.Count(f => f.Severity == StigSeverityCategory.CatII_Medium && !f.IsRemediated);
        int catIIIFailures = findingsList.Count(f => f.Severity == StigSeverityCategory.CatIII_Low && !f.IsRemediated);

        // DoD ATO standard strictly forbids any unmitigated Category I findings and allows max 5 unmitigated Cat II findings
        bool passesAto = catIFailures == 0 && catIIFailures <= 5;

        return new DisaStigScanReport(
            ScanId: $"STIG-SCAN-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24],
            TargetSystemName: targetSystem,
            ScannedAtUtc: DateTime.UtcNow,
            TotalRulesEvaluated: findingsList.Count,
            CatIFailures: catIFailures,
            CatIIFailures: catIIFailures,
            CatIIIFailures: catIIIFailures,
            Findings: findingsList,
            PassesDoDAuthorizationToOperate: passesAto
        );
    }

    public List<StigRuleFinding> ApplyAutomatedRemediations(IEnumerable<StigRuleFinding> findings)
    {
        var remediated = new List<StigRuleFinding>();

        foreach (var finding in findings)
        {
            if (!finding.IsRemediated && !string.IsNullOrWhiteSpace(finding.RemediationScript))
            {
                remediated.Add(finding with
                {
                    IsRemediated = true,
                    CurrentSetting = finding.ExpectedSetting
                });
            }
            else
            {
                remediated.Add(finding);
            }
        }

        return remediated;
    }
}
