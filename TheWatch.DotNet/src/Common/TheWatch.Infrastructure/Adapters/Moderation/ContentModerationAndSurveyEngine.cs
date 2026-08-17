using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Adapters.Moderation;

public sealed record ContentModerationResult(
    string ContentId,
    bool IsFlagged,
    double ToxicityScore,
    IReadOnlyList<string> FlaggedCategories,
    string RecommendedAction
);

public sealed record CitizenIncidentSurvey(
    string SurveyId,
    string IncidentId,
    string CitizenUserId,
    int RatingOneToFive,
    int ResponderResponseTimeMinutes,
    string FeedbackText,
    bool FeltSafeDuringResolution,
    DateTime SubmittedAtUtc
);

public interface IContentModerationAndSurveyEngine
{
    Task<ContentModerationResult> ModerateTextContentAsync(string contentId, string text);
    Task<string> IngestCitizenSurveyAsync(CitizenIncidentSurvey survey);
    double GetAverageIncidentSatisfactionRating(string? incidentId = null);
}

/// <summary>
/// Autonomous AI Content Safety Moderation and Post-Incident Citizen Feedback Survey Engine.
/// </summary>
public sealed class ContentModerationAndSurveyEngine : IContentModerationAndSurveyEngine
{
    private readonly ILogger<ContentModerationAndSurveyEngine> _logger;
    private readonly ConcurrentDictionary<string, CitizenIncidentSurvey> _surveys = new();
    private static readonly string[] ProhibitedWords = { "bomb_threat", "kill_confirm", "terrorist_strike" };

    public ContentModerationAndSurveyEngine(ILogger<ContentModerationAndSurveyEngine> logger)
    {
        _logger = logger;
    }

    public Task<ContentModerationResult> ModerateTextContentAsync(string contentId, string text)
    {
        var lower = text.ToLowerInvariant();
        var flagged = new List<string>();
        double toxicity = 0.05;

        foreach (var word in ProhibitedWords)
        {
            if (lower.Contains(word.Replace("_", " ")))
            {
                flagged.Add("ViolenceAndThreats");
                toxicity = 0.95;
            }
        }

        bool isFlagged = flagged.Any() || toxicity > 0.70;
        string action = isFlagged ? "EscalateToSafetyOfficer" : "AllowBroadcast";

        return Task.FromResult(new ContentModerationResult(contentId, isFlagged, toxicity, flagged, action));
    }

    public Task<string> IngestCitizenSurveyAsync(CitizenIncidentSurvey survey)
    {
        _surveys[survey.SurveyId] = survey;
        _logger.LogInformation("Citizen survey {SurveyId} ingested for Incident {IncidentId}. Rating: {Rating}/5",
            survey.SurveyId, survey.IncidentId, survey.RatingOneToFive);

        return Task.FromResult(survey.SurveyId);
    }

    public double GetAverageIncidentSatisfactionRating(string? incidentId = null)
    {
        var list = _surveys.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(incidentId))
        {
            list = list.Where(s => s.IncidentId == incidentId);
        }

        var items = list.ToList();
        return items.Any() ? items.Average(s => s.RatingOneToFive) : 5.0;
    }
}
