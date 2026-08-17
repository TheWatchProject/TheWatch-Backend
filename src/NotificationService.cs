// <copyright file="NotificationService.cs" company="The Watch, LLC">
// Copyright (c) 2026 Barton Milnor Mallory, The Watch, LLC. All rights reserved.
// </copyright>

/// <summary>
/// Source file: src/Services/NotificationService.cs
/// Module: Enterprise Microservices, BFF Gateway & Tactical Dispatch
/// Defines: interface INotificationService, class NotificationService
/// Namespace: TheWatch.Services
/// </summary>
using System.Threading.Tasks;

namespace TheWatch.Services
{
    public interface INotificationService
    {
        Task SendPushNotificationAsync(string userId, string title, string body);
        Task SendEmailAsync(string toEmail, string subject, string body);
    }

    public class NotificationService : INotificationService
    {
        public Task SendPushNotificationAsync(string userId, string title, string body)
        {
            // Simulate sending push notification via APNs / FCM
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(string toEmail, string subject, string body)
        {
            // Simulate sending email via SendGrid
            return Task.CompletedTask;
        }
    }
}
