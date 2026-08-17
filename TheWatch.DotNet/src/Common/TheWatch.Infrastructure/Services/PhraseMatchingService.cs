using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Service for matching detected phrases against user trigger phrases with fuzzy matching.
/// Handles speech recognition errors using Levenshtein distance and phonetic matching.
/// </summary>
public class PhraseMatchingService : IPhraseMatchingService
{
    /// <summary>
    /// Finds the best matching trigger phrase for a detected phrase.
    /// </summary>
    public async Task<(TriggerPhrase? MatchedPhrase, double Confidence)?> FindBestMatchAsync(
        string detectedPhrase,
        IEnumerable<TriggerPhrase> userPhrases,
        string sensitivityLevel = "medium",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(detectedPhrase))
            return null;

        var normalizedDetected = NormalizePhrase(detectedPhrase);
        var threshold = GetConfidenceThreshold(sensitivityLevel);

        TriggerPhrase? bestMatch = null;
        double bestConfidence = 0;

        foreach (var phrase in userPhrases.Where(p => p.IsActive))
        {
            // Check primary phrase
            var confidence = CalculateConfidence(normalizedDetected, NormalizePhrase(phrase.PhraseText));
            if (confidence > bestConfidence)
            {
                bestConfidence = confidence;
                bestMatch = phrase;
            }

            // Check alternative phrases
            if (!string.IsNullOrEmpty(phrase.AlternativePhrases))
            {
                try
                {
                    var alternatives = JsonSerializer.Deserialize<string[]>(phrase.AlternativePhrases);
                    if (alternatives != null)
                    {
                        foreach (var alt in alternatives)
                        {
                            confidence = CalculateConfidence(normalizedDetected, NormalizePhrase(alt));
                            if (confidence > bestConfidence)
                            {
                                bestConfidence = confidence;
                                bestMatch = phrase;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore JSON parsing errors
                }
            }
        }

        // Only return match if confidence exceeds threshold
        if (bestMatch != null && bestConfidence >= threshold)
        {
            return (bestMatch, bestConfidence);
        }

        return null;
    }

    /// <summary>
    /// Calculates confidence score between two phrases using Levenshtein distance.
    /// </summary>
    public double CalculateConfidence(string detectedPhrase, string triggerPhrase)
    {
        if (string.IsNullOrEmpty(detectedPhrase) || string.IsNullOrEmpty(triggerPhrase))
            return 0;

        var normalized1 = NormalizePhrase(detectedPhrase);
        var normalized2 = NormalizePhrase(triggerPhrase);

        // Exact match after normalization
        if (normalized1 == normalized2)
            return 1.0;

        // Calculate Levenshtein distance
        var distance = LevenshteinDistance(normalized1, normalized2);
        var maxLength = Math.Max(normalized1.Length, normalized2.Length);

        // Convert distance to similarity score (0-1)
        var similarity = 1.0 - ((double)distance / maxLength);

        return similarity;
    }

    /// <summary>
    /// Generates phonetic alternatives for a phrase (simplified implementation).
    /// In production, use libraries like Metaphone or Soundex for better phonetic matching.
    /// </summary>
    public IEnumerable<string> GeneratePhoneticAlternatives(string phrase)
    {
        var alternatives = new List<string> { phrase };

        // Common speech recognition substitutions
        var substitutions = new Dictionary<string, string[]>
        {
            { "help", new[] { "kelp", "held" } },
            { "now", new[] { "know", "no" } },
            { "need", new[] { "knead", "neat" } },
            { "me", new[] { "my", "we" } },
            { "call", new[] { "coal", "col" } },
            { "emergency", new[] { "emergencies", "emerge and see" } },
            { "nine", new[] { "nein", "line" } },
            { "one", new[] { "won", "1" } }
        };

        var words = phrase.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (substitutions.TryGetValue(word, out var subs))
            {
                foreach (var sub in subs)
                {
                    alternatives.Add(phrase.Replace(word, sub, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        return alternatives.Distinct();
    }

    /// <summary>
    /// Normalizes a phrase for comparison (lowercase, remove punctuation, trim).
    /// </summary>
    private string NormalizePhrase(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return string.Empty;

        // Convert to lowercase and remove common punctuation
        var normalized = phrase.ToLowerInvariant()
            .Replace(".", "")
            .Replace(",", "")
            .Replace("!", "")
            .Replace("?", "")
            .Replace("'", "")
            .Replace("\"", "")
            .Trim();

        // Normalize whitespace
        while (normalized.Contains("  "))
            normalized = normalized.Replace("  ", " ");

        return normalized;
    }

    /// <summary>
    /// Gets confidence threshold based on sensitivity level.
    /// </summary>
    private double GetConfidenceThreshold(string sensitivityLevel)
    {
        return sensitivityLevel?.ToLowerInvariant() switch
        {
            "low" => 0.6,      // More lenient, allows more fuzzy matches
            "medium" => 0.75,  // Balanced
            "high" => 0.9,     // Strict, requires near-exact match
            _ => 0.75
        };
    }

    /// <summary>
    /// Calculates Levenshtein distance between two strings.
    /// </summary>
    private int LevenshteinDistance(string s1, string s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;
        var matrix = new int[len1 + 1, len2 + 1];

        if (len1 == 0)
            return len2;
        if (len2 == 0)
            return len1;

        // Initialize first column and row
        for (int i = 0; i <= len1; i++)
            matrix[i, 0] = i;
        for (int j = 0; j <= len2; j++)
            matrix[0, j] = j;

        // Calculate distances
        for (int i = 1; i <= len1; i++)
        {
            for (int j = 1; j <= len2; j++)
            {
                int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[len1, len2];
    }
}
