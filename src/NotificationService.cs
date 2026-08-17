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
