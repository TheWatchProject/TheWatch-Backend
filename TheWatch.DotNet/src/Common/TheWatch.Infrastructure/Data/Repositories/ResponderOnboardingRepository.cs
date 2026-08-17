using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for responder onboarding operations.
/// Handles training modules, background checks, and profile management.
/// </summary>
public class ResponderOnboardingRepository : IResponderOnboardingRepository
{
    private readonly WatchDbContext _context;

    public ResponderOnboardingRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<ResponderProfile?> GetResponderProfileAsync(Guid responderId)
    {
        return await _context.ResponderProfiles
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.ResponderId == responderId);
    }

    public async Task<ResponderProfile> CreateResponderProfileAsync(ResponderProfile profile)
    {
        _context.ResponderProfiles.Add(profile);
        await _context.SaveChangesAsync();
        return profile;
    }

    public async Task UpdateResponderProfileAsync(ResponderProfile profile)
    {
        _context.ResponderProfiles.Update(profile);
        await _context.SaveChangesAsync();
    }

    public async Task<List<TrainingModule>> GetAllTrainingModulesAsync()
    {
        return await _context.TrainingModules
            .OrderBy(t => t.SortOrder)
            .ToListAsync();
    }

    public async Task<TrainingModule?> GetTrainingModuleAsync(Guid moduleId)
    {
        return await _context.TrainingModules
            .FirstOrDefaultAsync(t => t.ModuleId == moduleId);
    }

    public async Task<List<ResponderTrainingCompletion>> GetResponderTrainingCompletionsAsync(Guid responderId)
    {
        return await _context.ResponderTrainingCompletions
            .Include(c => c.Module)
            .Where(c => c.ResponderId == responderId)
            .ToListAsync();
    }

    public async Task<ResponderTrainingCompletion?> GetTrainingCompletionAsync(Guid responderId, Guid moduleId)
    {
        return await _context.ResponderTrainingCompletions
            .Include(c => c.Module)
            .FirstOrDefaultAsync(c => c.ResponderId == responderId && c.ModuleId == moduleId);
    }

    public async Task<ResponderTrainingCompletion> CreateTrainingCompletionAsync(ResponderTrainingCompletion completion)
    {
        completion.CompletedAt = DateTime.UtcNow;
        _context.ResponderTrainingCompletions.Add(completion);
        await _context.SaveChangesAsync();
        return completion;
    }

    public async Task UpdateTrainingCompletionAsync(ResponderTrainingCompletion completion)
    {
        _context.ResponderTrainingCompletions.Update(completion);
        await _context.SaveChangesAsync();
    }

    public async Task<BackgroundCheckRecord?> GetLatestBackgroundCheckAsync(Guid responderId)
    {
        return await _context.BackgroundCheckRecords
            .Where(b => b.ResponderId == responderId)
            .OrderByDescending(b => b.SubmittedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<BackgroundCheckRecord?> GetBackgroundCheckAsync(Guid checkId)
    {
        return await _context.BackgroundCheckRecords
            .FirstOrDefaultAsync(b => b.CheckId == checkId);
    }

    public async Task<BackgroundCheckRecord> CreateBackgroundCheckAsync(BackgroundCheckRecord check)
    {
        check.SubmittedAt = DateTime.UtcNow;
        _context.BackgroundCheckRecords.Add(check);
        await _context.SaveChangesAsync();
        return check;
    }

    public async Task UpdateBackgroundCheckAsync(BackgroundCheckRecord check)
    {
        _context.BackgroundCheckRecords.Update(check);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateBackgroundCheckStatusAsync(Guid checkId, string status, string? resultNotes = null)
    {
        var check = await _context.BackgroundCheckRecords
            .FirstOrDefaultAsync(b => b.CheckId == checkId);

        if (check is not null)
        {
            check.Status = status;
            check.ResultNotes = resultNotes;
            check.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    // Schedule management

    public async Task<List<DesignatedResponderSchedule>> GetResponderSchedulesAsync(Guid responderId)
    {
        return await _context.DesignatedResponderSchedules
            .Where(s => s.ResponderId == responderId && s.IsActive)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<DesignatedResponderSchedule> CreateScheduleAsync(DesignatedResponderSchedule schedule)
    {
        _context.DesignatedResponderSchedules.Add(schedule);
        await _context.SaveChangesAsync();
        return schedule;
    }

    public async Task UpdateScheduleAsync(DesignatedResponderSchedule schedule)
    {
        schedule.UpdatedAt = DateTime.UtcNow;
        _context.DesignatedResponderSchedules.Update(schedule);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteScheduleAsync(Guid designationId)
    {
        var schedule = await _context.DesignatedResponderSchedules
            .FirstOrDefaultAsync(s => s.DesignationId == designationId);

        if (schedule is not null)
        {
            schedule.IsActive = false;
            schedule.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}
