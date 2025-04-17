using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogNotificationProcessor
{
    public class CommentNotificationProcessor
    {
        private readonly ILogger<BlogNotificationProcessor> _logger;

        public CommentNotificationProcessor(ILogger<BlogNotificationProcessor> logger)
        {
            _logger = logger;
        }

        [Function(nameof(CommentNotificationProcessor))]
        public void Run([EventGridTrigger] EventGridEvent eventGridEvent)
        {
            _logger.LogInformation("Event received: Subject: {Subject}, EventType: {EventType}, Data: {Data}",
                eventGridEvent.Subject,
                eventGridEvent.EventType,
                eventGridEvent.Data.ToString());
        }
    }
}
