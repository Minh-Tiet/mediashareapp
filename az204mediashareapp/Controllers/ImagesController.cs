using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MVCMediaShareAppNew.Models;
using MVCMediaShareAppNew.Services;
using System.Security.Claims;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MVCMediaShareAppNew.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ImagesController : ControllerBase
    {
        private readonly ICosmosDbService _cosmosDbService;
        private readonly ILogger<ImagesController> _logger;

        public ImagesController(ICosmosDbService cosmosDbService, ILogger<ImagesController> logger)
        {
            _cosmosDbService = cosmosDbService;
            _logger = logger;
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
                
                // Convert blog posts to UserImage models, only including posts with media
                var images = blogPosts
                    .Where(post => !string.IsNullOrEmpty(post.MediaUrl) && post.MediaType?.StartsWith("image/") == true)
                    .Select(post => new UserImage
                    {
                        Id = post.id,
                        UserId = post.AuthorId ?? userId,
                        UserName = post.AuthorName ?? "Anonymous",
                        ImageUrlWithSas = post.MediaUrl,
                        CreatedAt = post.CreatedAt
                    })
                    .OrderByDescending(img => img.CreatedAt)
                    .ToList();

                return Ok(images);
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
