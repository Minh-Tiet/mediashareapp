using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;
using MVCMediaShareAppNew.CustomAttributes;
using MVCMediaShareAppNew.Enums;
using MVCMediaShareAppNew.Models;
using MVCMediaShareAppNew.Models.ViewModels;
using MVCMediaShareAppNew.Services;
using System.Security.Claims;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MVCMediaShareAppNew.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [RestrictAccess("Image APIs are under maintenance until further notice.")]
    public class ImagesController : ControllerBase
    {
        private readonly ICosmosDbService _cosmosDbService;
        private readonly ILogger<ImagesController> _logger;
        private readonly FeatureManager _featureManager;
        private readonly BlobStorageService _blobStorageService;

        public ImagesController(ICosmosDbService cosmosDbService, 
            ILogger<ImagesController> logger, 
            BlobStorageService blobStorageService, 
            FeatureManager featureManager)
        {
            _cosmosDbService = cosmosDbService;
            _logger = logger;
            _blobStorageService = blobStorageService;
            _featureManager = featureManager;
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetImagesByUserId(string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    return BadRequest("UserId is required");
                }

                var blogPosts = await _cosmosDbService.GetAllBlogPostsByAuthorAsync(userId);
                var ifNoSas = await _featureManager.IsEnabledAsync(Enum.GetName(ConfigFeatureFlags.StoreBlobItemWithSas)) == false;
                var images = blogPosts
                    .OrderByDescending(post => post.CreatedAt)
                    .ToList()
                    .Where(post => !string.IsNullOrEmpty(post.MediaBlobUrl) && post.MediaType?.StartsWith("image/") == true)
                    .Select(async post => new UserImage
                    {
                        Id = post.id,
                        UserId = post.AuthorId ?? userId,
                        UserName = post.AuthorName ?? "Anonymous",
                        ImageUrlWithSas = ifNoSas ? (await _blobStorageService.BuildSasTokenFromBlobAsync(post.MediaBlobName)).ToString() : post.MediaBlobUrl, // If no Sas associated, gen new Sas for each blob url
                        CreatedAt = post.CreatedAt
                    });
                var response = await Task.WhenAll(images);

                return Ok(response.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user images for userId: {UserId}", userId);
                return StatusCode(500, "An error occurred while retrieving user images");
            }
        }

        // GET: api/<ImagesController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<ImagesController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<ImagesController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<ImagesController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<ImagesController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
