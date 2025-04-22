using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using MVCMediaShareAppNew.Models;
using StackExchange.Redis;
using System.Web;

namespace MVCMediaShareAppNew.Services
{
    public interface IBlobStorageService
    {
        Task<string> UploadFileAsync(IFormFile file, string fileName);
        string GetBlobUrlAsync(string blobName);
        Task DeleteBlobAsync(string blobName);
        string GetSasToken();
    }

    public class BlobStorageService : IBlobStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<BlobStorageService> _logger;
        private readonly AzureStorageSettings _settings;
        private readonly IDatabase _redisDb;
        private readonly string _sasTokenCacheKey;
        private readonly string _containerName;
        private readonly int _containerSasExpiredInDays;

        public BlobStorageService(
            IOptions<AzureStorageSettings> settings,
            ILogger<BlobStorageService> logger,
            IConnectionMultiplexer redis,
            IConfiguration configuration)
        {
            _logger = logger;
            _settings = settings.Value;
            var connectionStringSecret = _settings.ConnectionString;
            _logger.LogInformation($"Fetching Azure Storage connection string from Key Vault: {connectionStringSecret}");
            if (string.IsNullOrEmpty(connectionStringSecret))
            {
                throw new InvalidOperationException("Azure Storage connection string is not configured in Key Vault");
            }

            try
            {
                _blobServiceClient = new BlobServiceClient(connectionStringSecret);
                _containerClient = _blobServiceClient.GetBlobContainerClient(_settings.ContainerName);
                _redisDb = redis.GetDatabase();
                _containerName = configuration["AzureStorage:ContainerName"] ?? "";
                _sasTokenCacheKey = configuration["Redis:BlobContainerSasTokenKey"] ?? "";
                _containerSasExpiredInDays = int.Parse(configuration["AzureStorage:ContainerSasExpiredInDays"] ?? "7");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating BlobServiceClient with connection string: {ConnectionString}", 
                    connectionStringSecret.Replace(connectionStringSecret, "***"));
                throw;
            }

            // Create the container if it doesn't exist
            _containerClient.CreateIfNotExists(PublicAccessType.None);
        }

        /// <summary>
        /// Uploads a file to Azure Blob Storage.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public async Task<string> UploadFileAsync(IFormFile file, string fileName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var blobClient = containerClient.GetBlobClient(fileName);

                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });
                }

                _logger.LogInformation("File uploaded successfully to blob storage: {BlobName}", fileName);
                return blobClient.Uri.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file to blob storage: {FileName}", fileName);
                throw;
            }
        }

        public string GetBlobUrlAsync(string blobName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var blobClient = containerClient.GetBlobClient(blobName);
                return blobClient.Uri.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting blob URL: {BlobName}", blobName);
                throw;
            }
        }

        public string GetSasToken()
        {
            try
            {
                // Check if the SAS token exists in Redis
                string? cachedSasToken = _redisDb.StringGet(_sasTokenCacheKey);
                if (!string.IsNullOrEmpty(cachedSasToken))
                {
                    // Parse the expiry time from the SAS token
                    var queryParams = HttpUtility.ParseQueryString(cachedSasToken);
                    string? expiryString = queryParams["se"];
                    if (!string.IsNullOrEmpty(expiryString) &&
                        DateTimeOffset.TryParse(expiryString, out DateTimeOffset expiryTime))
                    {
                        // Add a small buffer (e.g., 5 minutes) to avoid using a token too close to expiry
                        if (expiryTime > DateTimeOffset.UtcNow.AddMinutes(5))
                        {
                            _logger.LogInformation("Retrieved valid SAS token from Redis cache, expires at {Expiry}", expiryTime);
                            return cachedSasToken;
                        }
                        else
                        {
                            _logger.LogInformation("Cached SAS token has expired or is too close to expiry: {Expiry}", expiryTime);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to parse expiry from cached SAS token: {SasToken}", cachedSasToken);
                    }
                }

                // Generate a new SAS token if not in cache or expired
                BlobContainerSasPermissions permissions = BlobContainerSasPermissions.Read;
                DateTimeOffset expiryTimeNew = DateTimeOffset.UtcNow.AddDays(_containerSasExpiredInDays);

                string sasToken = _containerClient.GenerateSasUri(
                    permissions: permissions,
                    expiresOn: expiryTimeNew
                ).Query;

                // Store in Redis with TTL matching the expiry
                TimeSpan ttl = expiryTimeNew - DateTimeOffset.UtcNow;
                _redisDb.StringSet(_sasTokenCacheKey, sasToken, ttl);

                _logger.LogInformation("Generated and cached new SAS token for container {ContainerName} with expiry {Expiry}",
                    _settings.ContainerName, expiryTimeNew);

                return sasToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating or caching SAS token for container {ContainerName}", _settings.ContainerName);
                throw;
            }
        }

        public async Task DeleteBlobAsync(string blobName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var blobClient = containerClient.GetBlobClient(blobName);
                await blobClient.DeleteIfExistsAsync();
                _logger.LogInformation("Blob deleted successfully: {BlobName}", blobName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blob: {BlobName}", blobName);
                throw;
            }
        }
    }
}