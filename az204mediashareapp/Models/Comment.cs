using System;
using System.ComponentModel.DataAnnotations;

namespace MVCMediaShareAppNew.Models
{
    public class Comment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required(ErrorMessage = "Comment text is required")]
        [MinLength(1, ErrorMessage = "Comment must not be empty")]
        public string Text { get; set; }
        
        public string? MediaUrl { get; set; }
        
        public string? MediaType { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [Required]
        public string AuthorId { get; set; } = string.Empty;

        [Required]
        public string? AuthorName { get; set; }

        [Required]
        public string BlogPostId { get; set; }
    }
} 