using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for admin action audit operations.
/// Immutable audit log for HQ/admin actions.
/// </summary>
public class AdminAuditRepository : IAdminAuditRepository
{
    private readonly WatchDbContext _context;

    public AdminAuditRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<AdminActionAudit> LogActionAsync(AdminActionAudit action, CancellationToken cancellationToken = default)
    {
        if (action.ActionId == Guid.Empty)
            action.ActionId = Guid.NewGuid();
        action.Timestamp = DateTime.UtcNow;

        _context.AdminActionAudits.Add(action);
        await _context.SaveChangesAsync(cancellationToken);

        return action;
    }

    /// <summary>
    /// Helper method to log an action with simplified parameters.
    /// </summary>
    public async Task LogActionAsync(Guid userId, string actionType, string targetType, string targetId, object? metadata = null)
    {
        var action = new AdminActionAudit
        {
            ActionId = Guid.NewGuid(),
            AdminUserId = userId,
            ActionType = actionType,
            TargetType = targetType,
            TargetId = targetId,
            Metadata = metadata != null ? JsonSerializer.Serialize(metadata) : null,
            Timestamp = DateTime.UtcNow
        };

        await LogActionAsync(action);
    }

    public async Task<IEnumerable<AdminActionAudit>> GetByAdminAsync(Guid adminUserId, DateTime? startDate = null, 
        DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var query = _context.AdminActionAudits.Where(a => a.AdminUserId == adminUserId);

        if (startDate.HasValue)
            query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.Timestamp <= endDate.Value);

        return await query.OrderByDescending(a => a.Timestamp).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AdminActionAudit>> GetByTargetAsync(string targetType, string targetId, 
        CancellationToken cancellationToken = default)
    {
        // Note: Entity uses string for TargetId (entity ID as string)
        // targetType would need to be stored separately or parsed from ActionType
        return await _context.AdminActionAudits
            .Where(a => a.TargetId == targetId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AdminActionAudit>> GetByActionTypeAsync(string actionType, 
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var query = _context.AdminActionAudits.Where(a => a.ActionType == actionType);

        if (startDate.HasValue)
            query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.Timestamp <= endDate.Value);

        return await query.OrderByDescending(a => a.Timestamp).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AdminActionAudit>> GetRecentActionsAsync(int count = 100, 
        CancellationToken cancellationToken = default)
    {
        return await _context.AdminActionAudits
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AdminActionAudit>> SearchAsync(string? targetType = null, 
        string? actionType = null, Guid? adminUserId = null, DateTime? startDate = null, 
        DateTime? endDate = null, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var query = _context.AdminActionAudits.AsQueryable();

        if (!string.IsNullOrEmpty(actionType))
            query = query.Where(a => a.ActionType == actionType);

        if (adminUserId.HasValue)
            query = query.Where(a => a.AdminUserId == adminUserId.Value);

        if (startDate.HasValue)
            query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.Timestamp <= endDate.Value);

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
