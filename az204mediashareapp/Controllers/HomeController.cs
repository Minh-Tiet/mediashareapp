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
using MVCMediaShareAppNew.Enums;
using Azure.Messaging.EventGrid;

namespace MVCMediaShareAppNew.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ICosmosDbService _cosmosDbService;
        private readonly IBlobStorageService _blobStorageService;
        private readonly IQueueServiceFactory _queueServiceFactory;
        private readonly IEventGridService _eventGridService;

        public HomeController(ILogger<HomeController> logger,
            ICosmosDbService cosmosDbService,
            IBlobStorageService blobStorageService,
            IQueueServiceFactory queueServiceFactory,
            IEventGridService eventGridService)
        {
            _logger = logger;
            _cosmosDbService = cosmosDbService;
            _blobStorageService = blobStorageService;
            _queueServiceFactory = queueServiceFactory;
            _eventGridService = eventGridService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                var posts = await _cosmosDbService.GetAllBlogPostsAsync();

                // Load comments for each post
                /*foreach (var post in posts)
                {
                    post.Comments = await _cosmosDbService.GetCommentsForBlogPostAsync(post.id, userId);
                }*/

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

            if (!ModelState.IsValid)
            {
                return View(nameof(Index), await _cosmosDbService.GetAllBlogPostsAsync());
            }

            try
            {
                post.id = Guid.NewGuid().ToString();
                post.AuthorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
                post.AuthorName = User.Identity?.Name ?? "anoymous";
                post.CreatedAt = DateTime.UtcNow;

                BlogPost? createdBlogPost = null;
                if (mediaFile != null)
                {
                    var fileName = $"{Guid.NewGuid()}_{Path.GetFileNameWithoutExtension(mediaFile.FileName)}{Path.GetExtension(mediaFile.FileName)}";
                    string baseMediaUrl = await _blobStorageService.UploadFileAsync(mediaFile, fileName);
                    post.MediaType = mediaFile.ContentType;

                    // Generate container-level SAS token and append it to the base URL
                    string sasToken = _blobStorageService.GetSasToken();
                    post.MediaUrl = $"{baseMediaUrl}{sasToken}"; // Append SAS token to the URL
                    post.MediaBlobName = fileName; // Store the base URL for later use
                    post.OriginMediaName = mediaFile.FileName; // Store the original file name

                    var blogCreationTask = await _cosmosDbService.CreateBlogPostAsync(post);
                    createdBlogPost = blogCreationTask.Resource; // Retrieve the BlogPost object
                    _logger.LogInformation("Blog post created successfully: {Title}", createdBlogPost.Title);

                    // Send message to queue for image processing
                    if (!string.IsNullOrEmpty(post.MediaUrl))
                    {
                        var message = new
                        {
                            id = Guid.NewGuid().ToString(),
                            OriginMediaName = createdBlogPost.OriginMediaName,
                            MediaStorageBlobName = createdBlogPost.MediaBlobName,
                            MediaStorageBlobUrlWithSas = createdBlogPost.MediaUrl,
                            MediaType = createdBlogPost.MediaType,
                            AuthorId = createdBlogPost.AuthorId,
                            CreatedAt = DateTime.UtcNow
                        };
                        var storageQueueService = _queueServiceFactory.GetQueueService("Storage");
                        await storageQueueService.SendMessageAsync(JsonSerializer.Serialize(message), "image-processing-queue");
                        _logger.LogInformation("Sent message to Storage queue: image-processing-queue");

                        // TODO: enqueue message to the queue for image processing
                        var sbQueueService = _queueServiceFactory.GetQueueService("ServiceBus");
                        await sbQueueService.SendMessageAsync(JsonSerializer.Serialize(message), "resize-queue");
                        _logger.LogInformation("Sent message to ServiceBus queue: resize-queue");
                    }
                }
                else if (!string.IsNullOrEmpty(selectedImageUrl))
                {
                    // Use the selected image from the sidebar
                    post.MediaUrl = selectedImageUrl;
                    post.MediaType = "image/jpeg"; // Default to JPEG, adjust if needed

                    var blogCreationTask = await _cosmosDbService.CreateBlogPostAsync(post);
                    createdBlogPost = blogCreationTask.Resource; // Retrieve the BlogPost object
                    _logger.LogInformation("Blog post created successfully: {Title}", createdBlogPost.Title);

                }
                else
                {
                    // No media provided, save to Cosmos DB
                    var blogCreationTask = await _cosmosDbService.CreateBlogPostAsync(post);
                    createdBlogPost = blogCreationTask.Resource;
                }

                if (createdBlogPost != null)
                {
                    // Update blog post state to Published
                    _logger.LogInformation("Blog post created successfully: {Title}", createdBlogPost.Title);
                    createdBlogPost.State = Enum.GetName<BlogCreationState>(BlogCreationState.Published);
                    await _cosmosDbService.UpdateBlogPostAsync(createdBlogPost);

                    // Publish EventGrid event
                    var blogPublishedEvent = new EventGridEvent(
                        subject: $"BlogPost/{createdBlogPost.id}",
                        eventType: "BlogCreation.Published",
                        dataVersion: "1.0",
                        data: new
                        {
                            BlogId = createdBlogPost.id,
                            Title = createdBlogPost.Title,
                            AuthorId = createdBlogPost.AuthorId,
                            AuthorName = createdBlogPost.AuthorName,
                            State = createdBlogPost.State,
                            CreatedAt = createdBlogPost.CreatedAt,
                            MediaUrl = createdBlogPost.MediaUrl,
                            MediaType = createdBlogPost.MediaType
                        })
                    {
                        Id = Guid.NewGuid().ToString(), // Required for Microsoft.Azure.EventGrid
                        EventTime = DateTime.UtcNow // Required for Microsoft.Azure.EventGrid
                    };
                    await _eventGridService.SendEventAsync(blogPublishedEvent);
                    _logger.LogInformation("Published EventGrid event for blog post: {BlogId}", createdBlogPost.id);
                }

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
            var posts = await _cosmosDbService.GetAllBlogPostsAsync();
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

        public async Task<IActionResult> MyImages()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Index");
                }

                var mediaStores = await _cosmosDbService.GetAllMediaItemsAsync(userId);
                
                // Convert media items to UserImage models, only including posts with media
                var images = mediaStores
                    .Where(mediaItem => !string.IsNullOrEmpty(mediaItem.MediaStorageBlobUrlWithSas) && mediaItem.MediaType?.StartsWith("image/") == true)
                    .Select(mediaItem => new UserImage
                    {
                        Id = mediaItem.id,
                        UserId = userId,
                        UserName = User.Identity?.Name ?? "Anonymous",
                        ImageUrlWithSas = mediaItem.MediaStorageBlobUrlWithSas,
                        OriginMediaName = mediaItem.OriginMediaName,
                        ImageBlogName = mediaItem.MediaStorageBlobName,
                        CreatedAt = mediaItem.CreatedAt
                    })
                    .OrderByDescending(img => img.CreatedAt)
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
                var blogPost = await _cosmosDbService.GetBlogPostAsync(blogPostId);
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
                
                // TODO: publish event to event grid to notify a comment is added
                var commentEvent = new EventGridEvent(
                    subject: $"Comment/{comment.Id}",
                    eventType: "Comment.Added",
                    dataVersion: "1.0", // Data version
                    data: new
                    {
                        CommentId = comment.Id,
                        BlogPostId = blogPostId,
                        AuthorId = authorId,
                        AuthorName = comment.AuthorName,
                        Text = comment.Text,
                        MediaUrl = comment.MediaUrl,
                        CreatedAt = comment.CreatedAt
                    }
                );
                await _eventGridService.SendEventAsync(commentEvent);
                _logger.LogInformation("Published EventGrid event for comment: {CommentId}", comment.Id);

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

        [HttpGet]
        public async Task<IActionResult> GetUserImages()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                var mediaStores = await _cosmosDbService.GetAllMediaItemsAsync(userId);
                
                // Convert media item to UserImage models, only including posts with media
                var images = mediaStores
                    .Where(mediaItem => !string.IsNullOrEmpty(mediaItem.MediaStorageBlobUrlWithSas) && mediaItem.MediaType?.StartsWith("image/") == true)
                    .Select(mediaItem => new UserImage
                    {
                        Id = mediaItem.id,
                        UserId = userId,
                        UserName = User.Identity?.Name ?? "Anonymous",
                        ImageUrlWithSas = mediaItem.MediaStorageBlobUrlWithSas,
                        OriginMediaName = mediaItem.OriginMediaName,
                        ImageBlogName = mediaItem.MediaStorageBlobName,
                        CreatedAt = mediaItem.CreatedAt
                    })
                    .OrderByDescending(img => img.CreatedAt)
                    .ToList();

                return Json(images);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user images");
                return Json(new { success = false, message = "Error loading images" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUserMedia(string id)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                // Get the media item to get the blob name
                var mediaItems = await _cosmosDbService.GetAllMediaItemsAsync(userId);
                var mediaItem = mediaItems.FirstOrDefault(m => m.id == id);
                
                if (mediaItem == null)
                {
                    return Json(new { success = false, message = "Media item not found" });
                }

                // Delete from Cosmos DB
                await _cosmosDbService.DeleteMediaStoreItem(id, userId);

                // Delete from Blob Storage
                await _blobStorageService.DeleteBlobAsync(mediaItem.MediaStorageBlobName);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user media");
                return Json(new { success = false, message = "Error deleting media" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLike(string blogPostId)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                // Get the blog post
                var blogPost = await _cosmosDbService.GetBlogPostAsync(blogPostId);
                if (blogPost == null)
                {
                    return Json(new { success = false, message = "Blog post not found" });
                }

                bool isLiked = blogPost.LikedBy.Contains(userId);
                if (isLiked)
                {
                    // Unlike: Remove user from LikedBy and decrement LikeCount
                    blogPost.LikedBy.Remove(userId);
                    blogPost.LikeCount = Math.Max(0, blogPost.LikeCount - 1);
                }
                else
                {
                    // Like: Add user to LikedBy and increment LikeCount
                    blogPost.LikedBy.Add(userId);
                    blogPost.LikeCount++;
                }

                // Update the blog post in Cosmos DB
                await _cosmosDbService.UpdateBlogPostAsync(blogPost);
                _logger.LogInformation("{Action} blog post {BlogPostId} by user {UserId}", isLiked ? "Unliked" : "Liked", blogPostId, userId);

                // Publish EventGrid event
                var likeEvent = new EventGridEvent(
                    subject: $"BlogPost/{blogPostId}/Like",
                    eventType: isLiked ? "BlogPost.Unliked" : "BlogPost.Liked",
                    dataVersion: "1.0",
                    data: new
                    {
                        BlogId = blogPostId,
                        UserId = userId,
                        LikeCount = blogPost.LikeCount,
                        Action = isLiked ? "Unliked" : "Liked",
                        Timestamp = DateTime.UtcNow
                    })
                {
                    Id = Guid.NewGuid().ToString(),
                    EventTime = DateTime.UtcNow
                };
                await _eventGridService.SendEventAsync(likeEvent);
                _logger.LogInformation("Published EventGrid event for {Action} on blog post: {BlogId}", isLiked ? "unlike" : "like", blogPostId);

                return Json(new
                {
                    success = true,
                    likeCount = blogPost.LikeCount,
                    isLiked = !isLiked
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling like for blog post {BlogPostId}", blogPostId);
                return Json(new { success = false, message = "Error updating like. Please try again later." });
            }
        }
    }
}
