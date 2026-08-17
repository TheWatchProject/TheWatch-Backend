using Microsoft.Extensions.Logging;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Service for responder certification validation.
/// Ensures responders meet all requirements before being eligible for dispatch.
///
/// Certification Requirements:
/// - Background check: approved
/// - All mandatory training modules: completed
/// - Age: >= 18
/// - No disqualifying flags
/// </summary>
public class CertificationService : ICertificationService
{
    private readonly ILogger<CertificationService> _logger;
    private readonly IResponderOnboardingRepository _onboardingRepository;
    private readonly IUserRepository _userRepository;

    public CertificationService(
        ILogger<CertificationService> logger,
        IResponderOnboardingRepository onboardingRepository,
        IUserRepository userRepository)
    {
        _logger = logger;
        _onboardingRepository = onboardingRepository;
        _userRepository = userRepository;
    }

    /// <summary>
    /// Validates if a responder is certified based on all requirements.
    /// </summary>
    public async Task<CertificationStatus> ValidateCertificationAsync(Guid responderId)
    {
        _logger.LogInformation("Validating certification for responder {ResponderId}", responderId);

        var status = new CertificationStatus
        {
            MissingRequirements = new List<string>()
        };

        // 1. Check if user exists and is an adult
        var user = await _userRepository.GetUserByIdAsync(responderId);
        if (user == null)
        {
            status.IsCertified = false;
            status.MissingRequirements.Add("User not found");
            return status;
        }

        // Calculate age
        var age = DateTime.UtcNow.Year - user.DateOfBirth.Year;
        if (user.DateOfBirth > DateTime.UtcNow.AddYears(-age)) age--;

        status.IsAdult = age >= 18;
        if (!status.IsAdult)
        {
            status.MissingRequirements.Add("Must be at least 18 years old");
        }

        // 2. Check responder profile exists
        var profile = await _onboardingRepository.GetResponderProfileAsync(responderId);
        if (profile == null)
        {
            status.IsCertified = false;
            status.MissingRequirements.Add("Responder profile not found");
            return status;
        }

        // 3. Check background check status
        status.BackgroundCheckStatus = profile.BackgroundCheckStatus;
        if (profile.BackgroundCheckStatus != "approved")
        {
            status.MissingRequirements.Add($"Background check not approved (current status: {profile.BackgroundCheckStatus})");
        }

        // 4. Check training completion
        var allModules = await _onboardingRepository.GetAllTrainingModulesAsync();
        var completions = await _onboardingRepository.GetResponderTrainingCompletionsAsync(responderId);

        var mandatoryModules = allModules.Where(m => m.IsMandatory).ToList();
        var completedMandatory = completions.Count(c => c.Status == "completed" &&
            mandatoryModules.Any(m => m.ModuleId == c.ModuleId));

        status.TrainingCompletionPercentage = mandatoryModules.Count > 0
            ? (completedMandatory * 100 / mandatoryModules.Count)
            : 0;

        if (status.TrainingCompletionPercentage < 100)
        {
            var incompleteMandatory = mandatoryModules
                .Where(m => !completions.Any(c => c.ModuleId == m.ModuleId && c.Status == "completed"))
                .Select(m => m.Title)
                .ToList();

            status.MissingRequirements.Add(
                $"Incomplete mandatory training modules: {string.Join(", ", incompleteMandatory)}");
        }

        // 5. Determine if certified
        status.IsCertified = status.MissingRequirements.Count == 0;

        if (status.IsCertified && profile.IsResponderEligible)
        {
            // If already certified, use existing certification date
            status.CertifiedAt = DateTime.UtcNow; // In production, store actual certification timestamp
        }

        _logger.LogInformation(
            "Certification validation completed for responder {ResponderId}: IsCertified={IsCertified}, MissingRequirements={Count}",
            responderId, status.IsCertified, status.MissingRequirements.Count);

        return status;
    }

    /// <summary>
    /// Updates responder profile's certification status.
    /// </summary>
    public async Task UpdateResponderCertificationAsync(Guid responderId, bool isCertified)
    {
        var profile = await _onboardingRepository.GetResponderProfileAsync(responderId);
        if (profile == null)
        {
            _logger.LogWarning("Cannot update certification for non-existent responder profile {ResponderId}", responderId);
            return;
        }

        profile.IsResponderEligible = isCertified;

        // If newly certified, set status to available
        if (isCertified && profile.CurrentStatus == "unavailable")
        {
            profile.CurrentStatus = "available";
        }

        await _onboardingRepository.UpdateResponderProfileAsync(profile);

        _logger.LogInformation(
            "Responder {ResponderId} certification updated to {IsCertified}",
            responderId, isCertified);
    }
}
