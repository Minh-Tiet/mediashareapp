using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using MVCMediaShareAppNew.Models;
using System.Text.Json;

namespace MVCMediaShareAppNew.Services
{
    public class QueueService : IQueueService
    {
        private  QueueClient _queueClient;
        private readonly ILogger<QueueService> _logger;
        private readonly string _queueName;
        private readonly string _connectionString;

        public QueueService(IOptions<AzureStorageSettings> settings, ILogger<QueueService> logger)
        {
            _logger = logger;

            // Initialize QueueClient  
            QueueClientOptions queueClientOptions = new QueueClientOptions
            {
                MessageEncoding = QueueMessageEncoding.Base64
            };
            _queueName = settings.Value.QueueName;
            _connectionString = settings.Value.ConnectionString;

            _logger.LogInformation($"Initialize queue service - Connection string: {_connectionString}");
            _logger.LogInformation($"Initialize queue service - Queue name: {_queueName}");
            _queueClient = new QueueClient(_connectionString, _queueName, queueClientOptions);
            _queueClient.CreateIfNotExists();
        }

        public async Task SendMessageAsync(string message)
        {
            try
            {
                await _queueClient.SendMessageAsync(message);
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