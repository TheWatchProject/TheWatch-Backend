using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TheWatch.Contracts;
using static TheWatch.Contracts.WhistleblowerAndTipsContracts;

namespace TheWatch.Geospatial.Db;

public interface IWhistleblowerAndTipsEngine
{
    CorporateWhistleblowerReport SubmitWhistleblowerReport(
        string ticker,
        WhistleblowerCategory category,
        string encryptedPayload,
        string rawClaimantSecretToken,
        bool isAnonymous,
        List<string>? attachmentHashes = null);

    CorporateWhistleblowerReport? RetrieveWhistleblowerReport(string reportId, string rawClaimantSecretToken);

    CommunitySafetyTip SubmitCommunityTip(
        CommunityTipCategory category,
        string description,
        double lat,
        double lon,
        string landmark,
        bool isAnonymous,
        string submitterAlias,
        bool rewardRequested);

    IReadOnlyList<CorporateWhistleblowerReport> GetAllWhistleblowerReports();
    IReadOnlyList<CommunitySafetyTip> GetAllCommunityTips();
    void UpdateWhistleblowerStatus(string reportId, AnonymousReportStatus newStatus);
    void UpdateTipStatus(string tipId, AnonymousReportStatus newStatus);
}

/// <summary>
/// Cryptographic SOX/SEC Whistleblower Intake & Community Safety Tips Engine.
/// </summary>
public sealed class WhistleblowerAndTipsEngine : IWhistleblowerAndTipsEngine
{
    private readonly ConcurrentDictionary<string, CorporateWhistleblowerReport> _whistleblowerReports = new();
    private readonly ConcurrentDictionary<string, CommunitySafetyTip> _communityTips = new();

    public CorporateWhistleblowerReport SubmitWhistleblowerReport(
        string ticker,
        WhistleblowerCategory category,
        string encryptedPayload,
        string rawClaimantSecretToken,
        bool isAnonymous,
        List<string>? attachmentHashes = null)
    {
        string tokenHash = ComputeSha256(rawClaimantSecretToken);

        var report = new CorporateWhistleblowerReport(
            $"SOX-WB-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ticker.ToUpperInvariant(),
            category,
            encryptedPayload,
            tokenHash,
            isAnonymous,
            attachmentHashes ?? new List<string>(),
            AnonymousReportStatus.SubmittedEncrypted,
            DateTime.UtcNow
        );

        _whistleblowerReports[report.ReportId] = report;
        return report;
    }

    public CorporateWhistleblowerReport? RetrieveWhistleblowerReport(string reportId, string rawClaimantSecretToken)
    {
        if (_whistleblowerReports.TryGetValue(reportId, out var report))
        {
            string tokenHash = ComputeSha256(rawClaimantSecretToken);
            if (report.AnonymousClaimantTokenHash.Equals(tokenHash, StringComparison.OrdinalIgnoreCase))
            {
                return report;
            }
        }
        return null;
    }

    public CommunitySafetyTip SubmitCommunityTip(
        CommunityTipCategory category,
        string description,
        double lat,
        double lon,
        string landmark,
        bool isAnonymous,
        string submitterAlias,
        bool rewardRequested)
    {
        string voucherCode = rewardRequested ? $"VOUCHER-{Guid.NewGuid():N}"[..10].ToUpperInvariant() : string.Empty;

        var tip = new CommunitySafetyTip(
            $"TIP-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            category,
            description,
            lat,
            lon,
            landmark,
            isAnonymous,
            isAnonymous ? "Anonymous Citizen" : submitterAlias,
            rewardRequested,
            voucherCode,
            AnonymousReportStatus.SubmittedEncrypted,
            DateTime.UtcNow
        );

        _communityTips[tip.TipId] = tip;
        return tip;
    }

    public IReadOnlyList<CorporateWhistleblowerReport> GetAllWhistleblowerReports() =>
        _whistleblowerReports.Values.OrderByDescending(r => r.SubmittedAtUtc).ToList();

    public IReadOnlyList<CommunitySafetyTip> GetAllCommunityTips() =>
        _communityTips.Values.OrderByDescending(t => t.SubmittedAtUtc).ToList();

    public void UpdateWhistleblowerStatus(string reportId, AnonymousReportStatus newStatus)
    {
        if (_whistleblowerReports.TryGetValue(reportId, out var r))
        {
            _whistleblowerReports[reportId] = r with { Status = newStatus };
        }
    }

    public void UpdateTipStatus(string tipId, AnonymousReportStatus newStatus)
    {
        if (_communityTips.TryGetValue(tipId, out var t))
        {
            _communityTips[tipId] = t with { Status = newStatus };
        }
    }

    private static string ComputeSha256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
