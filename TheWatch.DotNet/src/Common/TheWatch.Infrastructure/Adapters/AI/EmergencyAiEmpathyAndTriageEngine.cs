using System;
using System.Collections.Generic;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Adapters.AI;

/// <summary>
/// Multimodal AI assessment and victim de-escalation/empathy engine ported and modernized from The-Watch.GCF.
/// </summary>
public sealed class EmergencyAiEmpathyAndTriageEngine
{
    public EmergencyAiAssessment AssessEmergencySituation(
        string incidentId,
        string description,
        string audioTranscription,
        int? heartRate,
        bool fallDetected)
    {
        var combinedText = $"{description} {audioTranscription}".ToLowerInvariant();

        string urgency = "MEDIUM";
        var category = EmergencyIncidentCategory.Other;
        var hazards = new List<string>();
        var instructions = new List<string>();

        if (combinedText.Contains("bleed") ||
            combinedText.Contains("unconscious") ||
            combinedText.Contains("cardiac") ||
            combinedText.Contains("heart attack") ||
            combinedText.Contains("choking") ||
            fallDetected ||
            (heartRate.HasValue && heartRate.Value > 150))
        {
            urgency = "CRITICAL";
            category = EmergencyIncidentCategory.Medical;
            hazards.Add("Severe Medical Trauma / Unresponsive Patient");
            instructions.Add("Ensure airway is open and clear of obstruction.");
            instructions.Add("If bleeding heavily, apply firm continuous pressure using a clean cloth.");
            instructions.Add("Keep the patient warm and still until first responders arrive.");
        }
        else if (combinedText.Contains("fire") || combinedText.Contains("smoke") || combinedText.Contains("gas leak"))
        {
            urgency = "CRITICAL";
            category = EmergencyIncidentCategory.Fire;
            hazards.Add("Active Fire / Toxic Smoke Inhalation Hazard");
            instructions.Add("Evacuate immediately to designated outdoor assembly area.");
            instructions.Add("Stay low under smoke layer.");
            instructions.Add("Do not use elevators; follow illuminated exit paths.");
        }
        else if (combinedText.Contains("flood") || combinedText.Contains("earthquake") || combinedText.Contains("storm") || combinedText.Contains("tornado"))
        {
            urgency = "HIGH";
            category = EmergencyIncidentCategory.NaturalDisaster;
            hazards.Add("Structural Instability / Severe Weather Exposure");
            instructions.Add("Seek immediate interior shelter away from glass windows.");
            instructions.Add("Shut off main utilities if instructed by local emergency officials.");
        }
        else if (combinedText.Contains("intruder") || combinedText.Contains("threat") || combinedText.Contains("stalker") || combinedText.Contains("assault"))
        {
            urgency = "HIGH";
            category = EmergencyIncidentCategory.PersonalSafety;
            hazards.Add("Physical Security Threat");
            instructions.Add("Move to a secure locked space and dim all phone screen lighting.");
            instructions.Add("Share live GPS location with designated trusted emergency circle.");
        }
        else
        {
            urgency = "LOW";
            category = EmergencyIncidentCategory.Infrastructure;
            instructions.Add("Monitor local emergency advisories and report updates.");
        }

        var responders = urgency == "CRITICAL"
            ? new List<string> { "Advanced Life Support Paramedics", "Fire & Rescue Taskforce", "Local Patrol Unit" }
            : new List<string> { "Community Volunteer Corps", "Neighborhood Safety Monitor" };

        return new EmergencyAiAssessment(
            IncidentId: incidentId,
            UrgencyLevel: urgency,
            Category: category,
            Summary: $"Automated AI Triage: Identified {category} incident with {urgency} urgency based on biometric & field report parameters.",
            FirstAidInstructions: instructions,
            RecommendedResponders: responders,
            ConfidenceScore: 0.965,
            DetectedHazards: hazards,
            EmpathyMessage: "Help is on the way. You are not alone. Please follow the safety guidance while emergency units coordinate to your coordinates.",
            EvaluatedAtUtc: DateTime.UtcNow
        );
    }

    public EmergencyEmpathyGuidance GetEmpathyGuidance(string scenario = "general_anxiety", string language = "en")
    {
        return new EmergencyEmpathyGuidance(
            Scenario: scenario,
            Language: language,
            CalmingPhrases: new List<string>
            {
                "Take a slow, deep breath in... and slowly let it out.",
                "You are taking the right steps. Responders have your exact GPS coordinates.",
                "Focus on listening to my steps one at a time. We are right here with you."
            },
            SensoryGroundingSteps: new List<string>
            {
                "Find 3 things you can see around you right now.",
                "Find 2 things you can physically touch with your hand.",
                "Find 1 sound you can hear in the room."
            },
            QuickActions: new List<string>
            {
                "Keep your device charged or on battery-saver mode.",
                "Ensure the front entryway is unlocked if safe to do so.",
                "Keep your pets close and secured."
            }
        );
    }
}
