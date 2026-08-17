using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for authentication operations.
/// </summary>
public class AuthRepository : IAuthRepository
{
    private readonly WatchDbContext _context;

    public AuthRepository(WatchDbContext context)
    {
        _context = context;
    }

    // User lookup methods
    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> GetUserByPhoneAsync(string phone)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Phone == phone);
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _context.Users.FindAsync(userId);
    }

    // Refresh Token operations
    public async Task<RefreshToken?> GetRefreshTokenAsync(string tokenHash)
    {
        return await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAt == null);
    }

    public async Task<RefreshToken> CreateRefreshTokenAsync(RefreshToken token)
    {
        if (token.TokenId == Guid.Empty)
            token.TokenId = Guid.NewGuid();
        token.IssuedAt = DateTime.UtcNow;

        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();

        return token;
    }

    public async Task RevokeRefreshTokenAsync(Guid tokenId, string reason)
    {
        var token = await _context.RefreshTokens.FindAsync(tokenId);
        if (token != null)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedReason = reason;
            await _context.SaveChangesAsync();
        }
    }

    // Session Token operations
    public async Task<SessionToken?> GetSessionTokenAsync(string tokenHash)
    {
        return await _context.SessionTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<SessionToken> CreateSessionTokenAsync(SessionToken session)
    {
        if (session.SessionId == Guid.Empty)
            session.SessionId = Guid.NewGuid();
        session.CreatedAt = DateTime.UtcNow;

        _context.SessionTokens.Add(session);
        await _context.SaveChangesAsync();

        return session;
    }

    public async Task RevokeSessionTokenAsync(Guid sessionId)
    {
        var session = await _context.SessionTokens.FindAsync(sessionId);
        if (session != null)
        {
            session.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<SessionToken>> GetActiveSessionsForUserAsync(Guid userId)
    {
        return await _context.SessionTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    // MFA operations
    public async Task<MfaEnrollment?> GetActiveMfaEnrollmentAsync(Guid userId)
    {
        return await _context.MfaEnrollments
            .FirstOrDefaultAsync(e => e.UserId == userId && e.IsActive);
    }

    public async Task<MfaEnrollment> CreateMfaEnrollmentAsync(MfaEnrollment enrollment)
    {
        if (enrollment.EnrollmentId == Guid.Empty)
            enrollment.EnrollmentId = Guid.NewGuid();
        enrollment.EnrolledAt = DateTime.UtcNow;
        enrollment.IsActive = true;

        _context.MfaEnrollments.Add(enrollment);
        await _context.SaveChangesAsync();

        return enrollment;
    }

    public async Task DeactivateMfaEnrollmentAsync(Guid enrollmentId)
    {
        var enrollment = await _context.MfaEnrollments.FindAsync(enrollmentId);
        if (enrollment != null)
        {
            enrollment.IsActive = false;
            await _context.SaveChangesAsync();
        }
    }

    // Password reset operations
    public async Task<PasswordResetToken> CreatePasswordResetTokenAsync(PasswordResetToken token)
    {
        if (token.TokenId == Guid.Empty)
            token.TokenId = Guid.NewGuid();
        token.CreatedAt = DateTime.UtcNow;

        _context.PasswordResetTokens.Add(token);
        await _context.SaveChangesAsync();

        return token;
    }

    public async Task<PasswordResetToken?> GetPasswordResetTokenAsync(string tokenHash)
    {
        return await _context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow);
    }

    public async Task MarkPasswordResetTokenUsedAsync(Guid tokenId)
    {
        var token = await _context.PasswordResetTokens.FindAsync(tokenId);
        if (token != null)
        {
            token.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    // Login attempt tracking
    public async Task<LoginAttempt> RecordLoginAttemptAsync(LoginAttempt attempt)
    {
        if (attempt.AttemptId == Guid.Empty)
            attempt.AttemptId = Guid.NewGuid();
        attempt.AttemptedAt = DateTime.UtcNow;

        _context.LoginAttempts.Add(attempt);
        await _context.SaveChangesAsync();

        return attempt;
    }

    public async Task<int> GetRecentFailedLoginAttemptsAsync(string identifierHash, TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        return await _context.LoginAttempts
            .Where(a => a.IdentifierHash == identifierHash && !a.Success && a.AttemptedAt >= cutoff)
            .CountAsync();
    }

    public async Task UpdateUserPasswordHashAsync(Guid userId, string passwordHash)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.PasswordHash = passwordHash;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}
