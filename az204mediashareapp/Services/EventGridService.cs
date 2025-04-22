using System;
using System.Threading.Tasks;
using Azure.Messaging.EventGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MVCMediaShareAppNew.Services
{
    public interface IEventGridService
    {
        Task SendEventAsync(EventGridEvent eventGridEvent);
    }

    public class EventGridService : IEventGridService
    {
        private readonly EventGridPublisherClient _client;
        private readonly ILogger<EventGridService> _logger;

        public EventGridService(IConfiguration configuration, ILogger<EventGridService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            var topicEndpoint = configuration["EventGrid:TopicEndpoint"];
            var topicKey = configuration["EventGrid:TopicKey"];

            if (string.IsNullOrEmpty(topicEndpoint) || string.IsNullOrEmpty(topicKey))
            {
                throw new InvalidOperationException("Event Grid topic endpoint or key is not configured.");
            }

            _client = new EventGridPublisherClient(
                new Uri(topicEndpoint),
                new Azure.AzureKeyCredential(topicKey));
        }

        public async Task SendEventAsync(EventGridEvent eventGridEvent)
        {
            if (eventGridEvent == null)
            {
                throw new ArgumentNullException(nameof(eventGridEvent));
            }

            try
            {
                _logger.LogInformation("Sending EventGrid event with subject: {Subject}", eventGridEvent.Subject);
                await _client.SendEventAsync(eventGridEvent);
                _logger.LogInformation("Successfully sent EventGrid event with subject: {Subject}", eventGridEvent.Subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending EventGrid event with subject: {Subject}", eventGridEvent.Subject);
                throw;
            }
        }
    }
}