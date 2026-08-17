using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for user operations.
/// Implements GDPR right-to-erasure via anonymization.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly WatchDbContext _context;

    public UserRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _context.Users
            .Include(u => u.ResponderProfile)
            .FirstOrDefaultAsync(u => u.UserId == userId);
    }

    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.ResponderProfile)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
    }

    public async Task<User> CreateUserAsync(User user)
    {
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        
        return user;
    }

    public async Task UpdateUserAsync(User user)
    {
        user.UpdatedAt = DateTime.UtcNow;

        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

    public async Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        user.UpdatedAt = DateTime.UtcNow;

        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task DeleteUserAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.DeletedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// GDPR right-to-erasure: Anonymizes user PII while preserving audit trail.
    /// Does NOT delete the record - replaces PII with anonymized values.
    /// </summary>
    public async Task AnonymizeUserAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return;

        // Replace PII with anonymized values
        user.Email = $"deleted-{userId}@anonymized.thewatch.app";
        user.Name = "Anonymized User";
        user.Phone = null;
        user.PiiState = "Anonymized";
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _context.Users.AnyAsync(u => u.Email == email);
    }

    public async Task<bool> PhoneExistsAsync(string phone)
    {
        return await _context.Users.AnyAsync(u => u.Phone == phone);
    }

    public async Task<List<UserAgreementConsent>> GetUserConsentsAsync(Guid userId)
    {
        return await _context.UserAgreementConsents
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.AcceptedAt)
            .ToListAsync();
    }

    public async Task<UserAgreementConsent> CreateConsentAsync(UserAgreementConsent consent)
    {
        consent.AcceptedAt = DateTime.UtcNow;
        _context.UserAgreementConsents.Add(consent);
        await _context.SaveChangesAsync();
        return consent;
    }

    public async Task<ParentalConsentRecord?> GetParentalConsentAsync(Guid minorUserId)
    {
        return await _context.ParentalConsentRecords
            .FirstOrDefaultAsync(c => c.MinorUserId == minorUserId);
    }

    public async Task<ParentalConsentRecord> CreateParentalConsentAsync(ParentalConsentRecord consent)
    {
        consent.SubmittedAt = DateTime.UtcNow;
        _context.ParentalConsentRecords.Add(consent);
        await _context.SaveChangesAsync();
        return consent;
    }

    public async Task UpdateParentalConsentStatusAsync(Guid consentId, string status, DateTime? verifiedAt = null)
    {
        var consent = await _context.ParentalConsentRecords.FindAsync(consentId);
        if (consent != null)
        {
            consent.Status = status;
            consent.VerifiedAt = verifiedAt;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<User>> GetRespondersByStatusAsync(string status, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Where(u => u.AccountType == "responder")
            .Where(u => u.ResponderProfile != null && u.ResponderProfile.CurrentStatus == status)
            .Include(u => u.ResponderProfile)
            .OrderBy(u => u.ResponderProfile!.LastStatusUpdate)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateResponderStatusAsync(Guid userId, string status, CancellationToken cancellationToken = default)
    {
        var profile = await _context.ResponderProfiles.FindAsync(new object[] { userId }, cancellationToken);
        if (profile != null)
        {
            profile.CurrentStatus = status;
            profile.LastStatusUpdate = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<ResponderProfile?> GetResponderProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.ResponderProfiles
            .Include(rp => rp.User)
            .FirstOrDefaultAsync(rp => rp.ResponderId == userId, cancellationToken);
    }

    public async Task<ResponderProfile> UpsertResponderProfileAsync(ResponderProfile profile, CancellationToken cancellationToken = default)
    {
        var existing = await _context.ResponderProfiles.FindAsync(new object[] { profile.ResponderId }, cancellationToken);

        if (existing != null)
        {
            // Update existing profile
            _context.Entry(existing).CurrentValues.SetValues(profile);
            await _context.SaveChangesAsync(cancellationToken);
            return existing;
        }
        else
        {
            // Create new profile
            _context.ResponderProfiles.Add(profile);
            await _context.SaveChangesAsync(cancellationToken);
            return profile;
        }
    }
}
