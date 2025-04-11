using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MVCMediaShareAppNew.Models;
using MVCMediaShareAppNew.Services;
using Microsoft.AspNetCore.Http;
using System.IO;
using System;
using System.Text.Json;
using System.Linq;
using Microsoft.Azure.Cosmos;

namespace MVCMediaShareAppNew.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ICosmosDbService _cosmosDbService;
        private readonly IBlobStorageService _blobStorageService;
        private readonly IQueueService _queueService;

        public HomeController(ILogger<HomeController> logger,
            ICosmosDbService cosmosDbService,
            IBlobStorageService blobStorageService,
            IQueueService queueService)
        {
            _logger = logger;
            _cosmosDbService = cosmosDbService;
            _blobStorageService = blobStorageService;
            _queueService = queueService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                var posts = await _cosmosDbService.GetAllBlogPostsAsync(userId);

                // Load comments for each post
                foreach (var post in posts)
                {
                    post.Comments = await _cosmosDbService.GetCommentsForBlogPostAsync(post.id, userId);
                }

                // Sort posts by CreatedAt in descending order
                posts = [.. posts.OrderByDescending(p => p.CreatedAt)];

                return View(posts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading blog posts");
                ModelState.AddModelError("", "Error loading blog posts. Please try again later.");
                return View(new List<BlogPost>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePost([Bind("Title,Content")] BlogPost post, IFormFile? mediaFile, string? selectedImageUrl)
        {
            if (string.IsNullOrEmpty(post.Title))
            {
                ModelState.AddModelError("Title", "Title is required");
            }
            else if (post.Title.Length < 3 || post.Title.Length > 100)
            {
                ModelState.AddModelError("Title", "Title must be between 3 and 100 characters");
            }

            if (string.IsNullOrEmpty(post.Content))
            {
                ModelState.AddModelError("Content", "Content is required");
            }
            else if (post.Content.Length < 10)
            {
                ModelState.AddModelError("Content", "Content must be at least 10 characters long");
            }

            if (mediaFile != null)
            {
                // Validate file size (100MB limit)
                const long maxFileSize = 104857600; // 100MB in bytes
                if (mediaFile.Length > maxFileSize)
                {
                    ModelState.AddModelError("mediaFile", "File size must be less than 100MB");
                }

                // Validate file type
                if (!mediaFile.ContentType.StartsWith("image/") && !mediaFile.ContentType.StartsWith("video/"))
                {
                    ModelState.AddModelError("mediaFile", "Only image and video files are allowed");
                }
            }

            try
            {
                post.id = Guid.NewGuid().ToString();
                post.AuthorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
                post.AuthorName = User.Identity?.Name ?? "anoymous";
                post.CreatedAt = DateTime.UtcNow;

                if (mediaFile != null)
                {
                    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(mediaFile.FileName)}";
                    string baseMediaUrl = await _blobStorageService.UploadFileAsync(mediaFile, fileName);
                    post.MediaType = mediaFile.ContentType;

                    // Generate container-level SAS token and append it to the base URL
                    string sasToken = _blobStorageService.GetSasToken();
                    post.MediaUrl = $"{baseMediaUrl}{sasToken}"; // Append SAS token to the URL
                }
                else if (!string.IsNullOrEmpty(selectedImageUrl))
                {
                    // Use the selected image from the sidebar
                    post.MediaUrl = selectedImageUrl;
                    post.MediaType = "image/jpeg"; // Default to JPEG, adjust if needed
                }

                var blogCreationTask = await _cosmosDbService.CreateBlogPostAsync(post);
                var createdBlogPost = blogCreationTask.Resource; // Retrieve the BlogPost object
                
                // Send message to queue for image processing
                if (!string.IsNullOrEmpty(post.MediaUrl))
                {
                    var message = new
                    {
                        BlogPostId = post.id,
                        AuthorId = post.AuthorId,
                        MediaUrl = post.MediaUrl,
                        MediaType = post.MediaType
                    };
                    await _queueService.SendMessageAsync(JsonSerializer.Serialize(message));
                }
                
                _logger.LogInformation("Blog post created successfully: {Title}", post.Title);

                // Check if this is an AJAX request (from sidebar upload)
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true, imageUrl = post.MediaUrl });
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating blog post");
                ModelState.AddModelError("", "Error creating blog post. Please try again later.");

                // Check if this is an AJAX request (from sidebar upload)
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Error creating blog post. Please try again later." });
                }
            }

            // If we got this far, something failed; redisplay form
            var posts = await _cosmosDbService.GetAllBlogPostsAsync(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "");
            return View(nameof(Index), posts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAllBlogs()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
                await _cosmosDbService.DeleteAllBlogPostsAsync(userId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting all blogs");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePost(string id)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
                await _cosmosDbService.DeleteBlogPostAsync(id, userId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blog post with id: {Id}", id);
                return Json(new { success = false, message = "Error deleting blog post. Please try again later." });
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddComment(string blogPostId, string text, IFormFile mediaFile, string? selectedImageUrl)
        {
            try
            {
                var authorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
                if (string.IsNullOrEmpty(authorId))
                {
                    return Json(new { success = false, error = "User not authenticated" });
                }

                // Get the blog post
                var blogPost = await _cosmosDbService.GetBlogPostAsync(blogPostId, authorId);
                if (blogPost == null)
                {
                    return Json(new { success = false, error = "Blog post not found" });
                }

                // Create new comment
                var comment = new Comment
                {
                    Id = Guid.NewGuid().ToString(),
                    Text = text,
                    CreatedAt = DateTime.UtcNow,
                    AuthorId = authorId,
                    AuthorName = User.Identity?.Name,
                    BlogPostId = blogPostId
                };

                if (mediaFile != null && mediaFile.Length > 0)
                {
                    // Validate file size (100MB limit)
                    const long maxFileSize = 104857600; // 100MB in bytes
                    if (mediaFile.Length > maxFileSize)
                    {
                        return Json(new { success = false, error = "File size must be less than 100MB" });
                    }

                    // Validate file type
                    if (!mediaFile.ContentType.StartsWith("image/") && !mediaFile.ContentType.StartsWith("video/"))
                    {
                        return Json(new { success = false, error = "Only image and video files are allowed" });
                    }

                    var fileName = $"comments/{Guid.NewGuid()}{Path.GetExtension(mediaFile.FileName)}";
                    string baseMediaUrl = await _blobStorageService.UploadFileAsync(mediaFile, fileName);
                    comment.MediaType = mediaFile.ContentType;

                    // Generate container-level SAS token and append it to the base URL
                    string sasToken = _blobStorageService.GetSasToken();
                    comment.MediaUrl = $"{baseMediaUrl}{sasToken}"; // Append SAS token to the URL
                }
                else if (!string.IsNullOrEmpty(selectedImageUrl))
                {
                    // Use the selected image from the sidebar
                    comment.MediaUrl = selectedImageUrl;
                    comment.MediaType = "image/jpeg"; // Default to JPEG, adjust if needed
                }

                // Add comment to blog post's comments list
                blogPost.Comments.Add(comment);

                // Update the blog post with the new comment
                await _cosmosDbService.UpdateBlogPostAsync(blogPost);
                _logger.LogInformation("Comment added successfully to blog post: {BlogPostId}", blogPostId);

                return Json(new { success = true, comment = comment });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding comment");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetComments(string blogPostId)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
                var comments = await _cosmosDbService.GetCommentsForBlogPostAsync(blogPostId, userId);
                return PartialView("_CommentsList", comments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading comments");
                return PartialView("_CommentsList", new List<Comment>());
            }
        }

        public async Task<IActionResult> MyImages()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
                var blogPosts = await _cosmosDbService.GetAllBlogPostsAsync(userId);
                
                // Convert blog posts to UserImage models, only including posts with media
                var images = blogPosts
                    .Where(post => !string.IsNullOrEmpty(post.MediaUrl))
                    .Select(post => new UserImage
                    {
                        Id = post.id,
                        UserId = post.AuthorId ?? "",
                        UserName = post.AuthorName ?? "anonymous",
                        ImageUrl = post.MediaUrl,
                        CreatedAt = post.CreatedAt,
                    })
                    .ToList();

                return View(images);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user images");
                ModelState.AddModelError("", "Error loading images. Please try again later.");
                return View(new List<UserImage>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserImages()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                var blogPosts = await _cosmosDbService.GetAllBlogPostsAsync(userId);
                
                // Convert blog posts to UserImage models, only including posts with media
                var images = blogPosts
                    .Where(post => !string.IsNullOrEmpty(post.MediaUrl) && post.MediaType?.StartsWith("image/") == true)
                    .Select(post => new UserImage
                    {
                        Id = post.id,
                        ImageUrl = post.MediaUrl,
                        CreatedAt = post.CreatedAt
                    })
                    .OrderByDescending(img => img.CreatedAt)
                    .ToList();

                return Json(images);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user images");
                return Json(new List<UserImage>());
            }
        }
    }
}
