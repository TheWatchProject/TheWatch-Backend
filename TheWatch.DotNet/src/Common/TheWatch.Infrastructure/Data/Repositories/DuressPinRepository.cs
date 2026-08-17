using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for duress PIN operations.
/// Uses BCrypt for secure PIN hashing.
/// </summary>
public class DuressPinRepository : IDuressPinRepository
{
    private readonly WatchDbContext _context;

    public DuressPinRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<DuressPin?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.DuressPins
            .FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken);
    }

    public async Task<DuressPin> CreateOrUpdateAsync(DuressPin duressPin, CancellationToken cancellationToken = default)
    {
        var existing = await GetByUserIdAsync(duressPin.UserId, cancellationToken);

        if (existing is null)
        {
            duressPin.UpdatedAt = DateTime.UtcNow;
            _context.DuressPins.Add(duressPin);
        }
        else
        {
            existing.DuressPinHash = duressPin.DuressPinHash;
            existing.SafePinHash = duressPin.SafePinHash;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return existing ?? duressPin;
    }

    public async Task<bool> VerifyDuressPinAsync(Guid userId, string pin, CancellationToken cancellationToken = default)
    {
        var duressPin = await GetByUserIdAsync(userId, cancellationToken);

        if (duressPin?.DuressPinHash is null)
            return false;

        // Use BCrypt to verify the PIN hash
        return BCrypt.Net.BCrypt.Verify(pin, duressPin.DuressPinHash);
    }

    public async Task<bool> VerifySafePinAsync(Guid userId, string pin, CancellationToken cancellationToken = default)
    {
        var duressPin = await GetByUserIdAsync(userId, cancellationToken);

        if (duressPin?.SafePinHash is null)
            return false;

        // Use BCrypt to verify the PIN hash
        return BCrypt.Net.BCrypt.Verify(pin, duressPin.SafePinHash);
    }
}
