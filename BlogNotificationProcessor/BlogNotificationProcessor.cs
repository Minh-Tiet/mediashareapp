// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

using System;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BlogNotificationProcessor
{
    public class BlogNotificationProcessor
    {
        private readonly ILogger<BlogNotificationProcessor> _logger;

        public BlogNotificationProcessor(ILogger<BlogNotificationProcessor> logger)
        {
            _logger = logger;
        }

        [Function(nameof(BlogNotificationProcessor))]
        public void Run([EventGridTrigger] EventGridEvent eventGridEvent)
        {
            _logger.LogInformation("Event received: Subject: {Subject}, Type: {Type}, Data: {Data}",
                eventGridEvent.Subject,
                eventGridEvent.EventType,
                eventGridEvent.Data);
        }
    }
}
