using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MVCMediaShareAppNew.Services;

public interface IQueueServiceFactory
{
    IQueueService GetQueueService(string provider);
}

public class QueueServiceFactory : IQueueServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public QueueServiceFactory(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public IQueueService GetQueueService(string provider)
    {
        return provider switch
        {
            "ServiceBus" => _serviceProvider.GetService<SBQueueService>()
                            ?? throw new InvalidOperationException("ServiceBusQueueService not registered"),
            "Storage" => _serviceProvider.GetService<QueueService>()
                         ?? throw new InvalidOperationException("StorageQueueService not registered"),
            _ => throw new ArgumentException($"Unknown queue service provider: {provider}")
        };
    }
}