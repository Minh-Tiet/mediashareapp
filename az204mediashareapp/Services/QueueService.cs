using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using MVCMediaShareAppNew.Models.SettingsModels;
using System.Text.Json;

namespace MVCMediaShareAppNew.Services
{
    public class QueueService : IQueueService
    {
        private readonly ILogger<QueueService> _logger;
        private readonly string _connectionString;

        public QueueService(IOptions<AzureStorageSettings> settings, ILogger<QueueService> logger)
        {
            _logger = logger;

            //_queueName = settings.Value.QueueName;
            _connectionString = settings.Value.ConnectionString;

            _logger.LogInformation($"Initialize queue service - Connection string: {_connectionString}");
        }

        public async Task SendMessageAsync(string message, string queueName)
        {
            try
            {
                QueueClientOptions queueClientOptions = new QueueClientOptions
                {
                    MessageEncoding = QueueMessageEncoding.Base64
                };
                var queueClient = new QueueClient(_connectionString, queueName, queueClientOptions);
                await queueClient.SendMessageAsync(message);
                _logger.LogInformation("Message sent to queue successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to queue");
                throw;
            }
        }
    }
} 