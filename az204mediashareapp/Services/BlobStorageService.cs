using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using MVCMediaShareAppNew.Enums;
using MVCMediaShareAppNew.Models.SettingsModels;
using StackExchange.Redis;
using System.Web;

namespace MVCMediaShareAppNew.Services
{
    public interface IBlobStorageService
    {
        Task<string> UploadFileAsync(IFormFile file, string fileName);
        Task<string> GetBlobUrlAsync(string blobName);
        Task DeleteBlobAsync(string blobName);
        string GetContainerLevelSasFromRedisCache();
        Task<Uri> BuildSasTokenFromBlobAsync(string blobNameOrBlobUrl);
        bool HasSasToken(string blobUri);
        string RemoveSasFromBlobPath(string fullBlobPath);
        string ExtractBlobNameFromUrl(string blobUrl);
        Task<string> CheckAndAssignSasToken(string blobUrl);
    }

    public class BlobStorageService : IBlobStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<BlobStorageService> _logger;
        private readonly AzureStorageSettings _settings;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly IDatabase _redisDb;
        private readonly string _sasTokenCacheKey;
        private readonly string _containerName;
        private readonly int _containerSasExpiredInDays;
        private readonly int _blobItemSasExpiredInMinutes;

        private readonly IFeatureManager _featureManager;
        private readonly string _cdnEndpoint;

        public BlobStorageService(
            IOptions<AzureStorageSettings> settings,
            ILogger<BlobStorageService> logger,
            IConnectionMultiplexer redis,
            IConfiguration configuration,
            IHostEnvironment hostEnvironment,
            IFeatureManager featureManager)
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
                _configuration = configuration;
                _hostEnvironment = hostEnvironment;
                _blobServiceClient = new BlobServiceClient(connectionStringSecret);
                _containerClient = _blobServiceClient.GetBlobContainerClient(_settings.ContainerName);
                _redisDb = redis.GetDatabase();
                _containerName = configuration["AzureStorage:ContainerName"] ?? "";
                _sasTokenCacheKey = configuration["Redis:BlobContainerSasTokenKey"] ?? "";
                _containerSasExpiredInDays = int.Parse(configuration["AzureStorage:ContainerSasExpiredInDays"] ?? "7");
                _blobItemSasExpiredInMinutes = int.Parse(configuration["AzureStorage:BlobItemSasExpiredInMinutes"] ?? "1");
                _featureManager = featureManager;
                _cdnEndpoint = configuration["AzureStorage:CdnEndpoint"] ?? "";
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
                return (await UpdateCdnHostBlobUri(blobClient.Uri)).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file to blob storage: {FileName}", fileName);
                throw;
            }
        }

        public async Task<string> GetBlobUrlAsync(string blobName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var blobClient = containerClient.GetBlobClient(blobName);
                return (await UpdateCdnHostBlobUri(blobClient.Uri)).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting blob URL: {BlobName}", blobName);
                throw;
            }
        }

        public string GetContainerLevelSasFromRedisCache()
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blobNameOrBlobUrl"></param>
        /// <returns></returns>
        public Task<Uri> BuildSasTokenFromBlobAsync(string blobNameOrBlobUrl)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                Uri blobUri = new Uri(blobNameOrBlobUrl);
                BlobUriBuilder blobUriBuilder = new BlobUriBuilder(blobUri);
                string blobName = blobUriBuilder.BlobName;
                var blobClient = containerClient.GetBlobClient(blobName);
                // Generate a SAS token for the blob
                BlobSasBuilder sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = _containerName,
                    BlobName = blobName,
                    Resource = "b",
                    ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_blobItemSasExpiredInMinutes)
                };
                sasBuilder.SetPermissions(BlobSasPermissions.Read);
                Uri sasToken = blobClient.GenerateSasUri(sasBuilder);
                return Task.FromResult(sasToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating SAS token for blob: {BlobName}", blobNameOrBlobUrl);
                throw;
            }
        }

        /// <summary>
        /// Update the blob URI to use the Front Door domain if caching is enabled.
        /// </summary>
        /// <param name="blobUri"></param>
        /// <returns></returns>
        async Task<Uri> UpdateCdnHostBlobUri(Uri blobUri)
        {
            if (!_hostEnvironment.IsDevelopment())
            {
                var enableFrontDoorCaching = await _featureManager.IsEnabledAsync(Enum.GetName(ConfigFeatureFlags.EnableFrontDoorCaching));
                if (enableFrontDoorCaching)
                {
                    var uriBuilder = new UriBuilder(blobUri)
                    {
                        Host = _cdnEndpoint // Replace with your Front Door domain
                    };
                    return uriBuilder.Uri;
                }
                else
                {
                    return blobUri;
                }
            }
            else
            {
                // In development mode, return the original blob URI
                return blobUri;
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

        public bool HasSasToken(string blobUri)
        {
            if (string.IsNullOrEmpty(blobUri))
                return false;

            try
            {
                var uri = new Uri(blobUri);
                // Check if the URI has a query string
                if (string.IsNullOrEmpty(uri.Query))
                    return false;

                // Common SAS token parameters: sig (signature), st (start time), se (expiry time), sp (permissions), etc.
                var queryParameters = System.Web.HttpUtility.ParseQueryString(uri.Query);
                return queryParameters.AllKeys.Any(key => key == "sig" || key == "st" || key == "se" || key == "sp");
            }
            catch (UriFormatException)
            {
                // Handle invalid URI
                return false;
            }
        }

        public string RemoveSasFromBlobPath(string blobUri)
        {
            if (string.IsNullOrEmpty(blobUri))
                return null;

            try
            {
                var uriBuilder = new UriBuilder(blobUri);
                uriBuilder.Query = string.Empty; // Remove query string
                return uriBuilder.Uri.ToString();
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        public string ExtractBlobNameFromUrl(string blobUrl)
        {
            if (string.IsNullOrEmpty(blobUrl))
                return null;

            try
            {
                var blobUriBuilder = new BlobUriBuilder(new Uri(blobUrl));
                // BlobName includes the blob name, SAS token is ignored
                return blobUriBuilder.BlobName;
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Check if the blob URL has a SAS token. If not, generate a new one.
        /// </summary>
        /// <param name="blobUrl"></param>
        /// <returns></returns>
        public async Task<string> CheckAndAssignSasToken(string blobUrl)
        {
            var storeSasFeatureEnabled = await _featureManager.IsEnabledAsync(Enum.GetName(ConfigFeatureFlags.StoreBlobItemWithSas));

            if (storeSasFeatureEnabled)
            {
                if (!string.IsNullOrEmpty(blobUrl) && !HasSasToken(blobUrl))
                {
                    var sasTokenFromCache = GetContainerLevelSasFromRedisCache();
                    var newBlobUri = new Uri($"{blobUrl}{sasTokenFromCache}");
                    return (await UpdateCdnHostBlobUri(newBlobUri)).ToString();
                }
                else
                {
                    return blobUrl;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(blobUrl) && !HasSasToken(blobUrl))
                {
                    // If the blob URL does not have a SAS token, generate a new one
                    var newBlobUriWithSas = await BuildSasTokenFromBlobAsync(blobUrl);
                    return (await UpdateCdnHostBlobUri(newBlobUriWithSas)).ToString();
                }
                else
                {
                    return (await UpdateCdnHostBlobUri(new Uri(blobUrl))).ToString();
                }
            }
        }
    }
}