using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using MVCMediaShareAppNew.Enums;

namespace MVCMediaShareAppNew.Models
{
    public class BlogPost
    {
        [JsonPropertyName("id")]
        public string id { get; set; } = Guid.NewGuid().ToString();
        
        [Required(ErrorMessage = "Title is required")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Title must be between 3 and 100 characters")]
        [JsonPropertyName("title")]
        public string Title { get; set; }
        
        [Required(ErrorMessage = "Content is required")]
        [MinLength(10, ErrorMessage = "Content must be at least 10 characters long")]
        [JsonPropertyName("content")]
        public string Content { get; set; }
        
        [JsonPropertyName("mediaUrl")]
        public string? MediaUrl { get; set; }
        [JsonPropertyName("mediaBlobName")] 
        public string? MediaBlobName { get; set; }

        [JsonPropertyName("originMediaName")]
        public string? OriginMediaName { get; set; }

        [JsonPropertyName("mediaType")]
        public string? MediaType { get; set; }
        
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [JsonPropertyName("authorId")]
        public string? AuthorId { get; set; }
        
        [JsonPropertyName("authorName")]
        public string? AuthorName { get; set; }

        [JsonPropertyName("comments")]
        public List<Comment> Comments { get; set; } = new List<Comment>();

        [JsonPropertyName("state")]
        public string State { get; set; } = Enum.GetName<BlogCreationState>(BlogCreationState.Draft);
    }
} 