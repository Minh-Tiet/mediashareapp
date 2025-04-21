using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ImageProcessor
{
    public class ImageProcessor
    {
        private readonly ILogger<ImageProcessor> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public ImageProcessor(ILogger<ImageProcessor> logger, BlobServiceClient blobServiceClient)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }

        [Function("ImageResizeProcessor")]
        public async Task Run(
            [ServiceBusTrigger("resize-queue", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
            [ServiceBus("watermark-queue", Connection = "ServiceBusConnection")] IAsyncCollector<ServiceBusMessage> watermarkMessages)
        {
            _logger.LogInformation("Received message: {MessageId} from resize-queue", message.MessageId);
            var imageData = JsonSerializer.Deserialize<dynamic>(message.Body.ToString());

            string blobUrl = imageData.MediaStorageBlobUrlWithSas;
            // Resize logic (e.g., using SixLabors.ImageSharp)
            string resizedBlobUrl = blobUrl; // Replace with actual resized URL

            var nextMessageData = new
            {
                id = imageData.id,
                OriginMediaName = imageData.OriginMediaName,
                MediaStorageBlobName = imageData.MediaStorageBlobName,
                MediaStorageBlobUrlWithSas = resizedBlobUrl,
                MediaType = imageData.MediaType,
                AuthorId = imageData.AuthorId,
                CreatedAt = imageData.CreatedAt
            };
            var nextMessage = new ServiceBusMessage(JsonSerializer.Serialize(nextMessageData));
            await watermarkMessages.AddAsync(nextMessage);
            _logger.LogInformation("Sent resized image to watermark-queue");
        }
    }
}
