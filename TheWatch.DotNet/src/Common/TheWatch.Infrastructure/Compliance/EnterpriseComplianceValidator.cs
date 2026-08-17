// Copyright (c) TheWatch. Licensed under MIT.
//
// Evidence-based compliance evaluator. Each ISO 27001 Annex A control is checked
// against a measurable runtime signal rather than hard-coded to `true`. The
// previous version of this class returned Compliant for every control; that
// behaviour has been replaced with a real check so the report is auditable.

using System.Collections.Generic;
using Microsoft.Extensions.Options;
using TheWatch.Infrastructure.Security;

namespace TheWatch.Infrastructure.Compliance;

public sealed class EnterpriseComplianceValidator
{
    private readonly IOptions<TheWatchAuthOptions> _authOptions;

    public EnterpriseComplianceValidator(IOptions<TheWatchAuthOptions> authOptions)
    {
        _authOptions = authOptions;
    }

    /// <summary>
    /// Evaluates runtime security posture against ISO 27001 Annex A controls.
    /// Returns a per-control dictionary and an overall status derived from the
    /// number of controls that failed.
    /// </summary>
    public ComplianceReport EvaluateIso27001Posture()
    {
        var auth = _authOptions.Value;
        var (issuerOk, audienceOk, keyOk) = (
            !string.IsNullOrWhiteSpace(auth.Issuer),
            !string.IsNullOrWhiteSpace(auth.Audience),
            !string.IsNullOrWhiteSpace(auth.SigningKey) || !string.IsNullOrWhiteSpace(auth.MetadataAddress));

        var controls = new Dictionary<string, bool>
        {
            // A.5.15 - Access Control: a real JWT issuer + audience is configured.
            ["A.5.15 - Access Control (JWT issuer and audience are configured)"] =
                issuerOk && audienceOk,

            // A.8.16 - Monitoring: not yet wired here. The current implementation
            // only checks auth configuration; observability signals are reported
            // by the host's OpenTelemetry pipeline, which is set up in
            // TheWatch.ServiceDefaults. The control is intentionally false until
            // the report is fed by an actual telemetry check.
            ["A.8.16 - Monitoring Activities (OpenTelemetry pipeline reachable)"] = false,

            // A.8.20 - Network Security: only checkable at infrastructure scope.
            // Marked false until the Istio PeerAuthentication policy is verified
            // by a network probe rather than a code constant.
            ["A.8.20 - Network Security (Istio STRICT mTLS)"] = false,

            // A.8.24 - Use of Cryptography: at least one signing key or JWKS
            // endpoint is configured.
            ["A.8.24 - Use of Cryptography (JWT signing key or JWKS configured)"] = keyOk,

            // A.8.12 - Data Leakage Prevention: requires a real redactor wired into
            // the logging pipeline. Marked false until PII is masked in
            // production logs.
            ["A.8.12 - Data Leakage Prevention (PII redactor active)"] = false,
        };

        var failedCount = 0;
        foreach (var value in controls.Values)
        {
            if (!value) failedCount++;
        }

        var status = failedCount switch
        {
            0 => "Compliant",
            1 => "Partial",
            _ => "NonCompliant",
        };

        return new ComplianceReport("ISO/IEC 27001:2022", status, controls, DateTime.UtcNow, failedCount);
    }
}

/// <summary>
/// Structured record holding the results of a compliance validation assessment.
/// </summary>
public record ComplianceReport(
    string Standard,
    string Status,
    Dictionary<string, bool> EvaluatedControls,
    DateTime EvaluatedAt,
    int FailedControlCount);
