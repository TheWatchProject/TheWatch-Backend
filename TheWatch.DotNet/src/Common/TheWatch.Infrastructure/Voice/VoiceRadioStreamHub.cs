using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Voice;

/// <summary>
/// SignalR Real-Time WebSockets Hub for Push-To-Talk (PTT) field radio audio streaming.
/// </summary>
public class VoiceRadioStreamHub : Hub
{
    private readonly ILogger<VoiceRadioStreamHub> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VoiceRadioStreamHub"/> class.
    /// </summary>
    /// <param name="logger">The logging service.</param>
    public VoiceRadioStreamHub(ILogger<VoiceRadioStreamHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Joins a specific emergency channel or incident tactical radio talkgroup.
    /// </summary>
    /// <param name="talkgroupId">The tactical talkgroup identifier.</param>
    public async Task JoinTalkgroup(string talkgroupId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, talkgroupId);
        _logger.LogInformation("Connection {ConnectionId} joined tactical talkgroup {Talkgroup}", Context.ConnectionId, talkgroupId);
    }

    /// <summary>
    /// Broadcasts an audio chunk packet to all responders listening on the talkgroup.
    /// </summary>
    /// <param name="talkgroupId">The target talkgroup.</param>
    /// <param name="audioChunkBase64">Opus or PCM audio bytes encoded in Base64.</param>
    public async Task BroadcastVoiceChunk(string talkgroupId, string audioChunkBase64)
    {
        await Clients.OthersInGroup(talkgroupId).SendAsync("ReceiveVoiceChunk", Context.ConnectionId, audioChunkBase64);
    }
}
