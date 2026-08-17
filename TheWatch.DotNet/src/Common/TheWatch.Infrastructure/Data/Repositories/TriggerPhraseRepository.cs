using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for trigger phrase operations.
/// </summary>
public class TriggerPhraseRepository : ITriggerPhraseRepository
{
    private readonly WatchDbContext _context;

    public TriggerPhraseRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<TriggerPhrase?> GetByIdAsync(Guid phraseId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.TriggerPhrases
            .FirstOrDefaultAsync(p => p.PhraseId == phraseId && p.UserId == userId, cancellationToken);
    }

    public async Task<IEnumerable<TriggerPhrase>> GetUserPhrasesAsync(Guid userId, bool? isActive = null, 
        string? responseType = null, CancellationToken cancellationToken = default)
    {
        var query = _context.TriggerPhrases.Where(p => p.UserId == userId);

        if (!string.IsNullOrEmpty(responseType))
            query = query.Where(p => p.ResponseType == responseType);

        return await query
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<TriggerPhrase> CreateAsync(TriggerPhrase phrase, CancellationToken cancellationToken = default)
    {
        if (phrase.PhraseId == Guid.Empty)
            phrase.PhraseId = Guid.NewGuid();
        phrase.CreatedAt = DateTime.UtcNow;
        phrase.UpdatedAt = DateTime.UtcNow;

        _context.TriggerPhrases.Add(phrase);
        await _context.SaveChangesAsync(cancellationToken);

        return phrase;
    }

    public async Task<TriggerPhrase> UpdateAsync(TriggerPhrase phrase, CancellationToken cancellationToken = default)
    {
        phrase.UpdatedAt = DateTime.UtcNow;

        _context.TriggerPhrases.Update(phrase);
        await _context.SaveChangesAsync(cancellationToken);

        return phrase;
    }

    public async Task DeleteAsync(Guid phraseId, Guid userId, CancellationToken cancellationToken = default)
    {
        var phrase = await _context.TriggerPhrases
            .FirstOrDefaultAsync(p => p.PhraseId == phraseId && p.UserId == userId, cancellationToken);
        
        if (phrase != null)
        {
            _context.TriggerPhrases.Remove(phrase);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid phraseId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.TriggerPhrases
            .AnyAsync(p => p.PhraseId == phraseId && p.UserId == userId, cancellationToken);
    }
}
