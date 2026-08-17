using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TheWatch.Contracts;
using static TheWatch.Contracts.PhraseSharingAndListeningContracts;

namespace TheWatch.Infrastructure.Adapters.Audio;

public interface ISipVoipListeningAdapter
{
    void RegisterTrunk(PhoneSystemListeningConfig config);
    bool ProcessAudioBuffer(string trunkId, string spokenTranscript, out string matchedPhrase, out double confidence);
}

/// <summary>
/// Active listening adapter for Phone & VoIP PBX systems (SIP/Twilio/Asterisk).
/// Analyzes voice call audio streams without disrupting the active call.
/// </summary>
public sealed class SipVoipActiveListeningAdapter : ISipVoipListeningAdapter
{
    private readonly ConcurrentDictionary<string, PhoneSystemListeningConfig> _trunks = new();

    public void RegisterTrunk(PhoneSystemListeningConfig config)
    {
        _trunks[config.TrunkId] = config;
    }

    public bool ProcessAudioBuffer(string trunkId, string spokenTranscript, out string matchedPhrase, out double confidence)
    {
        matchedPhrase = string.Empty;
        confidence = 0.0;

        if (!_trunks.TryGetValue(trunkId, out var config) || !config.IsActiveListeningEnabled)
        {
            return false;
        }

        string normalized = spokenTranscript.ToLowerInvariant();

        foreach (var hotword in config.MonitoredHotwords)
        {
            if (normalized.Contains(hotword.ToLowerInvariant()))
            {
                matchedPhrase = hotword;
                confidence = 0.98;
                return true;
            }
        }

        return false;
    }
}

public interface ISmartSpeakerListeningAndAlarmAdapter
{
    void RegisterSpeakerZone(SpeakerSystemListeningConfig config);
    AlarmSystemTriggerCommand TriggerAlarmSiren(string zoneId, AlarmTriggerAction action, string reason);
    bool ProcessSpeakerAmbientStream(string zoneId, string transcript, out string detectedWord);
}

/// <summary>
/// Smart Speaker & Facility PA Intercom Adapter (Sonos / Matter / Google Cast / AirPlay 2).
/// Ingests ambient audio and blasts reverse emergency audio sirens.
/// </summary>
public sealed class SmartSpeakerListeningAndAlarmAdapter : ISmartSpeakerListeningAndAlarmAdapter
{
    private readonly ConcurrentDictionary<string, SpeakerSystemListeningConfig> _zones = new();

    public void RegisterSpeakerZone(SpeakerSystemListeningConfig config)
    {
        _zones[config.SpeakerZoneId] = config;
    }

    public AlarmSystemTriggerCommand TriggerAlarmSiren(string zoneId, AlarmTriggerAction action, string reason)
    {
        var commandId = $"ALARM-CMD-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        return new AlarmSystemTriggerCommand(
            commandId,
            zoneId,
            action,
            reason,
            DurationSeconds: 180,
            DateTime.UtcNow
        );
    }

    public bool ProcessSpeakerAmbientStream(string zoneId, string transcript, out string detectedWord)
    {
        detectedWord = string.Empty;
        if (!_zones.TryGetValue(zoneId, out var config) || !config.ContinuousAmbientListening)
        {
            return false;
        }

        var panicWords = new[] { "help", "emergency", "fire", "intruder", "red falcon", "active shooter" };
        string norm = transcript.ToLowerInvariant();

        foreach (var word in panicWords)
        {
            if (norm.Contains(word))
            {
                detectedWord = word;
                return true;
            }
        }

        return false;
    }
}

public interface IPeerPhraseRelayMeshAdapter
{
    PeerPhraseRelayBroadcast BroadcastToNearbyPeers(string deviceId, string userId, string phrase, double lat, double lon);
    AlarmSystemTriggerCommand DispatchToConnectedAlarmPanels(string panelId, AlarmTriggerAction action, string phrase);
}

/// <summary>
/// Peer-to-peer phrase detection sharing across nearby BLE devices and smart home alarm panels.
/// </summary>
public sealed class PeerPhraseRelayMeshAdapter : IPeerPhraseRelayMeshAdapter
{
    public PeerPhraseRelayBroadcast BroadcastToNearbyPeers(string deviceId, string userId, string phrase, double lat, double lon)
    {
        return new PeerPhraseRelayBroadcast(
            $"RELAY-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            deviceId,
            userId,
            phrase,
            ConfidenceScore: 0.95,
            lat,
            lon,
            EstimatedProximityMeters: 25.0, // Typical BLE / local Wi-Fi range
            DateTime.UtcNow
        );
    }

    public AlarmSystemTriggerCommand DispatchToConnectedAlarmPanels(string panelId, AlarmTriggerAction action, string phrase)
    {
        return new AlarmSystemTriggerCommand(
            $"ALARM-PANEL-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            panelId,
            action,
            $"Triggered by peer panic phrase: '{phrase}'",
            DurationSeconds: 120,
            DateTime.UtcNow
        );
    }
}
