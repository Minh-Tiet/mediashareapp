using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using ImageProcessor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System;

namespace ImageProcessor.Services
{
    public interface ICosmosDbService
    {
        Task<MediaStoreItem> AddMediaStoreItem(MediaStoreItem newItem);
    }

    public class CosmosDbService : ICosmosDbService
    {
        private readonly ILogger<CosmosDbService> _logger;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _container;
        private readonly CosmosDbSettings _cosmosDbSettings;

        public CosmosDbService(
            IOptions<CosmosDbSettings> settings,
            ILogger<CosmosDbService> logger)
        {
            _cosmosDbSettings = settings.Value;
            _logger = logger;

            _cosmosClient = new CosmosClient(
                _cosmosDbSettings.AccountEndpoint,
                _cosmosDbSettings.AccountKey,
                new CosmosClientOptions
                {
                    ApplicationName = "ImageProcessor"
                });
            _container = _cosmosClient.GetContainer(_cosmosDbSettings.DatabaseName, _cosmosDbSettings.ContainerName);
        }

        public async Task<MediaStoreItem> AddMediaStoreItem(MediaStoreItem newItem)
        {
            try
            {
                var response = await _container.CreateItemAsync(newItem, new PartitionKey(newItem.AuthorId));
                _logger.LogInformation($"Item created with id: {response.Resource.id}");

                return response.Resource;
            }
            catch (CosmosException ex)
            {
                _logger.LogError($"Cosmos DB error: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"General error: {ex.Message}");
                throw;
            }
        }
    }
} 