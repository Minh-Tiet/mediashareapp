using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using MVCMediaShareAppNew.Models;

namespace MVCMediaShareAppNew.Services
{
    public interface ICosmosDbService
    {
        Task<IEnumerable<BlogPost>> GetAllBlogPostsAsync();
        Task<IEnumerable<BlogPost>> GetAllBlogPostsByAuthorAsync(string authorId);
        Task<IEnumerable<MediaStoreItem>> GetAllMediaItemsAsync(string userId);
        Task<BlogPost?> GetBlogPostAsync(string id, string authorId);
        Task<BlogPost?> GetBlogPostAsync(string itemId);
        Task<ItemResponse<BlogPost>> CreateBlogPostAsync(BlogPost blogPost);
        Task UpdateBlogPostAsync(BlogPost blogPost);
        Task DeleteBlogPostAsync(string id, string authorId);
        Task DeleteAllBlogPostsAsync(string userId);
        Task<List<Comment>> GetCommentsForBlogPostAsync(string blogPostId, string currentUserId);
        Task<List<UserImage>> GetUserImagesAsync(string userId);
        Task DeleteMediaStoreItem(string id, string authorId);
    }

    public class CosmosDbService : ICosmosDbService
    {
        private readonly Container _container;
        private readonly Container _mediaStoreContainer;
        private readonly ILogger<CosmosDbService> _logger;
        private readonly CosmosDbSettings _settings;

        public CosmosDbService(
            IOptions<CosmosDbSettings> settings,
            ILogger<CosmosDbService> logger,
            IConfiguration configuration)
        {
            _settings = settings.Value;
            _logger = logger;
            
            // Create the connection string with the account key from Key Vault
            var connectionString = $"AccountEndpoint={_settings.AccountEndpoint};AccountKey={_settings.AccountKey};";
            _logger.LogInformation($"Fetching Cosmos DB connection string from Key Vault: {connectionString}");
            try
            {
                var client = new CosmosClient(connectionString);
                var database = client.GetDatabase(_settings.DatabaseName);
                _container = database.GetContainer(_settings.ContainerName);
                _mediaStoreContainer = database.GetContainer(_settings.MediaStoreContainerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Cosmos DB client with connection string: {ConnectionString}", 
                    connectionString.Replace(_settings.AccountKey, "***"));
                throw;
            }
        }

        

        public async Task<IEnumerable<BlogPost>> GetAllBlogPostsAsync()
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c");
                var iterator = _container.GetItemQueryIterator<BlogPost>(query);
                var results = new List<BlogPost>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response.ToList());
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all blog posts");
                throw;
            }
        }

        public async Task<BlogPost?> GetBlogPostAsync(string id, string authorId)
        {
            try
            {
                var response = await _container.ReadItemAsync<BlogPost>(
                    id,
                    new PartitionKey(authorId));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving blog post with ID: {Id}", id);
                throw;
            }
        }

        public async Task<ItemResponse<BlogPost>> CreateBlogPostAsync(BlogPost blogPost)
        {
            try
            {
                return await _container.CreateItemAsync(
                    blogPost,
                    new PartitionKey(blogPost.AuthorId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating blog post: {Title}", blogPost.Title);
                throw;
            }
        }

        public async Task UpdateBlogPostAsync(BlogPost blogPost)
        {
            try
            {
                await _container.ReplaceItemAsync(
                    blogPost,
                    blogPost.id,
                    new PartitionKey(blogPost.AuthorId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating blog post: {Id}", blogPost.id);
                throw;
            }
        }

        public async Task DeleteBlogPostAsync(string id, string authorId)
        {
            try
            {
                await _container.DeleteItemAsync<BlogPost>(
                    id,
                    new PartitionKey(authorId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blog post: {Id}", id);
                throw;
            }
        }

        public async Task DeleteAllBlogPostsAsync(string userId)
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.AuthorId = @userId")
                .WithParameter("@userId", userId);

            var iterator = _container.GetItemQueryIterator<BlogPost>(query);
            var blogPosts = new List<BlogPost>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                blogPosts.AddRange(response);
            }

            foreach (var blogPost in blogPosts)
            {
                await _container.DeleteItemAsync<BlogPost>(blogPost.id, new PartitionKey(blogPost.AuthorId));
            }
        }

        public async Task<List<Comment>> GetCommentsForBlogPostAsync(string blogPostId, string currentUserId)
        {
            try
            {
                // Use a cross-partition query to find the blog post by ID
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
                    .WithParameter("@id", blogPostId);

                var iterator = _container.GetItemQueryIterator<BlogPost>(query, requestOptions: new QueryRequestOptions
                {
                    MaxConcurrency = -1
                });

                BlogPost? blogPost = null;
                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    blogPost = response.FirstOrDefault();
                }

                if (blogPost == null)
                {
                    return new List<Comment>();
                }

                // Filter comments to only show those from the current user
                var userComments = blogPost.Comments
                    //.Where(c => c.AuthorId == currentUserId)
                    .OrderByDescending(c => c.CreatedAt)
                    .ToList();

                return userComments;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving comments for blog post: {BlogPostId}", blogPostId);
                throw;
            }
        }

        public async Task<List<UserImage>> GetUserImagesAsync(string userId)
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.AuthorId = @userId")
                    .WithParameter("@userId", userId);

                var iterator = _container.GetItemQueryIterator<UserImage>(query);
                var results = new List<UserImage>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange([.. response]);
                }

                return [.. results.OrderByDescending(i => i.CreatedAt)];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user images for user: {UserId}", userId);
                throw;
            }
        }

        public async Task<IEnumerable<MediaStoreItem>> GetAllMediaItemsAsync(string userId)
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c Where c.AuthorId=@userId").WithParameter("@userId", userId);
                var iterator = _mediaStoreContainer.GetItemQueryIterator<MediaStoreItem>(query);
                var results = new List<MediaStoreItem>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response.ToList());
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all medias");
                throw;
            }
        }

        public async Task DeleteMediaStoreItem(string id, string authorId)
        {
            try
            {
                await _mediaStoreContainer.DeleteItemAsync<MediaStoreItem>(
                    id,
                    new PartitionKey(authorId));
                _logger.LogInformation("Media store item deleted successfully: {Id}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting media store item: {Id}", id);
                throw;
            }
        }

        public async Task<IEnumerable<BlogPost>> GetAllBlogPostsByAuthorAsync(string authorId)
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c Where c.AuthorId=@userId").WithParameter("@userId", authorId);
                var iterator = _container.GetItemQueryIterator<BlogPost>(query);
                var results = new List<BlogPost>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response.ToList());
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all blog posts");
                throw;
            }
        }

        public async Task<BlogPost?> GetBlogPostAsync(string itemId)
        {
            try
            {
                string query = $"SELECT * FROM c WHERE c.id = @itemId";
                QueryDefinition queryDefinition = new QueryDefinition(query)
                    .WithParameter("@itemId", itemId);

                using FeedIterator<BlogPost> feedIterator = _container.GetItemQueryIterator<BlogPost>(queryDefinition);
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<BlogPost> response = await feedIterator.ReadNextAsync();
                    foreach (BlogPost item in response)
                    {
                        return item;
                    }
                }
                return null;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving blog post with ID: {Id}", itemId);
                throw;
            }
        }
    }
} 