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
            _logger.LogInformation("Event received: Subject: {Subject}, EventType: {EventType}, Data: {Data}",
                eventGridEvent.Subject,
                eventGridEvent.EventType,
                eventGridEvent.Data.ToString());
            var eventData = eventGridEvent.Data.ToObjectFromJson<dynamic>();
            switch (eventGridEvent.EventType)
            {
                case "BlogCreation.Published":
                    _logger.LogInformation($"Processing blog post creation: {eventData?.BlogId}");
                    break;
                case "BlogPost.Liked":
                case "BlogPost.Unliked":
                    _logger.LogInformation($"Processing like action with blog id: {eventData?.BlogId}");
                    break;
                default:
                    _logger.LogWarning("Unhandled event type: {EventType}", eventGridEvent.EventType);
                    break;
            }
        }
    }
}
