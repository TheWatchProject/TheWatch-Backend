using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for user safety settings.
/// </summary>
public class SafetySettingsRepository : ISafetySettingsRepository
{
    private readonly WatchDbContext _context;

    public SafetySettingsRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<SafetySettings?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.SafetySettings
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
    }

    public async Task<SafetySettings> CreateOrUpdateAsync(SafetySettings settings, CancellationToken cancellationToken = default)
    {
        var existing = await GetByUserIdAsync(settings.UserId, cancellationToken);

        if (existing is null)
        {
            settings.UpdatedAt = DateTime.UtcNow;
            _context.SafetySettings.Add(settings);
            await _context.SaveChangesAsync(cancellationToken);
            return settings;
        }

        // Update existing settings
        existing.VoiceActivationEnabled = settings.VoiceActivationEnabled;
        existing.StealthModeEnabled = settings.StealthModeEnabled;
        existing.FalsePositiveProtection = settings.FalsePositiveProtection;
        existing.AlertRadiusMeters = settings.AlertRadiusMeters;
        existing.IncludeDesignatedResponders = settings.IncludeDesignatedResponders;
        existing.TrustedContactsOnly = settings.TrustedContactsOnly;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }
}
