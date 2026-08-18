// <copyright file="NotificationService.cs" company="The Watch, LLC">
// Copyright (c) 2026 Barton Milnor Mallory, The Watch, LLC. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TheWatch.Services;

public class NotificationDispatchRequest
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string TargetH3Index { get; set; } = string.Empty;
    public int TargetKRing { get; set; } = 1;
    public string Priority { get; set; } = "HIGH"; // HIGH, CRITICAL, STANDARD
    public Dictionary<string, string> DataPayload { get; set; } = new();
}

public interface INotificationService
{
    Task SendPushNotificationAsync(string userId, string title, string body);
    Task SendEmailAsync(string toEmail, string subject, string body);
    Task<int> DispatchH3GeofenceAlertAsync(NotificationDispatchRequest request);
}

public class NotificationService : INotificationService
{
    private readonly IH3BackendResponderCache _h3Cache;

    public NotificationService(IH3BackendResponderCache? h3Cache = null)
    {
        _h3Cache = h3Cache ?? new H3BackendResponderCache();
    }

    public Task SendPushNotificationAsync(string userId, string title, string body)
    {
        // Backend APNs / FCM push dispatch
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(string toEmail, string subject, string body)
    {
        // Backend SendGrid / ACS Email dispatch
        return Task.CompletedTask;
    }

    public async Task<int> DispatchH3GeofenceAlertAsync(NotificationDispatchRequest request)
    {
        if (string.IsNullOrEmpty(request.TargetH3Index)) return 0;

        var cells = _h3Cache.GetKRingHexagons(request.TargetH3Index, request.TargetKRing);
        // Simulates broadcast to all subscriber devices within target hexagons
        int dispatchedCount = cells.Count * 4;
        await Task.CompletedTask;
        return dispatchedCount;
    }
}
