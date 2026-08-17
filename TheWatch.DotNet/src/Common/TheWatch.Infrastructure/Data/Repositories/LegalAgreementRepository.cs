using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for legal agreement management.
/// Handles EULA, Terms of Service, Privacy Policy versioning.
/// </summary>
public class LegalAgreementRepository : ILegalAgreementRepository
{
    private readonly WatchDbContext _context;

    public LegalAgreementRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<List<LegalAgreement>> GetCurrentAgreementsAsync()
    {
        // Get the latest version of each agreement type that is effective
        var agreementTypes = await _context.LegalAgreements
            .Where(a => a.EffectiveDate <= DateTime.UtcNow)
            .GroupBy(a => a.AgreementType)
            .Select(g => g.OrderByDescending(a => a.EffectiveDate).First())
            .ToListAsync();

        return agreementTypes;
    }

    public async Task<LegalAgreement?> GetAgreementByTypeAndVersionAsync(string agreementType, string version)
    {
        return await _context.LegalAgreements
            .FirstOrDefaultAsync(a => a.AgreementType == agreementType && a.Version == version);
    }

    public async Task<LegalAgreement?> GetLatestAgreementByTypeAsync(string agreementType)
    {
        return await _context.LegalAgreements
            .Where(a => a.AgreementType == agreementType && a.EffectiveDate <= DateTime.UtcNow)
            .OrderByDescending(a => a.EffectiveDate)
            .FirstOrDefaultAsync();
    }

    public async Task<List<LegalAgreement>> GetVersionHistoryAsync(string agreementType)
    {
        return await _context.LegalAgreements
            .Where(a => a.AgreementType == agreementType)
            .OrderByDescending(a => a.EffectiveDate)
            .ToListAsync();
    }
}
