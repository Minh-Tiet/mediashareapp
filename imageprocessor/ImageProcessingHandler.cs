using System;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ImageProcessor
{
    public class ImageProcessingHandler
    {
        private readonly ILogger<ImageProcessingHandler> _logger;

        public ImageProcessingHandler(ILogger<ImageProcessingHandler> logger)
        {
            _logger = logger;
        }

        [Function(nameof(ImageProcessingHandler))]
        public void Run([QueueTrigger("%QueueName%", Connection = "StorageConnection")] string message)
        {
            _logger.LogInformation($"Simple processing: {message}");
        }
    }
}
