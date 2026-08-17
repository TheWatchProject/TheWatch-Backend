using System;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;

namespace TheWatch.Security.Compliance;

/// <summary>
/// NIST SP 800-53 Rev 5 / FedRAMP High compliance verification engine.
/// Evaluates Access Control (AC), Audit (AU), Identification (IA), Cryptography (SC), and System Integrity (SI).
/// </summary>
public sealed class Nist80053ComplianceEngine
{
    public NistComplianceReport AssessSystemBoundary(string systemBoundary, IEnumerable<NistControlEvaluation> evaluations)
    {
        var evalList = evaluations.ToList();
        if (evalList.Count == 0)
        {
            return new NistComplianceReport(
                AssessmentId: $"NIST-{Guid.NewGuid():N}"[..18],
                SystemBoundary: systemBoundary,
                Evaluations: new List<NistControlEvaluation>(),
                ComplianceScorePercent: 0.0,
                MeetsFedRampHighBaseline: false
            );
        }

        int compliantCount = evalList.Count(e => e.Status == ComplianceStatus.Compliant);
        int partialCount = evalList.Count(e => e.Status == ComplianceStatus.PartiallyCompliant);
        double score = ((compliantCount * 1.0) + (partialCount * 0.5)) / evalList.Count * 100.0;

        // FedRAMP High baseline requires >= 95% compliance score and zero unmitigated AC/IA/SC failures
        bool mandatoryFamiliesPassing = evalList
            .Where(e => e.Family == NistControlFamily.AccessControl_AC ||
                        e.Family == NistControlFamily.IdentificationAndAuthentication_IA ||
                        e.Family == NistControlFamily.SystemAndCommunicationsProtection_SC)
            .All(e => e.Status == ComplianceStatus.Compliant);

        bool meetsFedRampHigh = score >= 95.0 && mandatoryFamiliesPassing;

        return new NistComplianceReport(
            AssessmentId: $"NIST-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..22],
            SystemBoundary: systemBoundary,
            Evaluations: evalList,
            ComplianceScorePercent: Math.Round(score, 2),
            MeetsFedRampHighBaseline: meetsFedRampHigh
        );
    }
}
