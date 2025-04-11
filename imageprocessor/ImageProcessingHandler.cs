using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ImageProcessor.Models;
using ImageProcessor.Services;

namespace ImageProcessor
{
    public class ImageProcessingHandler
    {
        private readonly ILogger<ImageProcessingHandler> _logger;
        private readonly ICosmosDbService _cosmosDbService;

        public ImageProcessingHandler(
            ILogger<ImageProcessingHandler> logger,
            ICosmosDbService cosmosDbService)
        {
            _logger = logger;
            _cosmosDbService = cosmosDbService;
        }

        [Function(nameof(ImageProcessingHandler))]
        public async Task Run([QueueTrigger("%QueueName%", Connection = "StorageConnection")] string message)
        {
            try
            {
                _logger.LogInformation($"C# ServiceBus queue trigger function processed message: {message}");

                // Deserialize the message to MediaStoreItem
                var mediaStoreItem = JsonSerializer.Deserialize<MediaStoreItem>(message);
                if (mediaStoreItem == null)
                {
                    _logger.LogError("Failed to deserialize message to MediaStoreItem");
                    return;
                }

                // Store the item in Cosmos DB
                var storedItem = await _cosmosDbService.AddMediaStoreItem(mediaStoreItem);
                _logger.LogInformation($"Successfully stored MediaStoreItem with ID: {storedItem.id}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing message: {ex.Message}");
                throw;
            }
        }
    }
}
