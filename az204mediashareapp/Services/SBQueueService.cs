using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using MVCMediaShareAppNew.Models.SettingsModels;

namespace MVCMediaShareAppNew.Services
{
    public class SBQueueService : IQueueService
    {
        private readonly ServiceBusClient _client;
        private readonly ILogger<SBQueueService> _logger;

        public SBQueueService(IOptions<ServiceBusSettings> settings, ILogger<SBQueueService> logger)
        {
            _client = new ServiceBusClient(settings.Value.ConnectionString);
            _logger = logger;
        }

        public async Task SendMessageAsync(string message, string queueName)
        {
            try
            {
                var sender = _client.CreateSender(queueName);
                var serviceBusMessage = new ServiceBusMessage(message);
                await sender.SendMessageAsync(serviceBusMessage);
                _logger.LogInformation("Sent message to Service Bus queue: {QueueName}", queueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to Service Bus queue: {QueueName}", queueName);
                throw;
            }
        }
    }
}
