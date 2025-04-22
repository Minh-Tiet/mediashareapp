using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ImageProcessor
{
    public class ImageResizeProcessor
    {
        private readonly ILogger<ImageResizeProcessor> _logger;
        private readonly IConfiguration _configuration;
        private readonly ServiceBusClient _serviceBusClient;

        public ImageResizeProcessor(ILogger<ImageResizeProcessor> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceBusClient = new ServiceBusClient(_configuration["ServiceBusConnection"]);
        }

        [Function(nameof(ImageResizeProcessor))]
        [ServiceBusOutput("watermark-queue", Connection = "ServiceBusConnection")]
        public async Task<string> Run(
            [ServiceBusTrigger("resize-queue", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message)
        {
            _logger.LogInformation("Received message: {MessageId} from resize-queue", message.MessageId);
            try
            {


                // Deserialize the message to dynamic object
                JsonElement imageData = JsonSerializer.Deserialize<JsonElement>(message.Body.ToString());

                string? blobId = imageData.TryGetProperty("id", out JsonElement blobIdElement) ? blobIdElement.GetString() : null;
                string? blobUrl = imageData.TryGetProperty("MediaStorageBlobUrlWithSas", out JsonElement blobUrlElement) ? blobUrlElement.GetString() : null;
                string? blobType = imageData.TryGetProperty("MediaType", out JsonElement mediaTypeElement) ? mediaTypeElement.GetString() : null;
                string? authorId = imageData.TryGetProperty("AuthorId", out JsonElement authorIdElement) ? authorIdElement.GetString() : null;
                string? createdAt = imageData.TryGetProperty("CreatedAt", out JsonElement createdAtElement) ? createdAtElement.GetString() : null;

                // Resize logic (e.g., using SixLabors.ImageSharp)
                var nextMessageData = new
                {
                    id = blobId,
                    MediaStorageBlobUrlWithSas = blobUrl,
                    MediaType = blobType,
                    AuthorId = authorId,
                    CreatedAt = createdAt
                };
                _logger.LogInformation($"MediaStorageBlogUrlWithSas: {blobUrl} \n MediaType: {blobType} \n Author: {authorId} \n Created at: {createdAt}");
                _logger.LogInformation("Sent resized image to watermark-queue");
                return JsonSerializer.Serialize(nextMessageData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing message: {ex.Message}");
                await _serviceBusClient.CreateReceiver("resize-queue").CompleteMessageAsync(message);

                throw;
            }
            
        }
    }
}
