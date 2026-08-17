using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Service for generating feedback responses based on configured feedback mode.
/// Ensures user confirmation of trigger detection without alerting potential aggressors.
/// </summary>
public class FeedbackModeService : IFeedbackModeService
{
    /// <summary>
    /// Generates feedback response based on configured mode.
    /// </summary>
    public Task<FeedbackResponse> GenerateFeedbackAsync(
        FeedbackMode feedbackMode,
        DeceptiveDisguise? deceptiveDisguise = null,
        CancellationToken cancellationToken = default)
    {
        var response = feedbackMode switch
        {
            FeedbackMode.Standard => GenerateStandardFeedback(),
            FeedbackMode.Silent => GenerateSilentFeedback(),
            FeedbackMode.HapticOnly => GenerateHapticOnlyFeedback(),
            FeedbackMode.Deceptive => GenerateDeceptiveFeedback(deceptiveDisguise),
            _ => GenerateStandardFeedback()
        };

        return Task.FromResult(response);
    }

    /// <summary>
    /// Standard feedback: normal audio/visual confirmation.
    /// </summary>
    private FeedbackResponse GenerateStandardFeedback()
    {
        return new FeedbackResponse
        {
            Mode = FeedbackMode.Standard,
            ShowVisual = true,
            PlayAudio = true,
            TriggerHaptic = true,
            DisplayMessage = "Emergency response activated. Help is on the way."
        };
    }

    /// <summary>
    /// Silent feedback: no UI change, user trusts system activated.
    /// </summary>
    private FeedbackResponse GenerateSilentFeedback()
    {
        return new FeedbackResponse
        {
            Mode = FeedbackMode.Silent,
            ShowVisual = false,
            PlayAudio = false,
            TriggerHaptic = false,
            DisplayMessage = null
        };
    }

    /// <summary>
    /// Haptic only: subtle vibration, no screen/audio feedback.
    /// </summary>
    private FeedbackResponse GenerateHapticOnlyFeedback()
    {
        return new FeedbackResponse
        {
            Mode = FeedbackMode.HapticOnly,
            ShowVisual = false,
            PlayAudio = false,
            TriggerHaptic = true,
            DisplayMessage = null
        };
    }

    /// <summary>
    /// Deceptive feedback: UI disguises as innocent app while secretly dispatching help.
    /// </summary>
    private FeedbackResponse GenerateDeceptiveFeedback(DeceptiveDisguise? disguise)
    {
        var (disguiseApp, fakeMessage) = (disguise ?? DeceptiveDisguise.Maps) switch
        {
            DeceptiveDisguise.Maps => ("maps", "Finding nearby locations..."),
            DeceptiveDisguise.Weather => ("weather", "Loading weather forecast..."),
            DeceptiveDisguise.Music => ("music", "Loading your music library..."),
            DeceptiveDisguise.Calculator => ("calculator", ""),
            DeceptiveDisguise.Notes => ("notes", "Opening notes..."),
            _ => ("maps", "Loading...")
        };

        return new FeedbackResponse
        {
            Mode = FeedbackMode.Deceptive,
            ShowVisual = true,  // Shows fake UI
            PlayAudio = false,  // No alert sounds
            TriggerHaptic = false,  // No vibration that might alert aggressor
            DeceptiveDisguise = disguiseApp,
            DisplayMessage = fakeMessage
        };
    }
}
